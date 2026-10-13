using Tmds.DBus.Protocol;

namespace CommandDeck;

public sealed partial class PackageHelperClient
{
    /// <summary>
    /// Observes helper progress signals. The match rule and a second check on
    /// every delivered message both bind the subscription to the unique name
    /// that currently owns the helper, so no other connection can forge
    /// progress for an operation.
    /// </summary>
    public IDisposable SubscribeProgress( Action<string, string, string> handler )
    {
        ArgumentNullException.ThrowIfNull( handler );
        ObjectDisposedException.ThrowIf( _disposed, this );
        ProgressSubscription subscription = new( this, handler );
        subscription.Start( );
        return subscription;
    }

    private static MessageBuffer BuildStatusRequest( DBusConnection connection )
    {
        using var writer = connection.GetMessageWriter( );
        writer.WriteMethodCallHeader(
            PackageHelperProtocol.BusName,
            PackageHelperProtocol.ObjectPath,
            PackageHelperProtocol.InterfaceName,
            null,
            PackageHelperProtocol.MethodGetStatus,
            MessageFlags.None );
        return writer.CreateMessage( );
    }

    private static MessageBuffer BuildCommitRequest(
        DBusConnection connection,
        string operation,
        IReadOnlyList<string> names,
        string databaseIdentity,
        string transactionDigest )
    {
        using var writer = connection.GetMessageWriter( );
        writer.WriteMethodCallHeader(
            PackageHelperProtocol.BusName,
            PackageHelperProtocol.ObjectPath,
            PackageHelperProtocol.InterfaceName,
            "sasss",
            PackageHelperProtocol.MethodCommit,
            MessageFlags.AllowInteractiveAuthorization );
        writer.WriteString( operation );
        string[] requested = [.. names];
        writer.WriteArray( requested );
        writer.WriteString( databaseIdentity );
        writer.WriteString( transactionDigest );
        return writer.CreateMessage( );
    }

    private static MessageBuffer BuildOperationRequest(
        DBusConnection connection,
        string member,
        string operationId )
    {
        using var writer = connection.GetMessageWriter( );
        writer.WriteMethodCallHeader(
            PackageHelperProtocol.BusName,
            PackageHelperProtocol.ObjectPath,
            PackageHelperProtocol.InterfaceName,
            "s",
            member,
            MessageFlags.None );
        writer.WriteString( operationId );
        return writer.CreateMessage( );
    }

    private static MessageBuffer BuildOwnerRequest( DBusConnection connection )
    {
        using var writer = connection.GetMessageWriter( );
        writer.WriteMethodCallHeader(
            BusServiceName,
            BusObjectPath,
            BusInterfaceName,
            "s",
            "GetNameOwner",
            MessageFlags.None );
        writer.WriteString( PackageHelperProtocol.BusName );
        return writer.CreateMessage( );
    }

    private static HelperStatusReply ReadStatus( Message message, object? state )
    {
        var reader = message.GetBodyReader( );
        return new HelperStatusReply( reader.ReadString( ), reader.ReadString( ), reader.ReadBool( ) );
    }

    private static OperationReply ReadOperation( Message message, object? state )
    {
        var reader = message.GetBodyReader( );
        return new OperationReply(
            reader.ReadString( ),
            reader.ReadString( ),
            reader.ReadArrayOfString( ),
            reader.ReadArrayOfString( ) );
    }

    private static ProgressSignal ReadProgress( Message message, object? state ) => new(
        message.SenderAsString ?? string.Empty,
        ReadProgressBody( message ) );

    private static ProgressBody ReadProgressBody( Message message )
    {
        var reader = message.GetBodyReader( );
        return new ProgressBody( reader.ReadString( ), reader.ReadString( ), reader.ReadString( ) );
    }

    private sealed record ProgressBody( string OperationId, string State, string Message );

    private sealed record ProgressSignal( string Sender, ProgressBody Body );

    private sealed class ProgressSubscription(
        PackageHelperClient client,
        Action<string, string, string> handler ) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new( );
        private readonly Lock _gate = new( );
        private IDisposable? _observer;
        private Task? _worker;
        private string _owner = string.Empty;
        private bool _disposed;

        public void Start( ) => _worker = SubscribeAsync( );

        public void Dispose( )
        {
            IDisposable? observer;
            lock ( _gate )
            {
                if ( _disposed )
                {
                    return;
                }

                _disposed = true;
                observer = _observer;
                _observer = null;
            }

            _cancellation.Cancel( );
            observer?.Dispose( );
            _cancellation.Dispose( );
        }

        private async Task SubscribeAsync( )
        {
            try
            {
                var connection = await client
                    .ConnectAsync( _cancellation.Token )
                    .ConfigureAwait( false );
                _owner = await connection
                    .CallMethodAsync(
                        BuildOwnerRequest( connection ),
                        static ( Message message, object? state ) =>
                            message.GetBodyReader( ).ReadString( ) )
                    .ConfigureAwait( false );
                MatchRule rule = new( )
                {
                    Type = MessageType.Signal,
                    Sender = _owner,
                    Path = PackageHelperProtocol.ObjectPath,
                    Interface = PackageHelperProtocol.InterfaceName,
                    Member = PackageHelperProtocol.SignalProgress
                };
                var observer = await connection.AddMatchAsync(
                    rule,
                    ReadProgress,
                    Deliver,
                    false,
                    ObserverFlags.None,
                    null ).ConfigureAwait( false );
                Attach( observer );
            }
            catch ( OperationCanceledException )
            {
                // Disposal stops the subscription before it is established.
            }
            catch ( Exception exception ) when ( exception is not OperationCanceledException )
            {
                // An absent or closed helper simply produces no progress.
            }
        }

        private void Attach( IDisposable observer )
        {
            bool disposed;
            lock ( _gate )
            {
                disposed = _disposed;
                if ( !disposed )
                {
                    _observer = observer;
                }
            }

            if ( disposed )
            {
                observer.Dispose( );
            }
        }

        private void Deliver( Notification<ProgressSignal> notification )
        {
            try
            {
                if ( notification.Type != NotificationType.Value )
                {
                    return;
                }

                var signal = notification.Value;
                if ( _owner.Length == 0 ||
                    !string.Equals( signal.Sender, _owner, StringComparison.Ordinal ) )
                {
                    return;
                }

                handler( signal.Body.OperationId, signal.Body.State, signal.Body.Message );
            }
            catch ( Exception exception ) when ( exception is not OperationCanceledException )
            {
                // The observer contract forbids throwing back into the reader.
            }
        }
    }
}
