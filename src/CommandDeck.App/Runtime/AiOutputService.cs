using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

public interface IAtomicFileExchange
{
    void Exchange( string firstPath, string secondPath );
}

public sealed class AiOutputService : IDisposable
{
    private readonly AiGenerationCoordinator _generations;
    private readonly IActionExecutor _actions;
    private readonly ISavedOutputOpener _opener;
    private readonly IAtomicFileExchange _atomicExchange;
    private readonly AiOverwriteConfirmationStore _confirmations;
    private readonly AiSavedOutputStore _savedFiles = new( );
    private readonly SemaphoreSlim _saveGate = new( 1, 1 );
    private readonly Func<string, long, string?>? _additionalOutputSource;

    public AiOutputService(
        AiGenerationCoordinator generations,
        IActionExecutor actions,
        TimeProvider? clock = null,
        IAtomicFileExchange? atomicExchange = null,
        Func<string, long, string?>? additionalOutputSource = null,
        ISavedOutputOpener? opener = null )
    {
        _generations = generations;
        _actions = actions;
        _opener = opener ?? new DesktopPortalFileOpener( );
        _confirmations = new AiOverwriteConfirmationStore( clock ?? TimeProvider.System );
        _atomicExchange = atomicExchange ?? LinuxAtomicFileExchange.Instance;
        _additionalOutputSource = additionalOutputSource;
    }

    public async ValueTask<ActionExecutionResult> ExecuteAsync(
        string clientOwnerId,
        long generation,
        string action,
        string? targetPath,
        string? confirmationToken,
        CancellationToken cancellationToken )
    {
        if ( !_generations.TryGetOutput( clientOwnerId, generation, out var output ) ||
            output is null )
        {
            output = _additionalOutputSource?.Invoke( clientOwnerId, generation );
        }
        if ( output is null )
        {
            return ActionExecutionResult.Failure( "The ephemeral AI output is unavailable." );
        }

        return action switch
        {
            "copy" => await CopyAsync( generation, output, cancellationToken ).ConfigureAwait( false ),
            "save" => await SaveAsync(
                clientOwnerId,
                generation,
                output,
                targetPath,
                confirmationToken,
                cancellationToken ).ConfigureAwait( false ),
            "open" => await OpenAsync( clientOwnerId, generation, cancellationToken )
                .ConfigureAwait( false ),
            _ => ActionExecutionResult.Failure( "The AI output action is unavailable." )
        };
    }

    public void ClearClient( string clientOwnerId )
    {
        _confirmations.ClearClient( clientOwnerId );
        _savedFiles.ClearClient( clientOwnerId );
    }

    private async ValueTask<ActionExecutionResult> CopyAsync(
        long generation,
        string output,
        CancellationToken cancellationToken ) => await _actions.ExecuteAsync(
            AiOutputResult( generation ),
            new CommandAction
            {
                Id = "copy",
                Title = "Copy",
                Kind = ActionKinds.CopyText,
                Arguments = new Dictionary<string, string>( StringComparer.Ordinal )
                {
                    ["text"] = output
                }
            },
            string.Empty,
            cancellationToken ).ConfigureAwait( false );

    private async ValueTask<ActionExecutionResult> SaveAsync(
        string clientOwnerId,
        long generation,
        string output,
        string? targetPath,
        string? confirmationToken,
        CancellationToken cancellationToken )
    {
        await _saveGate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            return await SaveCoreAsync(
                clientOwnerId,
                generation,
                output,
                targetPath,
                confirmationToken,
                cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _saveGate.Release( );
        }
    }

