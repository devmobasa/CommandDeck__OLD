using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

internal sealed partial class ExtensionCommandSession : IAsyncDisposable
{
    private enum Phase { Starting, Ready, Busy, Completed, Closed }

    private readonly CancellationTokenSource _lifetime = new( );
    private readonly TaskCompletionSource _opened = new( TaskCreationOptions.RunContinuationsAsynchronously );
    private Task _run = Task.CompletedTask;
    private readonly ExtensionSessionRunner _runner;
    private readonly ExtensionSessionPublisher _publisher;
    private readonly RemoteConfirmationGate _confirmations;
    internal ExtensionChildRequestRouter Router { get; }
    internal PresentedExtensionView? CurrentView => _view;
    internal ExtensionDebugState? DebugState => _debug?.State;
    internal ExtensionRemoteConfirmation? Confirmation => _confirmations.Current;
    private PresentedExtensionView? _view;
    private int _phase = ( int ) Phase.Starting;
    private int _revoked;
    internal bool Completed => Volatile.Read( ref _phase ) == ( int ) Phase.Completed;
    private readonly ExtensionDevelopment.DebugLease? _debug;
    public bool Debugging => _debug is not null;
    public bool HasTransportFault => _runner.HasTransportFault;

    public ExtensionCommandSession( ExtensionInstallation installation, ExtensionCommand command, string[] features,
        IExtensionSessionHost host, ExtensionDevelopment.DebugLease? debug = null,
        Dictionary<string, JsonElement>? inputs = null )
    {
        Installation = installation;
        Command = command;
        Id = Guid.NewGuid( ).ToString( "N" );
        Lifetime = _lifetime.Token;
        _debug = debug;
        Router = new ExtensionChildRequestRouter( this, host, features );
        Authority = new ExtensionViewAuthority( Id, ExtensionSessionProtocol.ExecutionEpoch, installation.GrantRevision, Router.ResolveAsset );
        _runner = new ExtensionSessionRunner( this, host, features, inputs ?? [], debug );
        _publisher = new ExtensionSessionPublisher( this, host );
        _confirmations = new RemoteConfirmationGate( this );
    }

    public string Id { get; }
    public ExtensionInstallation Installation { get; }
    public ExtensionCommand Command { get; }
    public ExtensionViewAuthority Authority { get; }
    public CancellationToken Lifetime { get; }
    public Task Opened => _opened.Task;

    public void Run( IExtensionLauncher supervisor, string artifact, string entrypoint, Action release ) =>
        _run = Task.Run( ( ) => _runner.RunAsync( supervisor, artifact, entrypoint, release ) );

    public void AdmitCopyEffect( CopyTextRequest request, long grantRevision, IReadOnlySet<string> grants ) =>
        Authority.AdmitCopyEffect( request, grantRevision, grants );
    public void AuthorizeData( CommandDataRequest request, long grantRevision, IReadOnlySet<string> grants ) =>
        Authority.AuthorizeData( request, grantRevision, grants );

    public ExtensionActionAdmission Admit( ExtensionSessionInput input )
    {
        Lifetime.ThrowIfCancellationRequested( );
        if ( Interlocked.CompareExchange( ref _phase, ( int ) Phase.Busy, ( int ) Phase.Ready ) != ( int ) Phase.Ready )
        {
            throw new InvalidOperationException( "Wait for the current extension action to finish." );
        }
        try
        {
            return Authority.AdmitAction( input.Sequence, input.Revision, input.Handle ?? "", string.IsNullOrEmpty( input.ItemId ) ? null : input.ItemId, input.FormRevision, input.Values );
        }
        catch
        {
            FinishAction( );
            throw;
        }
    }

    public async ValueTask BackAsync( CancellationToken cancellationToken )
    {
        _view = Authority.Pop( );
        await PublishStatusAsync( ExtensionSessionStatuses.View, null, cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask CallbackAsync( ExtensionActionAdmission admission, CancellationToken cancellationToken )
    {
        var invocation = Authority.BeginInvocation( InvocationOrigin.UserAction );
        try
        {
            var response = await _runner.CallAsync( ExtensionMethods.CommandEvent, JsonSerializer.SerializeToElement(
                new CommandEventRequest( invocation.Generation, invocation.Id, admission.ViewId, admission.Revision,
                    CallbackEvent( admission ) ),
                ExtensionViewJsonContext.Default.CommandEventRequest ), cancellationToken ).ConfigureAwait( false );
            if ( response.Deserialize( ExtensionViewJsonContext.Default.CommandOutcome )?.Outcome != ExtensionCommandOutcomes.Completed )
            {
                throw new InvalidDataException( "Invalid callback outcome." );
            }
        }
        finally
        {
            Authority.CompleteInvocation( invocation );
        }
    }

    private static CommandEvent CallbackEvent( ExtensionActionAdmission admission ) => admission.Binding switch
    {
        SubmitBinding => admission.Submission!,
        PageBinding page => new PageRequested( page.Token ),
        CallbackBinding callback => new ActionInvoked( callback.ActionId, admission.ItemId ),
        _ => throw new InvalidOperationException( "The binding is not a callback." )
    };

    public void FinishAction( ) => Interlocked.CompareExchange( ref _phase, ( int ) Phase.Ready, ( int ) Phase.Busy );
    public void Revoke( )
    {
        Authority.Revoke( );
        if ( Interlocked.Exchange( ref _revoked, 1 ) == 0 )
        {
            if ( !Completed )
            {
                Interlocked.Exchange( ref _phase, ( int ) Phase.Closed );
            }
            _lifetime.Cancel( );
        }
    }
    public ValueTask DisposeAsync( ) => CloseAsync( );
    public async ValueTask CloseAsync( string? error = null )
    {
        if ( error is not null )
        {
            _runner.SetError( error );
        }
        Revoke( );
        await _run.ConfigureAwait( false );
    }

    internal void MarkCompleted( ) => Interlocked.CompareExchange( ref _phase, ( int ) Phase.Completed, ( int ) Phase.Starting );
    internal void MarkReady( ) => Interlocked.CompareExchange( ref _phase, ( int ) Phase.Ready, ( int ) Phase.Starting );
    internal void MarkOpened( ) => _opened.TrySetResult( );
    internal void SetView( PresentedExtensionView view ) => _view = view;
    internal void NotifyStopped( string? error )
    {
        _lifetime.Dispose( );
        if ( Completed && error is null )
        {
            _opened.TrySetResult( );
        }
        _opened.TrySetException( new InvalidOperationException( error ?? "The extension was closed before opening." ) );
    }
    public ValueTask PublishStatusAsync( string status, string? error, CancellationToken cancellationToken ) =>
        _publisher.PublishAsync( status, error, cancellationToken );
    public void AuthorizeBroker( BrokerRequest request, long revision, IReadOnlySet<string> grants ) =>
        Authority.AuthorizeBroker( request, revision, grants );
    public ValueTask<bool> ConfirmRemoteAsync( ExtensionRemoteConfirmation confirmation, CancellationToken token ) => _confirmations.ConfirmAsync( confirmation, token );
    public void DecideRemote( ExtensionSessionInput input ) => _confirmations.Decide( input );
}
