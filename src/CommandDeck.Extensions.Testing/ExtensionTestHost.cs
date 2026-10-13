using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Extensions.Testing;

/// <summary>Runs the real SDK over in-memory pipes. This author test helper does not emulate process isolation.</summary>
public sealed partial class ExtensionTestHost : IAsyncDisposable
{
    private static readonly JsonElement Empty = JsonElement.Parse( "{}" );
    private readonly ExtensionTestHostOptions _options;
    private readonly IExtensionCommand _command;
    private readonly MemoryPipe _toHost = new( );
    private readonly MemoryPipe _toChild = new( );
    private readonly CancellationTokenSource _lifetime = new( );
    private readonly SemaphoreSlim _invocations = new( 1, 1 );
    private readonly object _gate = new( );
    private readonly List<ExtensionView> _parents = [];
    private readonly List<string> _copied = [];
    private readonly Dictionary<string, JsonElement> _storage;
    private ExtensionPeer? _peer;
    private Task? _sdk;
    private readonly string _session = Guid.NewGuid( ).ToString( "N" );
    private long _generation, _viewRevision, _storageRevision, _formRevision;
    private string _invocation = "";
    private bool _active;
    private int _disposed;
    private ExtensionView? _view;
    public ExtensionView? CurrentView { get { lock ( _gate ) { return _view; } } }
    public IReadOnlyList<string> CopiedText { get { lock ( _gate ) { return _copied.ToArray( ); } } }

    private ExtensionTestHost( IExtensionCommand command, ExtensionTestHostOptions options )
    {
        _command = command;
        _options = options;
        _storage = Clone( options.Storage );
    }

    public static async ValueTask<ExtensionTestHost> StartAsync( IExtensionCommand command, ExtensionTestHostOptions? options = null )
    {
        ArgumentNullException.ThrowIfNull( command );
        ViewValidation.ValidateId( command.Id );
        if ( command.Mode is not (ExtensionCommandModes.View or ExtensionCommandModes.NoView) )
        {
            throw new ArgumentException( "Unsupported command mode.", nameof( command ) );
        }

        var host = new ExtensionTestHost( command, options ?? new( ) );
        try
        {
            await host.StartAsync( ).ConfigureAwait( false );
            return host;
        }
        catch
        {
            await host.DisposeAsync( ).ConfigureAwait( false );
            throw;
        }
    }

    private async Task StartAsync( )
    {
        _peer = new( _toHost, _toChild, true, [ExtensionMethods.ViewsReplace, ExtensionMethods.ViewsPush, ExtensionMethods.CommandData,
            ExtensionMethods.CopyText, ExtensionMethods.CommandBroker], HandleAsync );
        _peer.Start( );
        _sdk = ExtensionApp.RunAsync( _toChild, _toHost, [_command], _lifetime.Token );
        var request = new InitializeRequest( new( ), _session, ExtensionSessionProtocol.ExecutionEpoch,
            _command.Id, ExtensionPurposes.Interactive, _options.HostFeatures.ToArray( ), new( ) );
        await CallAsync( ExtensionMethods.Initialize, JsonSerializer.SerializeToElement( request, ExtensionJsonContext.Default.InitializeRequest ) ).ConfigureAwait( false );
        await CallAsync( ExtensionMethods.Initialized, Empty ).ConfigureAwait( false );
        if ( _options.HostFeatures.Contains( ExtensionFeatures.ErrorCodes ) )
        {
            _peer.EnableErrorData( );
        }
    }

    public ValueTask OpenAsync( IReadOnlyDictionary<string, JsonElement>? inputs = null ) => InvokeAsync( null, inputs );
    public ValueTask InvokeAsync( string actionId, string? itemId = null ) => ActivateAsync( actionId, itemId );
    private async ValueTask ActivateAsync( string actionId, string? itemId )
    {
        var view = CurrentView;
        if ( view is null )
        {
            throw new InvalidOperationException( "No view is open." );
        }
        var actions = itemId is null ? view.Actions : (( ListView ) view).Items.Single( item => item.Id == itemId ).Actions;
        var action = actions.Single( value => value.Id == actionId );
        switch ( action )
        {
            case CallbackAction:
                await InvokeAsync( new ActionInvoked( actionId, itemId ), null ).ConfigureAwait( false );
                break;
            case CopyTextAction copy:
                RequireGrant( ExtensionPermissionIds.ClipboardWrite );
                lock ( _gate )
                {
                    _copied.Add( copy.Text );
                }

                break;
            case OpenUrlAction:
                RequireGrant( ExtensionPermissionIds.DesktopOpenUrl );
                break;
        }
    }

