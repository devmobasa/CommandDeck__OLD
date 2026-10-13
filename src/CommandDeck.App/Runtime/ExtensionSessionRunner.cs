using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

internal sealed class ExtensionSessionRunner : IAsyncDisposable
{
    private readonly ExtensionCommandSession _session;
    private readonly IExtensionSessionHost _host;
    private readonly string[] _features;
    private readonly Dictionary<string, JsonElement> _inputs;
    private readonly ExtensionDevelopment.DebugLease? _debug;
    private readonly CancellationTokenRegistration _debugCancellation;
    private ExtensionPeer? _peer;
    private string? _error;
    public bool HasTransportFault => _peer?.Fault is not null;
    public void SetError( string error ) => _error = error;
    public ValueTask DisposeAsync( ) => _peer?.DisposeAsync( ) ?? ValueTask.CompletedTask;
    public ExtensionSessionRunner( ExtensionCommandSession session, IExtensionSessionHost host, string[] features,
        Dictionary<string, JsonElement> inputs, ExtensionDevelopment.DebugLease? debug )
    {
        _session = session;
        _host = host;
        _features = features;
        _inputs = inputs;
        _debug = debug;
        if ( debug is not null )
        {
            _debugCancellation = debug.Token.Register( _session.Revoke );
        }
    }
    public ValueTask<JsonElement> CallAsync( string method, JsonElement input, CancellationToken token )
    {
        var peer = _peer;
        if ( peer is null )
        {
            throw new InvalidOperationException( "The extension peer is unavailable." );
        }

        return peer.CallAsync( method, input, token );
    }

