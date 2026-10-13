using Tmds.DBus.Protocol;

namespace CommandDeck;

/// <summary>
/// The daemon side of the privileged package helper. Every call is bounded,
/// every failure is reduced to a short message without paths, and an absent
/// helper is reported as "not installed" instead of an error.
/// </summary>
public sealed partial class PackageHelperClient : IPackageHelper
{
    private const string BusServiceName = "org.freedesktop.DBus";
    private const string BusObjectPath = "/org/freedesktop/DBus";
    private const string BusInterfaceName = "org.freedesktop.DBus";
    private const string ServiceUnknownError = "org.freedesktop.DBus.Error.ServiceUnknown";
    private const string NameHasNoOwnerError = "org.freedesktop.DBus.Error.NameHasNoOwner";
    private const string NotInstalledMessage = "The package helper is not installed.";

    private readonly SemaphoreSlim _connectGate = new( 1, 1 );
    private DBusConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// Reduces a D-Bus failure to the helper status the daemon reports. An
    /// unknown service name and an unreachable bus both mean "not installed".
    /// </summary>
    public static AgentHelperStatus MapStatusFailure( string? errorName, string? message ) =>
        IsMissingService( errorName )
            ? new AgentHelperStatus { Installed = false, Message = NotInstalledMessage }
            : new AgentHelperStatus
            {
                Installed = false,
                Message = DescribeError( errorName, message )
            };

    /// <summary>
    /// Maps a helper or bus error onto a short message that carries no path
    /// and no separator the daemon would have to redact.
    /// </summary>
    public static string DescribeError( string? errorName, string? message ) => errorName switch
    {
        ServiceUnknownError or NameHasNoOwnerError => NotInstalledMessage,
        PackageHelperErrors.DatabaseChanged =>
            "The package databases changed. Preview the change again.",
        PackageHelperErrors.TransactionChanged =>
            "The transaction changed. Preview the change again.",
        PackageHelperErrors.DatabaseLocked =>
            "Another package transaction already holds the database lock.",
        PackageHelperErrors.Busy => "The helper is already running a package operation.",
        PackageHelperErrors.NotAuthorized => "The package change was not authorized.",
        PackageHelperErrors.NoAuthenticationAgent =>
            "No authentication agent answered the request.",
        PackageHelperErrors.InvalidOperation or
        PackageHelperErrors.InvalidNames or
        PackageHelperErrors.InvalidRequest => "The package request was rejected.",
        PackageHelperErrors.UnknownOperation => "The package operation is not known.",
        _ => Sanitize( message )
    };

    public static bool VersionMatches( string? reportedVersion ) =>
        string.Equals( reportedVersion, PackageHelperProtocol.ProtocolVersion, StringComparison.Ordinal );

    public async ValueTask<AgentHelperStatus> GetStatusAsync( CancellationToken cancellationToken )
    {
        try
        {
            var connection = await ConnectAsync( cancellationToken ).ConfigureAwait( false );
            var reply = await connection
                .CallMethodAsync( BuildStatusRequest( connection ), ReadStatus )
                .ConfigureAwait( false );
            return new AgentHelperStatus
            {
                Installed = true,
                Version = reply.ProtocolVersion,
                VersionMatches = VersionMatches( reply.ProtocolVersion ),
                PolkitAgentLikely = IsGraphicalSession( ),
                Message = reply.DatabaseLocked
                    ? "Another package transaction already holds the database lock."
                    : null
            };
        }
        catch ( DBusErrorReplyException exception )
        {
            return MapStatusFailure( exception.ErrorName, exception.ErrorMessage );
        }
        catch ( Exception exception ) when ( IsUnreachable( exception ) )
        {
            return new AgentHelperStatus { Installed = false, Message = NotInstalledMessage };
        }
    }

    public async ValueTask<string> CommitAsync(
        string operation,
        IReadOnlyList<string> names,
        string databaseIdentity,
        string transactionDigest,
        CancellationToken cancellationToken )
    {
        ArgumentException.ThrowIfNullOrEmpty( operation );
        ArgumentNullException.ThrowIfNull( names );
        try
        {
            var connection = await ConnectAsync( cancellationToken ).ConfigureAwait( false );
            var request = BuildCommitRequest(
                connection,
                operation,
                names,
                databaseIdentity,
                transactionDigest );
            return await connection
                .CallMethodAsync( request, static ( Message message, object? state ) =>
                    message.GetBodyReader( ).ReadString( ) )
                .ConfigureAwait( false );
        }
        catch ( DBusErrorReplyException exception )
        {
            throw new InvalidOperationException(
                DescribeError( exception.ErrorName, exception.ErrorMessage ) );
        }
        catch ( Exception exception ) when ( IsUnreachable( exception ) )
        {
            throw new InvalidOperationException( NotInstalledMessage );
        }
    }

