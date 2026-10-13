using System.Collections.Concurrent;
using System.Text;

namespace CommandDeck;

public sealed class AiGenerationCoordinator : IDisposable
{
    private readonly AiPreflightService _preflights;
    private readonly SemaphoreSlim _capacity = new( 2, 2 );
    private readonly ConcurrentDictionary<string, ActiveAiGenerationCancellation> _activeClients =
        new( StringComparer.Ordinal );
    // Provider-bound view of the active generations so a settings mutation can
    // cancel exactly the streams whose definition changed and await their exit.
    private readonly ConcurrentDictionary<string, ActiveProviderGeneration> _activeProviders =
        new( StringComparer.Ordinal );
    private readonly AiOutputCache _outputs = new( maximumEntries: 16 );
    private readonly AiOperationDeadlines _deadlines;

    public AiGenerationCoordinator(
        AiPreflightService preflights,
        AiOperationDeadlines? deadlines = null )
    {
        _preflights = preflights;
        _deadlines = deadlines ?? new AiOperationDeadlines( );
        if ( _deadlines.OverallGeneration <= TimeSpan.Zero )
        {
            throw new ArgumentOutOfRangeException(
                nameof( deadlines ), "The AI generation deadline must be positive." );
        }
    }

    public async ValueTask RunAsync(
        string clientOwnerId,
        string preflightId,
        string digest,
        long generation,
        Func<AiChunk, ValueTask> onChunk,
        Func<AiDone, ValueTask> onDone,
        CancellationToken cancellationToken )
    {
        using CancellationTokenSource overallDeadline = new( _deadlines.OverallGeneration );
        using ActiveAiGenerationCancellation activeGeneration = new(
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, overallDeadline.Token ) );
        var generationCancellation = activeGeneration.Token;
        if ( !_activeClients.TryAdd( clientOwnerId, activeGeneration ) )
        {
            throw new InvalidOperationException( "This client already has an active AI generation." );
        }
        var entered = false;
        var terminalDeliveryStarted = false;
        TaskCompletionSource completion = new( TaskCreationOptions.RunContinuationsAsynchronously );
        async ValueTask DeliverTerminalAsync( AiDone terminal )
        {
            terminalDeliveryStarted = true;
            await onDone( terminal ).ConfigureAwait( false );
        }
        try
        {
            entered = await _capacity.WaitAsync( 0, generationCancellation ).ConfigureAwait( false );
            if ( !entered )
            {
                throw new AiOperationException(
                    AiErrorCategories.Busy,
                    safeSummary: "AI generation capacity is busy." );
            }
            var prepared = await _preflights.ConsumeAsync(
                clientOwnerId,
                preflightId,
                digest,
                generation,
                generationCancellation ).ConfigureAwait( false );
            // Hold the registry the preflight was prepared against until the terminal
            // event: a replacement published meanwhile retires it without cutting
            // this stream, and its transports are disposed once the lease is gone.
            using var registryLease = prepared.Registry.AcquireLease( );
            _activeProviders[clientOwnerId] = new ActiveProviderGeneration(
                prepared.Preflight.ProviderId, activeGeneration, completion );
            if ( !prepared.Registry.TryGet( prepared.Preflight.ProviderId, out var provider ) ||
                provider is null )
            {
                throw new InvalidOperationException( "The selected AI provider is unavailable." );
            }
            StringBuilder output = new( );
            var sequence = 0;
            var outputBytes = 0;
            AiUsage? usage = null;
            var finish = "stop";
            await using var events = provider.StreamAsync(
                prepared.ProviderRequest, generationCancellation )
                .GetAsyncEnumerator( generationCancellation );
            bool hasNext;
            using ( await _preflights.AcquireTransportLeaseAsync(
                prepared.AuthorityRevision,
                generationCancellation ).ConfigureAwait( false ) )
            {
                hasNext = await events.MoveNextAsync( ).ConfigureAwait( false );
            }
            while ( hasNext )
            {
                var value = events.Current;
                if ( !string.IsNullOrEmpty( value.Text ) )
                {
                    var bytes = Encoding.UTF8.GetByteCount( value.Text );
                    if ( bytes > AiLimits.MaximumChunkBytes ||
                        outputBytes + bytes > AiLimits.MaximumOutputBytes ||
                        ++sequence > AiLimits.MaximumChunks )
                    {
                        throw new AiOperationException(
                            AiErrorCategories.ResponseTooLarge,
                            safeSummary: "AI provider output exceeded its stream bounds." );
                    }
                    output.Append( value.Text );
                    outputBytes += bytes;
                    await onChunk( new AiChunk
                    {
                        Generation = generation,
                        Sequence = sequence,
                        Text = value.Text,
                        CumulativeOutputBytes = outputBytes
                    } ).ConfigureAwait( false );
                    generationCancellation.ThrowIfCancellationRequested( );
                }
                usage = value.Usage ?? usage;
                finish = value.FinishCategory ?? finish;
                hasNext = await events.MoveNextAsync( ).ConfigureAwait( false );
            }
            generationCancellation.ThrowIfCancellationRequested( );
            _outputs.Store( generation, clientOwnerId, output.ToString( ) );
            await DeliverTerminalAsync( new AiDone
            {
                Generation = generation,
                Outcome = AiGenerationOutcomes.Completed,
                FinishCategory = finish,
                FinalOutputBytes = outputBytes,
                Usage = usage,
                OutputActions = OutputActions( )
            } ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when (
            !terminalDeliveryStarted && overallDeadline.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested )
        {
            await DeliverTerminalAsync( FailedDone(
                generation,
                new TimeoutException( "The AI generation exceeded its overall deadline." ) ) )
                .ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when (
            !terminalDeliveryStarted && generationCancellation.IsCancellationRequested )
        {
            await DeliverTerminalAsync( new AiDone
            {
                Generation = generation,
                Outcome = AiGenerationOutcomes.Cancelled,
                FinishCategory = AiErrorCategories.Cancelled,
                Error = new AiError
                {
                    Category = AiErrorCategories.Cancelled,
                    Summary = "AI generation was cancelled."
                }
            } ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException exception ) when ( !terminalDeliveryStarted )
        {
            await DeliverTerminalAsync( FailedDone( generation, exception ) ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( !terminalDeliveryStarted )
        {
            await DeliverTerminalAsync( FailedDone( generation, exception ) ).ConfigureAwait( false );
        }
        finally
        {
            if ( _activeClients.TryGetValue( clientOwnerId, out var active ) &&
                ReferenceEquals( active, activeGeneration ) )
            {
                _activeClients.TryRemove( clientOwnerId, out _ );
            }
            if ( _activeProviders.TryGetValue( clientOwnerId, out var bound ) &&
                ReferenceEquals( bound.Cancellation, activeGeneration ) )
            {
                _activeProviders.TryRemove( clientOwnerId, out _ );
            }
            completion.TrySetResult( );
            if ( entered )
            {
                _capacity.Release( );
            }
        }
    }

    /// <summary>
    /// Cancels every active direct generation whose provider satisfies
    /// <paramref name="affected"/> and waits, up to <paramref name="bound"/>, for
    /// them to deliver their terminal event. A request that already left the
    /// machine is not retracted; only its stream is stopped.
    /// </summary>
    public async ValueTask<int> CancelAsync(
        Func<string, bool> affected,
        TimeSpan bound,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( affected );
        List<Task> completions = [];
        foreach ( (_, var generation) in _activeProviders )
        {
            if ( affected( generation.ProviderId ) )
            {
                generation.Cancellation.Cancel( );
                completions.Add( generation.Completion.Task );
            }
        }
        if ( completions.Count == 0 )
        {
            return 0;
        }
        try
        {
            await Task.WhenAll( completions ).WaitAsync( bound, cancellationToken ).ConfigureAwait( false );
        }
        catch ( TimeoutException )
        {
            // The streams were signalled; a stalled transport is bounded by its own
            // idle and overall deadlines and cannot hold the settings change back.
        }
        return completions.Count;
    }

    private sealed record ActiveProviderGeneration(
        string ProviderId,
        ActiveAiGenerationCancellation Cancellation,
        TaskCompletionSource Completion );

    private static AiDone FailedDone( long generation, Exception exception )
    {
        var category = AiErrorClassifier.Classify( exception );
        return new AiDone
        {
            Generation = generation,
            Outcome = AiGenerationOutcomes.Failed,
            FinishCategory = category,
            Error = new AiError
            {
                Category = category,
                Summary = exception is AiOperationException categorized
                    ? categorized.SafeSummary
                    : "AI generation failed. Review the provider status and retry with a new preflight."
            }
        };
    }

    public bool TryGetOutput(
        string clientOwnerId,
        long generation,
        out string? output )
    {
        return _outputs.TryGet( clientOwnerId, generation, out output );
    }

    public void ClearClient( string clientOwnerId )
    {
        if ( _activeClients.TryGetValue( clientOwnerId, out var active ) )
        {
            active.Cancel( );
        }
        _outputs.ClearClient( clientOwnerId );
    }

    private static List<CommandAction> OutputActions( ) =>
    [
        new CommandAction { Id = "copy", Title = "Copy", Kind = ActionKinds.CopyText },
        new CommandAction { Id = "save", Title = "Save", Kind = ActionKinds.CopyText },
        new CommandAction { Id = "open", Title = "Open Saved File", Kind = ActionKinds.OpenPath }
    ];

    public void Dispose( )
    {
        _outputs.Clear( );
        _capacity.Dispose( );
    }
}