    private async ValueTask<ActionExecutionResult> SaveCoreAsync(
        string clientOwnerId,
        long generation,
        string output,
        string? targetPath,
        string? confirmationToken,
        CancellationToken cancellationToken )
    {
        if ( string.IsNullOrWhiteSpace( targetPath ) )
        {
            return ActionExecutionResult.Failure( "Choose a local file path before saving." );
        }

        string? temporary = null;
        string? confirmedTargetIdentity = null;
        var deleteTemporaryOnExit = true;
        try
        {
            var path = Path.GetFullPath( targetPath );
            if ( Directory.Exists( path ) || new FileInfo( path ).LinkTarget is not null )
            {
                return ActionExecutionResult.Failure(
                    "The AI output target must be a regular file path." );
            }

            var outputDigest = Convert.ToHexString(
                SHA256.HashData( Encoding.UTF8.GetBytes( output ) ) ).ToLowerInvariant( );
            if ( File.Exists( path ) )
            {
                using var existing = SafeRegularFile.Open( path );
                if ( !_confirmations.Consume(
                        confirmationToken,
                        clientOwnerId,
                        generation,
                        path,
                        outputDigest,
                        existing.ReplacementIdentityToken ) )
                {
                    return ActionExecutionResult.Failure(
                        "The target exists; confirm overwrite to continue.",
                        confirmationToken: _confirmations.Create(
                            clientOwnerId,
                            generation,
                            path,
                            outputDigest,
                            existing.ReplacementIdentityToken ) );
                }
                confirmedTargetIdentity = existing.ReplacementIdentityToken;
            }

            var directory = Path.GetDirectoryName( path );
            if ( directory is null || !Directory.Exists( directory ) )
            {
                return ActionExecutionResult.Failure( "The output directory does not exist." );
            }

            temporary = Path.Combine( directory, $".commanddeck-ai-{Guid.NewGuid( ):N}.tmp" );
            await using ( var stream = SafeRegularFile.CreatePrivateWriteStream( temporary ) )
            {
                var bytes = Encoding.UTF8.GetBytes( output );
                await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
                await stream.FlushAsync( cancellationToken ).ConfigureAwait( false );
                stream.Flush( flushToDisk: true );
            }

            if ( confirmedTargetIdentity is null )
            {
                try
                {
                    File.Move( temporary, path, overwrite: false );
                    temporary = null;
                }
                catch ( IOException ) when ( File.Exists( path ) )
                {
                    using var appeared = SafeRegularFile.Open( path );
                    return ActionExecutionResult.Failure(
                        "The target appeared before save; confirm overwrite to continue.",
                        confirmationToken: _confirmations.Create(
                            clientOwnerId,
                            generation,
                            path,
                            outputDigest,
                            appeared.ReplacementIdentityToken ) );
                }
            }
            else
            {
                var stagedPath = temporary;
                _atomicExchange.Exchange( stagedPath, path );
                deleteTemporaryOnExit = false;
                var restoreRequired = true;
                try
                {
                    using var displaced = SafeRegularFile.Open( stagedPath );
                    if ( !string.Equals(
                            displaced.ReplacementIdentityToken,
                            confirmedTargetIdentity,
                            StringComparison.Ordinal ) )
                    {
                        _atomicExchange.Exchange( stagedPath, path );
                        deleteTemporaryOnExit = true;
                        restoreRequired = false;
                        using var changed = SafeRegularFile.Open( path );
                        return ActionExecutionResult.Failure(
                            "The target changed before save; confirm overwrite again.",
                            confirmationToken: _confirmations.Create(
                                clientOwnerId,
                                generation,
                                path,
                                outputDigest,
                                changed.ReplacementIdentityToken ) );
                    }

                    File.Delete( stagedPath );
                    temporary = null;
                    restoreRequired = false;
                }
                finally
                {
                    if ( restoreRequired )
                    {
                        _atomicExchange.Exchange( stagedPath, path );
                        deleteTemporaryOnExit = true;
                    }
                }
            }
            var identity = SafeRegularFile.Open( path );
            _savedFiles.Store( clientOwnerId, generation, path, identity );
            return ActionExecutionResult.Success( "AI output saved." );
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            throw;
        }
        catch ( Exception exception ) when ( exception is
            IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException )
        {
            return ActionExecutionResult.Failure( "The AI output could not be saved safely." );
        }
        finally
        {
            if ( temporary is not null && deleteTemporaryOnExit )
            {
                try
                {
                    File.Delete( temporary );
                }
                catch ( Exception exception ) when (
                    exception is IOException or UnauthorizedAccessException )
                {
                }
            }
        }
    }

    private async ValueTask<ActionExecutionResult> OpenAsync(
        string clientOwnerId,
        long generation,
        CancellationToken cancellationToken )
    {
        SafeRegularFile? retained = null;
        string? originalPath = null;
        _savedFiles.TryDuplicate( clientOwnerId, generation, out retained, out originalPath );
        if ( retained is null || originalPath is null )
        {
            return ActionExecutionResult.Failure( "Save the AI output before opening it." );
        }

        using ( retained )
        {
            try
            {
                using var current = SafeRegularFile.Open( originalPath );
                if ( !retained.HasSameIdentity( current ) )
                {
                    return ActionExecutionResult.Failure(
                        "The saved AI output changed; save it again before opening." );
                }

                return await _opener.OpenAsync( retained, cancellationToken ).ConfigureAwait( false );
            }
            catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
            {
                throw;
            }
            catch ( Exception exception ) when ( exception is
                IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or NotSupportedException )
            {
                return ActionExecutionResult.Failure(
                    "The saved AI output changed; save it again before opening." );
            }
        }
    }

    private static SearchResult AiOutputResult( long generation ) => new( )
    {
        Id = $"ai-output:{generation}",
        Kind = ResultKinds.Status,
        Title = "AI output",
        ProviderId = ProviderIds.Ai,
        SourceId = SourceIds.Ai
    };

    public void Dispose( )
    {
        _saveGate.Dispose( );
        _confirmations.Clear( );
        _savedFiles.Dispose( );
    }

    private sealed class LinuxAtomicFileExchange : IAtomicFileExchange
    {
        public static LinuxAtomicFileExchange Instance { get; } = new( );

        public void Exchange( string firstPath, string secondPath ) =>
            SafeRegularFile.ExchangePaths( firstPath, secondPath );
    }
}
