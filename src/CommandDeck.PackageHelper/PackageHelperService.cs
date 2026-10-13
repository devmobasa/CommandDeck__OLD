using Tmds.DBus.Protocol;

namespace CommandDeck.PackageHelper;

/// <summary>
/// Publishes operation progress as an <c>org.commanddeck.Package1.Progress</c>
/// signal so a disconnected client can rejoin a running transaction.
/// </summary>
internal sealed class PackageProgressPublisher( DBusConnection connection )
{
    private readonly DBusConnection _connection = connection;

    public void Publish( string operationId, string state, string message )
    {
        try
        {
            _connection.TrySendMessage( Build( operationId, state, message ) );
        }
        catch ( Exception exception ) when ( exception is DBusExceptionBase or ObjectDisposedException )
        {
            // A closed bus never blocks an in-flight package transaction.
        }
    }

    private MessageBuffer Build( string operationId, string state, string message )
    {
        using var writer = _connection.GetMessageWriter( );
        writer.WriteSignalHeader(
            null,
            PackageHelperProtocol.ObjectPath,
            PackageHelperProtocol.InterfaceName,
            PackageHelperProtocol.SignalProgress,
            "sss" );
        writer.WriteString( operationId );
        writer.WriteString( state );
        writer.WriteString( message );
        return writer.CreateMessage( );
    }
}

/// <summary>
/// The D-Bus surface of the privileged helper. It reads bounded request
/// values, hands every decision to <see cref="PackageHelperCore"/>, and maps
/// helper failures onto <c>org.commanddeck.Package1.Error.*</c> names.
/// </summary>
internal sealed class PackageHelperService( PackageHelperCore core ) : IPathMethodHandler
{
    private const int MaximumRequestNames = 64;

    private static readonly ReadOnlyMemory<byte>[] IntrospectionFragments =
    [
        System.Text.Encoding.UTF8.GetBytes(
            """
              <interface name="org.commanddeck.Package1">
                <method name="GetStatus">
                  <arg name="protocolVersion" type="s" direction="out"/>
                  <arg name="pacmanVersion" type="s" direction="out"/>
                  <arg name="databaseLocked" type="b" direction="out"/>
                </method>
                <method name="Commit">
                  <arg name="operation" type="s" direction="in"/>
                  <arg name="names" type="as" direction="in"/>
                  <arg name="databaseIdentity" type="s" direction="in"/>
                  <arg name="transactionDigest" type="s" direction="in"/>
                  <arg name="operationId" type="s" direction="out"/>
                </method>
                <method name="GetOperation">
                  <arg name="operationId" type="s" direction="in"/>
                  <arg name="state" type="s" direction="out"/>
                  <arg name="message" type="s" direction="out"/>
                  <arg name="verifiedNames" type="as" direction="out"/>
                  <arg name="failedNames" type="as" direction="out"/>
                </method>
                <method name="Cancel">
                  <arg name="operationId" type="s" direction="in"/>
                  <arg name="cancelled" type="b" direction="out"/>
                </method>
                <signal name="Progress">
                  <arg name="operationId" type="s"/>
                  <arg name="state" type="s"/>
                  <arg name="message" type="s"/>
                </signal>
              </interface>

            """)
    ];

    private readonly PackageHelperCore _core = core;

    public string Path => PackageHelperProtocol.ObjectPath;

    public bool HandlesChildPaths => false;

    public async ValueTask HandleMethodAsync( MethodContext context )
    {
        ArgumentNullException.ThrowIfNull( context );
        if ( context.IsDBusIntrospectRequest )
        {
            context.ReplyIntrospectXml( IntrospectionFragments, ReadOnlySpan<string>.Empty );
            return;
        }

        if ( !string.Equals(
            context.Request.InterfaceAsString,
            PackageHelperProtocol.InterfaceName,
            StringComparison.Ordinal ) )
        {
            context.ReplyUnknownMethodError( );
            return;
        }

        try
        {
            await DispatchAsync( context ).ConfigureAwait( false );
        }
        catch ( PackageHelperException exception ) when ( !context.ReplySent )
        {
            context.ReplyError( exception.ErrorName, exception.Message );
        }
        catch ( Exception exception ) when (
            exception is not OperationCanceledException && !context.ReplySent )
        {
            context.ReplyError(
                PackageHelperErrors.Failed,
                "The helper could not complete the request." );
        }
    }