    public async Task RunAsync( IExtensionLauncher supervisor, string artifact, string entrypoint, Action release )
    {
        IExtensionProcess? process = null;
        try
        {
            _session.Router.SetArtifact( artifact );
            await _session.PublishStatusAsync( _session.Command.Mode == ExtensionCommandModes.NoView ? ExtensionSessionStatuses.Running : ExtensionSessionStatuses.Starting, null, _session.Lifetime ).ConfigureAwait( false );
            using var startup = CancellationTokenSource.CreateLinkedTokenSource( _session.Lifetime );
            if ( !_session.Debugging )
            {
                startup.CancelAfter( TimeSpan.FromSeconds( 8 ) );
            }
            process = await supervisor.StartAsync( new( artifact, entrypoint, _session.Installation.Key, ExtensionLaunchProfile.Interactive, _debug?.State.SocketPath ), startup.Token ).ConfigureAwait( false );
            _peer = new ExtensionPeer( process.Input, process.Output, true, [ExtensionMethods.ViewsReplace, ExtensionMethods.ViewsPush, ExtensionMethods.CopyText, ExtensionMethods.CommandData, ExtensionMethods.CommandBroker], _session.Router.HandleAsync );
            _peer.Start( );
            using ( var handshake = CancellationTokenSource.CreateLinkedTokenSource( startup.Token ) )
            {
                if ( !_session.Debugging )
                {
                    handshake.CancelAfter( TimeSpan.FromSeconds( 3 ) );
                }
                var response = await _peer.CallAsync( ExtensionMethods.Initialize, JsonSerializer.SerializeToElement(
                                    new InitializeRequest( new( ), _session.Id, ExtensionSessionProtocol.ExecutionEpoch, _session.Command.Id, ExtensionPurposes.Interactive, _features, new( ) ),
                                    ExtensionJsonContext.Default.InitializeRequest ), handshake.Token ).ConfigureAwait( false );
                var initialized = response.Deserialize( ExtensionJsonContext.Default.InitializeResult );
                if ( initialized?.Protocol is not { Major: 1, Minor: 0 } || initialized.Commands is null ||
                     !initialized.Commands.Order( StringComparer.Ordinal ).SequenceEqual( _session.Installation.Manifest.Commands.Select( command => command.Id ).Order( StringComparer.Ordinal ) ) )
                {
                    throw new InvalidDataException( "The extension handshake does not match its installed manifest." );
                }
                if ( initialized.Features is { } supported && supported.Contains( ExtensionFeatures.ErrorCodes, StringComparer.Ordinal ) &&
                    _features.Contains( ExtensionFeatures.ErrorCodes, StringComparer.Ordinal ) )
                {
                    _peer.EnableErrorData( );
                }
            }
            _peer.Notify( ExtensionMethods.Initialized );
            var invocation = _session.Authority.BeginInvocation( InvocationOrigin.Open );
            try
            {
                var result = await _peer.CallAsync( ExtensionMethods.CommandOpen, JsonSerializer.SerializeToElement(
                    new CommandOpenRequest( invocation.Generation, invocation.Id, _inputs ), ExtensionViewJsonContext.Default.CommandOpenRequest ), startup.Token ).ConfigureAwait( false );
                var outcome = result.Deserialize( ExtensionViewJsonContext.Default.CommandOutcome )?.Outcome;
                if ( _session.Command.Mode == ExtensionCommandModes.NoView )
                {
                    if ( outcome != ExtensionCommandOutcomes.Completed || _session.CurrentView is not null )
                    {
                        throw new InvalidDataException( "Invalid no-view completion." );
                    }
                    _session.MarkCompleted( );
                    return;
                }
                if ( outcome != ExtensionCommandOutcomes.OpenedView || _session.CurrentView is null )
                {
                    throw new InvalidDataException( "The extension did not open a supported view." );
                }
            }
            finally
            {
                _session.Authority.CompleteInvocation( invocation );
            }
            _session.Lifetime.ThrowIfCancellationRequested( );
            _session.MarkReady( );
            await _session.PublishStatusAsync( ExtensionSessionStatuses.Ready, null, _session.Lifetime ).ConfigureAwait( false );
            _session.MarkOpened( );
            await _peer.Completion.WaitAsync( _session.Lifetime ).ConfigureAwait( false );
            throw new IOException( "The extension process exited." );
        }
        catch ( OperationCanceledException ) when ( _session.Lifetime.IsCancellationRequested ) { }
        catch ( Exception exception )
        {
            // Child diagnostics and exception payloads can contain secrets or forged UI text.
            _error = exception is PlatformNotSupportedException ? "The restricted runtime is unavailable. Check Bubblewrap and the systemd user resource controllers." :
                "The extension failed or timed out. An effect may already have completed; check before restarting the command.";
            AppLog.Debug( $"Extension session stopped: {exception.GetType( ).Name}" );
        }
        finally
        {
            await FinishAsync( process, release ).ConfigureAwait( false );
        }
    }

    private async Task FinishAsync( IExtensionProcess? process, Action release )
    {
        _host.RecordDiagnostic( new( DateTimeOffset.UtcNow, _session.Id, _session.Command.Id, _session.Installation.Digest,
            _error is not null ? ExtensionSessionStatuses.Failed : _session.Completed ? ExtensionSessionStatuses.Completed : ExtensionSessionStatuses.Closed, process?.ReadDiagnostics( ) ) );
        _session.Revoke( );
        try
        {
            // Stop descendants first, which also unblocks any pending pipe work.
            try
            {
                if ( process is not null )
                {
                    await process.DisposeAsync( ).ConfigureAwait( false );
                }
            }
            finally
            {
                await DisposeAsync( ).ConfigureAwait( false );
            }
        }
        catch ( Exception exception )
        {
            _error = "Extension cleanup failed. Check the user service before reopening.";
            AppLog.Error( "Extension cleanup failed.", exception );
        }
        // Terminal UI can immediately activate another command; release ownership first.
        release( );
        _debugCancellation.Dispose( );
        _debug?.Dispose( );
        try
        {
            await _session.PublishStatusAsync( _error is not null ? ExtensionSessionStatuses.Failed : _session.Completed ? ExtensionSessionStatuses.Completed : ExtensionSessionStatuses.Closed, _error, CancellationToken.None ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or OperationCanceledException or ObjectDisposedException ) { }
        _session.NotifyStopped( _error );
    }
}
