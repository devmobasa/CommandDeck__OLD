using System.Text;

namespace CommandDeck;

/// <summary>
/// One conversation with a CLI agent. Each turn is a fresh sandboxed process;
/// the CLI's own session ID carries the transcript between turns through the
/// owned home. There is no long-lived process to keep warm.
/// </summary>
public sealed class CliAgentSession : IAgentSession
{
    private static readonly TimeSpan IdleBound = TimeSpan.FromSeconds( 120 );
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds( 10 );
    private readonly CliAgentBackend _backend;
    private readonly string _developerInstructions;
    private readonly long? _admissionEpoch;
    private ICliAgentProcess? _activeProcess;
    private int _turnActive;
    private int _closed;

    internal CliAgentSession( CliAgentBackend backend, AgentSessionOptions options )
    {
        _backend = backend;
        _developerInstructions = options.DeveloperInstructions;
        SessionId = Guid.NewGuid( ).ToString( "N" );
        ModelId = options.ModelId;
        ReasoningEffort = options.ReasoningEffort;
        _admissionEpoch = options.Admission?.Epoch;
    }

    public string SessionId { get; }

    public string? ModelId { get; private set; }

    public string? ReasoningEffort { get; private set; }

    /// <summary>The CLI's own session identifier once the first turn reported it.</summary>
    public string? ContinuationId { get; private set; }

    public bool IsFaulted => _closed != 0;

    // Each turn starts and owns its own process; there is no shared generation.
    public string ProcessGeneration => string.Empty;