    private async ValueTask DispatchAsync( MethodContext context )
    {
        var member = context.Request.MemberAsString ?? string.Empty;
        var signature = context.Request.SignatureAsString ?? string.Empty;
        switch ( member )
        {
            case PackageHelperProtocol.MethodGetStatus when signature.Length == 0:
                await ReplyStatusAsync( context ).ConfigureAwait( false );
                return;
            case PackageHelperProtocol.MethodCommit when string.Equals( signature, "sasss", StringComparison.Ordinal ):
                await ReplyCommitAsync( context ).ConfigureAwait( false );
                return;
            case PackageHelperProtocol.MethodGetOperation when string.Equals( signature, "s", StringComparison.Ordinal ):
                ReplyOperation( context );
                return;
            case PackageHelperProtocol.MethodCancel when string.Equals( signature, "s", StringComparison.Ordinal ):
                ReplyCancel( context );
                return;
            default:
                context.ReplyUnknownMethodError( );
                return;
        }
    }

    private async ValueTask ReplyStatusAsync( MethodContext context )
    {
        var status = await _core
            .GetStatusAsync( context.RequestAborted )
            .ConfigureAwait( false );
        WriteStatus( context, status );
    }

    private static void WriteStatus( MethodContext context, PackageHelperStatus status )
    {
        using var writer = context.CreateReplyWriter( "ssb" );
        writer.WriteString( status.ProtocolVersion );
        writer.WriteString( status.PacmanVersion );
        writer.WriteBool( status.DatabaseLocked );
        context.Reply( writer.CreateMessage( ) );
    }

    private async ValueTask ReplyCommitAsync( MethodContext context )
    {
        var request = ReadCommitRequest( context );
        var operationId = await _core.CommitAsync(
            request.Sender,
            request.Operation,
            request.Names,
            request.DatabaseIdentity,
            request.TransactionDigest,
            context.RequestAborted ).ConfigureAwait( false );
        WriteString( context, operationId );
    }

    private static CommitRequest ReadCommitRequest( MethodContext context )
    {
        var sender = context.Request.SenderAsString ?? string.Empty;
        if ( sender.Length == 0 )
        {
            throw new PackageHelperException(
                PackageHelperErrors.InvalidRequest,
                "The request has no verified sender." );
        }

        var reader = context.Request.GetBodyReader( );
        var operation = reader.ReadString( );
        var names = reader.ReadArrayStart( DBusType.String );
        List<string> requested = [];
        while ( reader.HasNext( names ) )
        {
            if ( requested.Count >= MaximumRequestNames )
            {
                throw new PackageHelperException(
                    PackageHelperErrors.InvalidNames,
                    "The request lists too many package names." );
            }

            requested.Add( reader.ReadString( ) );
        }

        return new CommitRequest(
            sender,
            operation,
            requested,
            reader.ReadString( ),
            reader.ReadString( ) );
    }

    private void ReplyOperation( MethodContext context )
    {
        var reader = context.Request.GetBodyReader( );
        var state = _core.GetOperation( reader.ReadString( ) );
        using var writer = context.CreateReplyWriter( "ssasas" );
        writer.WriteString( state.State );
        writer.WriteString( state.Message );
        string[] verified = [.. state.VerifiedNames];
        string[] failed = [.. state.FailedNames];
        writer.WriteArray( verified );
        writer.WriteArray( failed );
        context.Reply( writer.CreateMessage( ) );
    }

    private void ReplyCancel( MethodContext context )
    {
        var reader = context.Request.GetBodyReader( );
        var cancelled = _core.Cancel( reader.ReadString( ) );
        using var writer = context.CreateReplyWriter( "b" );
        writer.WriteBool( cancelled );
        context.Reply( writer.CreateMessage( ) );
    }

    private static void WriteString( MethodContext context, string value )
    {
        using var writer = context.CreateReplyWriter( "s" );
        writer.WriteString( value );
        context.Reply( writer.CreateMessage( ) );
    }

    private sealed record CommitRequest(
        string Sender,
        string Operation,
        IReadOnlyList<string> Names,
        string DatabaseIdentity,
        string TransactionDigest );
}