    public ValueTask SubmitAsync( IReadOnlyDictionary<string, JsonElement> values )
    {
        if ( CurrentView is not FormView form )
        {
            throw new InvalidOperationException( "No form is open." );
        }
        var submission = Clone( values );
        FormValidation.ValidateSubmission( form.Fields, submission );
        return InvokeAsync( new FormSubmitted( form.Submit.Id, ++_formRevision, submission ), null );
    }
    public ValueTask NextPageAsync( )
    {
        if ( CurrentView is not ListView { NextPage: { } token } )
        {
            throw new InvalidOperationException( "No next page." );
        }
        return InvokeAsync( new PageRequested( token ), null );
    }
    public ValueTask BackAsync( )
    {
        lock ( _gate )
        {
            if ( _parents.Count == 0 )
            {
                throw new InvalidOperationException( "No parent view." );
            }
            _view = _parents[^1];
            _parents.RemoveAt( _parents.Count - 1 );
            _viewRevision++;
        }
        return ValueTask.CompletedTask;
    }

    private async ValueTask InvokeAsync( CommandEvent? commandEvent, IReadOnlyDictionary<string, JsonElement>? inputs )
    {
        await _invocations.WaitAsync( _lifetime.Token ).ConfigureAwait( false );
        try
        {
            _generation++;
            _invocation = Guid.NewGuid( ).ToString( "N" );
            _active = true;
            if ( commandEvent is null )
            {
                var request = new CommandOpenRequest( _generation, _invocation, Clone( inputs ?? new Dictionary<string, JsonElement>( ) ) );
                await CallAsync( ExtensionMethods.CommandOpen, JsonSerializer.SerializeToElement( request, ExtensionViewJsonContext.Default.CommandOpenRequest ) ).ConfigureAwait( false );
            }
            else
            {
                var request = new CommandEventRequest( _generation, _invocation, _view!.Id, _viewRevision, commandEvent );
                await CallAsync( ExtensionMethods.CommandEvent, JsonSerializer.SerializeToElement( request, ExtensionViewJsonContext.Default.CommandEventRequest ) ).ConfigureAwait( false );
            }
        }
        finally { _active = false; _invocations.Release( ); }
    }
    private ValueTask<JsonElement> CallAsync( string method, JsonElement value ) => _peer!.CallAsync( method, value, _lifetime.Token );
    private static Dictionary<string, JsonElement> Clone( IReadOnlyDictionary<string, JsonElement> values ) =>
        values.ToDictionary( pair => pair.Key, pair => pair.Value.Clone( ), StringComparer.Ordinal );
    private void RequireGrant( string permission )
    {
        if ( !_options.GrantedPermissions.Contains( permission ) )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.PermissionDenied );
        }
    }
    private void RequireInvocation( string session, long epoch, long generation, string invocation )
    {
        if ( !_active || session != _session || epoch != ExtensionSessionProtocol.ExecutionEpoch || generation != _generation || invocation != _invocation )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.StaleSession );
        }
    }
    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        await _lifetime.CancelAsync( ).ConfigureAwait( false );
        if ( _peer is not null )
        {
            await _peer.DisposeAsync( ).ConfigureAwait( false );
        }
        _toHost.Dispose( );
        _toChild.Dispose( );
        if ( _sdk is not null )
        {
            try
            { await _sdk.WaitAsync( TimeSpan.FromSeconds( 2 ) ).ConfigureAwait( false ); }
            catch ( Exception exception ) when ( exception is OperationCanceledException or IOException or TimeoutException ) { }
        }
        _invocations.Dispose( );
        _lifetime.Dispose( );
    }
}
