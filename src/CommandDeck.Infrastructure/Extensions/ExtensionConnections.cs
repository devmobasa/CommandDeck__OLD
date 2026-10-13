using System.Text;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

/// <summary>Daemon-lifetime grants. Child input can select only names owned by its installation.</summary>
public sealed class ExtensionConnections : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new( false, true );
    private readonly object _gate = new( );
    private readonly Dictionary<(string Package, string Name), Connection> _connections = [];
    private readonly IRemoteEffectJournal _journal;
    private readonly bool _ownsJournal;
    private readonly IExtensionSecretResolver _secrets;
    public IRemoteEffectJournal Journal => _journal;
    public ExtensionConnections( string journalDirectory, IExtensionSecretResolver secrets ) : this( new FileRemoteEffectJournal( journalDirectory ), secrets ) => _ownsJournal = true;

    public ExtensionConnections( IRemoteEffectJournal journal, IExtensionSecretResolver secrets )
    {
        _journal = journal;
        _secrets = secrets;
    }

    internal sealed class Connection : IDisposable
    {
        public required ConnectionSetupModel Model { get; init; }
        public ExtensionConnectionSetup Setup => Model.ToWire( );
        public ExtensionFileRoot? Files { get; init; }
        public required ICredentialSource Credential { get; init; }
        public CancellationTokenSource Lifetime { get; } = new( );
        public CancellationToken Token { get; }
        public Connection( )
        {
            Token = Lifetime.Token;
        }
        public void Dispose( )
        {
            Lifetime.Cancel( );
            Files?.Dispose( );
            Credential.Dispose( );
            Lifetime.Dispose( );
        }
    }

    public sealed class PreparedConnection : IDisposable
    {
        internal Connection? Value { get; private set; }
        internal PreparedConnection( Connection value ) => Value = value;
        internal Connection Take( )
        {
            var value = Value;
            if ( value is null )
            {
                throw new InvalidOperationException( "The connection has already been transferred." );
            }
            Value = null;
            return value;
        }
        public ValueTask<string> DisconnectAsync( bool remoteRevoke, CancellationToken token )
        {
            var value = Value;
            if ( value is null )
            {
                throw new InvalidOperationException( "The connection is unavailable." );
            }
            return value.Credential.DisconnectAsync( remoteRevoke, token );
        }
        public void Dispose( )
        {
            Value?.Dispose( );
            Value = null;
        }
    }

    public async ValueTask<PreparedConnection> PrepareAsync( ExtensionConnectionSetup setup, CancellationToken token )
    {
        var model = ConnectionSetupConversion.ToModel( setup );
        var credential = await CredentialSourceFactory.CreateAsync( model, _secrets, token ).ConfigureAwait( false );
        try
        {
            var connection = new Connection
            {
                Model = model.Policy,
                Credential = credential,
                Files = model.Policy is FileConnectionModel file ? new ExtensionFileRoot( file.Path ) : null
            };
            return new( connection );
        }
        catch
        {
            credential.Dispose( );
            throw;
        }
    }

    public void Install( string package, PreparedConnection prepared, CancellationToken token )
    {
        lock ( _gate )
        {
            token.ThrowIfCancellationRequested( );
            var value = prepared.Value;
            if ( value is null )
            {
                throw new InvalidOperationException( "The connection is unavailable." );
            }
            var key = (package, value.Setup.Name);
            if ( _connections.Count >= 128 && !_connections.ContainsKey( key ) )
            {
                throw new InvalidOperationException( "The connection limit was reached." );
            }
            if ( _connections.Remove( key, out var previous ) )
            {
                previous.Dispose( );
            }
            _connections.Add( key, prepared.Take( ) );
        }
    }

    public async ValueTask ConfigureAsync( string package, ExtensionConnectionSetup setup, CancellationToken token )
    {
        using var prepared = await PrepareAsync( setup, token ).ConfigureAwait( false );
        Install( package, prepared, token );
    }

    public ExtensionConnectionSummary[] List( string package )
    {
        lock ( _gate )
        {
            return _connections.Where( pair => pair.Key.Package == package ).Select( pair => new ExtensionConnectionSummary( pair.Key.Name,
            pair.Value.Setup.Kind, pair.Value.Setup.Kind == ExtensionConnectionKinds.Files ? pair.Value.Setup.Path! : pair.Value.Setup.Origin + pair.Value.Setup.Route + " · " + string.Join( ", ", pair.Value.Setup.Methods ),
            pair.Value.Credential.Status ) ).OrderBy( value => value.Name, StringComparer.Ordinal ).ToArray( );
        }
    }

    internal Connection Find( string package, string name )
    {
        lock ( _gate )
        {
            var result = _connections.GetValueOrDefault( (package, name) );
            if ( result is null )
            {
                throw new InvalidOperationException( "Configure this installation's named connection first." );
            }

            return result;
        }
    }

    public PreparedConnection Detach( string package, string name )
    {
        lock ( _gate )
        {
            if ( !_connections.Remove( (package, name), out var connection ) )
            {
                throw new InvalidOperationException( "The connection is unavailable." );
            }
            connection.Lifetime.Cancel( );
            return new( connection );
        }
    }

    public async ValueTask<string> DisconnectAsync( string package, string name, bool remoteRevoke, CancellationToken token )
    {
        using var detached = Detach( package, name );
        return await detached.DisconnectAsync( remoteRevoke, token ).ConfigureAwait( false );
    }
    public sealed class PendingLogin
    {
        private readonly Connection _connection;
        internal PendingLogin( Connection connection ) => _connection = connection;
        public CancellationToken Lifetime => _connection.Token;
        public ValueTask<string> BeginAsync( CancellationToken cancellationToken )
        {
            Lifetime.ThrowIfCancellationRequested( );
            return _connection.Credential.BeginLoginAsync( cancellationToken );
        }
    }
    public PendingLogin CaptureLogin( string package, string name ) => new( Find( package, name ) );
    public async ValueTask<string> BeginLoginAsync( string package, string name, CancellationToken token )
    {
        var login = CaptureLogin( package, name );
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource( token, login.Lifetime );
        return await login.BeginAsync( lifetime.Token ).ConfigureAwait( false );
    }
    public void Revoke( string? package )
    {
        lock ( _gate )
        {
            foreach ( var key in _connections.Keys.Where( key => package is null || key.Package == package ).ToArray( ) )
            {
                var connection = _connections[key];
                _connections.Remove( key );
                connection.Dispose( );
            }
        }
    }
    public void Dispose( )
    {
        Revoke( null );
        if ( _ownsJournal && _journal is IDisposable disposable )
        {
            disposable.Dispose( );
        }
    }

    public async ValueTask<BrokerResult> ExecuteAsync( string package, BrokerRequest request, bool canUseSecrets,
        Func<ExtensionRemoteConfirmation, CancellationToken, ValueTask<bool>> confirm, CancellationToken token )
    {
        var brokerOperation = BrokerOperation.FromRequest( request );
        var connection = Find( package, request.Connection );
        using var operation = CancellationTokenSource.CreateLinkedTokenSource( token, connection.Token );
        if ( brokerOperation is FileListOperation or FileReadOperation )
        {
            var listing = connection.Files?.Access( brokerOperation, operation.Token );
            if ( listing is null )
            {
                throw new InvalidOperationException( "Select a file connection." );
            }

            return listing;
        }
        if ( brokerOperation is not HttpSendOperation { Request: var http } || connection.Model is not HttpConnectionModel )
        {
            throw new InvalidOperationException( "Invalid broker operation." );
        }
        if ( connection.Setup.CredentialKind != ExtensionCredentialKinds.None && !canUseSecrets )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.PermissionDenied );
        }
        var uri = ExtensionHttpTransport.RequestUri( connection.Setup, http );
        var body = ExtensionHttpTransport.Body( http );
        var mutation = http.Method != "GET";
        if ( mutation )
        {
            if ( http.IdempotencyKey is not { Length: >= 16 and <= 128 } || http.IdempotencyKey.Any( character => !char.IsAsciiLetterOrDigit( character ) && character is not ('-' or '_') ) )
            {
                throw new InvalidOperationException( "Remote writes require a stable idempotency key." );
            }
            if ( !await confirm( new( Guid.NewGuid( ).ToString( "N" ), request.Connection, http.Method, uri.AbsoluteUri,
                            PreviewBody( body ), http.IdempotencyKey ), operation.Token ).ConfigureAwait( false ) )
            {
                return new( ExtensionBrokerOutcomes.Cancelled );
            }
        }
        operation.CancelAfter( TimeSpan.FromSeconds( 15 ) );
        var started = false;
        try
        {
            var bearer = await connection.Credential.GetAsync( operation.Token ).ConfigureAwait( false );
            operation.Token.ThrowIfCancellationRequested( );
            if ( mutation && !await AdmitAttemptAsync( package, request.Connection, http.IdempotencyKey!, operation.Token ).ConfigureAwait( false ) )
            {
                return new( ExtensionBrokerOutcomes.Unknown );
            }
            using var client = ExtensionHttpTransport.Create( ExtensionHttpTransport.Origin( connection.Setup.Origin!, connection.Setup.PrivateNetwork ), connection.Setup.PrivateNetwork );
            // Revocation and network admission are ordered on the grant lock; cancellation also closes in-flight transport.
            ValueTask<BrokerResult> sending;
            lock ( _gate )
            {
                operation.Token.ThrowIfCancellationRequested( );
                if ( _connections.GetValueOrDefault( (package, request.Connection) ) != connection )
                {
                    throw new OperationCanceledException( operation.Token );
                }
                started = true;
                sending = ExtensionHttpTransport.SendAsync( client, uri, http, bearer, operation.Token );
            }
            var response = await sending.ConfigureAwait( false );
            return MutationOutcome( response, mutation );
        }
        catch ( Exception exception ) when ( IsTransportFailure( exception ) )
        {
            return new( mutation && started ? ExtensionBrokerOutcomes.Unknown : operation.IsCancellationRequested ? ExtensionBrokerOutcomes.Cancelled : ExtensionBrokerOutcomes.ConnectionUnavailable );
        }
    }

    private static bool IsTransportFailure( Exception exception ) => exception is not ExtensionApplicationException &&
        exception is (HttpRequestException or IOException or InvalidDataException or OperationCanceledException or InvalidOperationException);

    private static BrokerResult MutationOutcome( BrokerResult response, bool mutation ) =>
        mutation && (response.Outcome != ExtensionBrokerOutcomes.Completed || response.StatusCode >= 500) ? new( ExtensionBrokerOutcomes.Unknown ) : response;

    private static string PreviewBody( byte[] body )
    {
        try
        {
            return StrictUtf8.GetString( body );
        }
        catch ( DecoderFallbackException )
        {
            return "Binary body (base64):\n" + Convert.ToBase64String( body );
        }
    }

    private async ValueTask<bool> AdmitAttemptAsync( string package, string connection, string key, CancellationToken token )
    {
        var admission = await _journal.TryRecordAttemptAsync( package, connection, key, token ).ConfigureAwait( false );
        return admission switch
        {
            JournalAdmission.Recorded => true,
            JournalAdmission.AlreadyAttempted => false,
            JournalAdmission.PackageFull => throw new ExtensionApplicationException( ExtensionErrorCodes.QuotaExceeded ),
            JournalAdmission.JournalFull => throw new ExtensionApplicationException( ExtensionErrorCodes.QuotaExceeded ),
            _ => throw new InvalidOperationException( "Invalid journal admission." )
        };
    }
}