    public async ValueTask RunTurnAsync( AgentTurnInput input, IAgentTurnHost host, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( input );
        ArgumentNullException.ThrowIfNull( host );
        if ( _closed != 0 )
        {
            throw new AiOperationException( AiErrorCategories.Unavailable, safeSummary: "The agent session is closed." );
        }
        if ( Interlocked.CompareExchange( ref _turnActive, 1, 0 ) != 0 )
        {
            throw new AiOperationException( AiErrorCategories.Busy, safeSummary: "The agent session already has an active turn." );
        }
        try
        {
            ModelId = input.ModelId ?? ModelId;
            ReasoningEffort = input.ReasoningEffort ?? ReasoningEffort;
            await host.OnStatusAsync( "Starting " + _backend.Profile.DisplayName, cancellationToken ).ConfigureAwait( false );
            await RunProcessAsync( input.Text, host, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            Interlocked.Exchange( ref _turnActive, 0 );
        }
    }

    private async ValueTask RunProcessAsync( string prompt, IAgentTurnHost host, CancellationToken cancellationToken )
    {
        var binary = _backend.Locate( );
        _backend.Home.Ensure( );
        var scratch = _backend.Home.CreateScratch( );
        try
        {
            string? promptFile = null;
            if ( _backend.Profile.PromptFromFile )
            {
                promptFile = Path.Combine( scratch, "prompt.txt" );
                using var stream = SafeRegularFile.CreatePrivateWriteStream( promptFile );
                stream.Write( Encoding.UTF8.GetBytes( prompt ) );
                stream.Flush( flushToDisk: true );
            }
            var argv = _backend.Profile.BuildTurnArguments( new CliAgentTurnRequest
            {
                Prompt = prompt,
                ModelId = ModelId,
                ReasoningEffort = ReasoningEffort,
                ContinuationId = ContinuationId,
                SystemPrompt = _developerInstructions,
                PromptFilePath = promptFile
            } );
            await using var process = await _backend.Launcher.LaunchAsync( new CliAgentLaunchRequest
            {
                BinaryPath = binary.CanonicalPath,
                InstallPath = binary.InstallPath,
                HomePath = _backend.Home.Path,
                WorkingDirectory = scratch,
                Arguments = argv,
                StandardInput = _backend.Profile.PromptOnStandardInput
            }, cancellationToken ).ConfigureAwait( false );
            _activeProcess = process;
            _backend.RegisterTurn( process, _admissionEpoch );
            try
            {
                if ( _backend.Profile.PromptOnStandardInput )
                {
                    await process.Input.WriteAsync( Encoding.UTF8.GetBytes( prompt ), cancellationToken ).ConfigureAwait( false );
                    await process.Input.FlushAsync( cancellationToken ).ConfigureAwait( false );
                    process.Input.Close( );
                }
                await ConsumeAsync( process, host, cancellationToken ).ConfigureAwait( false );
            }
            catch ( OperationCanceledException )
            {
                process.Terminate( );
                throw;
            }
            finally
            {
                _backend.UnregisterTurn( process );
                _activeProcess = null;
            }
        }
        finally
        {
            CliAgentBackend.TryDeleteDirectory( scratch );
        }
    }

    private async ValueTask ConsumeAsync( ICliAgentProcess process, IAgentTurnHost host, CancellationToken cancellationToken )
    {
        CliAgentStreamReader reader = new( _backend.Profile.StreamFormat );
        BoundedLineReader lines = new( process.Output, AgentLimits.MaximumFrameBytes );
        var streamedAnything = false;
        CliAgentStreamEvent? terminal = null;
        while ( true )
        {
            string? line;
            using var idle = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            idle.CancelAfter( IdleBound );
            try
            {
                line = await lines.ReadLineAsync( idle.Token ).ConfigureAwait( false );
            }
            catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested )
            {
                process.Terminate( );
                throw new AiOperationException( AiErrorCategories.Timeout, safeSummary: "The agent produced no output for two minutes." );
            }
            catch ( Exception exception ) when ( exception is PayloadTooLargeException or InvalidDataException )
            {
                process.Terminate( );
                throw new AiOperationException( AiErrorCategories.MalformedResponse, safeSummary: "The agent emitted an unusable stream frame." );
            }
            if ( line is null )
            {
                break;
            }
            var streamEvent = reader.Read( line );
            if ( streamEvent is null )
            {
                continue;
            }
            if ( streamEvent.SessionId is { Length: > 0 } session )
            {
                ContinuationId = session;
            }
            if ( streamEvent.Status is { } status )
            {
                await host.OnStatusAsync( status, cancellationToken ).ConfigureAwait( false );
            }
            if ( streamEvent.Reasoning is { Length: > 0 } reasoning )
            {
                await host.OnReasoningAsync( reasoning, cancellationToken ).ConfigureAwait( false );
            }
            if ( streamEvent.Text is { Length: > 0 } text )
            {
                streamedAnything = true;
                await host.OnTextAsync( text, cancellationToken ).ConfigureAwait( false );
            }
            if ( streamEvent.Usage is { } usage )
            {
                await host.OnUsageAsync( usage, cancellationToken ).ConfigureAwait( false );
            }
            if ( streamEvent.Terminal )
            {
                terminal = streamEvent;
                if ( streamEvent.FinalText is { Length: > 0 } finalText && !streamedAnything )
                {
                    streamedAnything = true;
                    await host.OnTextAsync( finalText, cancellationToken ).ConfigureAwait( false );
                }
            }
        }
        int exitCode;
        try
        {
            exitCode = await process.Exited.WaitAsync( ExitGrace, cancellationToken ).ConfigureAwait( false );
        }
        catch ( TimeoutException )
        {
            process.Terminate( );
            exitCode = -1;
        }
        if ( terminal is { IsError: true } )
        {
            var error = new AiError
            {
                Category = terminal.ErrorCategory ?? AiErrorCategories.Unavailable,
                Summary = terminal.ErrorSummary ?? "The agent turn failed."
            };
            _backend.ReportTurnError( error );
            throw new AiOperationException( error.Category, safeSummary: error.Summary );
        }
        if ( terminal is null && !streamedAnything )
        {
            var category = exitCode == 0 ? AiErrorCategories.MalformedResponse : CliAgentStreamReader.ClassifyResult( string.Empty, process.Diagnostics );
            var summary = category == AiErrorCategories.Authentication
                ? "The agent is not signed in. Sign in and try again."
                : exitCode == 0
                    ? "The agent exited without a result."
                    : "The agent process exited before answering.";
            _backend.ReportTurnError( new AiError { Category = category, Summary = summary } );
            throw new AiOperationException( category, safeSummary: summary );
        }
        _backend.ReportTurnError( null );
    }

    public ValueTask InterruptAsync( CancellationToken cancellationToken )
    {
        _activeProcess?.Terminate( );
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync( )
    {
        Interlocked.Exchange( ref _closed, 1 );
        _activeProcess?.Terminate( );
        return ValueTask.CompletedTask;
    }
}