    public async ValueTask<PackageCommitResult> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken )
    {
        ArgumentException.ThrowIfNullOrEmpty( operationId );
        try
        {
            var connection = await ConnectAsync( cancellationToken ).ConfigureAwait( false );
            var request = BuildOperationRequest(
                connection,
                PackageHelperProtocol.MethodGetOperation,
                operationId );
            var reply = await connection
                .CallMethodAsync( request, ReadOperation )
                .ConfigureAwait( false );
            return new PackageCommitResult
            {
                OperationId = operationId,
                State = reply.State,
                Message = reply.Message,
                VerifiedNames = [.. reply.VerifiedNames],
                FailedNames = [.. reply.FailedNames]
            };
        }
        catch ( DBusErrorReplyException exception )
        {
            return Failed( operationId, DescribeError( exception.ErrorName, exception.ErrorMessage ) );
        }
        catch ( Exception exception ) when ( IsUnreachable( exception ) )
        {
            return Failed( operationId, NotInstalledMessage );
        }
    }

    public async ValueTask<bool> CancelAsync(
        string operationId,
        CancellationToken cancellationToken )
    {
        ArgumentException.ThrowIfNullOrEmpty( operationId );
        try
        {
            var connection = await ConnectAsync( cancellationToken ).ConfigureAwait( false );
            var request = BuildOperationRequest(
                connection,
                PackageHelperProtocol.MethodCancel,
                operationId );
            return await connection
                .CallMethodAsync( request, static ( Message message, object? state ) =>
                    message.GetBodyReader( ).ReadBool( ) )
                .ConfigureAwait( false );
        }
        catch ( DBusErrorReplyException )
        {
            return false;
        }
        catch ( Exception exception ) when ( IsUnreachable( exception ) )
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync( )
    {
        if ( _disposed )
        {
            return;
        }

        _disposed = true;
        await _connectGate.WaitAsync( ).ConfigureAwait( false );
        try
        {
            _connection?.Dispose( );
            _connection = null;
        }
        finally
        {
            _connectGate.Release( );
            _connectGate.Dispose( );
        }
    }

    private static PackageCommitResult Failed( string operationId, string message ) => new( )
    {
        OperationId = operationId,
        State = PackageOperationStates.Failed,
        Message = message
    };

    private static bool IsMissingService( string? errorName ) =>
        string.Equals( errorName, ServiceUnknownError, StringComparison.Ordinal ) ||
        string.Equals( errorName, NameHasNoOwnerError, StringComparison.Ordinal );

    private static bool IsUnreachable( Exception exception ) =>
        exception is DBusExceptionBase or IOException or InvalidOperationException
            or ObjectDisposedException;

    private static bool IsGraphicalSession( ) =>
        !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "WAYLAND_DISPLAY" ) ) ||
        !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "DISPLAY" ) );

    private static string Sanitize( string? message )
    {
        if ( string.IsNullOrWhiteSpace( message ) ||
            message.Length > 200 ||
            message.Contains( '/', StringComparison.Ordinal ) )
        {
            return "The package helper reported a failure.";
        }

        return message;
    }

    private async ValueTask<DBusConnection> ConnectAsync( CancellationToken cancellationToken )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );
        await _connectGate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( _connection is not null )
            {
                return _connection;
            }

            var address = DBusAddress.System;
            if ( address is null )
            {
                throw new InvalidOperationException( "The system bus address is not set." );
            }

            DBusConnection connection = new( address );
            await connection.ConnectAsync( ).ConfigureAwait( false );
            _connection = connection;
            return connection;
        }
        finally
        {
            _connectGate.Release( );
        }
    }

    private sealed record HelperStatusReply(
        string ProtocolVersion,
        string PacmanVersion,
        bool DatabaseLocked );

    private sealed record OperationReply(
        string State,
        string Message,
        string[] VerifiedNames,
        string[] FailedNames );
}
