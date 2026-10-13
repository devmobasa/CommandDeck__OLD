using Tmds.DBus.Protocol;

namespace CommandDeck.PackageHelper;

internal readonly record struct PolkitAuthorizationResult( bool IsAuthorized, bool IsChallenge );

/// <summary>
/// Authorizes one privileged request for the calling unique bus name.
/// </summary>
internal interface IPolkitAuthority
{
    ValueTask<PolkitAuthorizationResult> CheckAuthorizationAsync(
        string senderBusName,
        string actionId,
        IReadOnlyList<KeyValuePair<string, string>> details,
        CancellationToken cancellationToken );
}

/// <summary>
/// Calls Polkit over the system bus with a <c>system-bus-name</c> subject so
/// the decision is bound to the exact caller and cannot be replayed by another
/// connection.
/// </summary>
internal sealed class PolkitAuthority( DBusConnection connection ) : IPolkitAuthority
{
    private const string Destination = "org.freedesktop.PolicyKit1";
    private const string ObjectPath = "/org/freedesktop/PolicyKit1/Authority";
    private const string Interface = "org.freedesktop.PolicyKit1.Authority";
    private const string Member = "CheckAuthorization";
    private const string RequestSignature = "(sa{sv})sa{ss}us";
    private const uint AllowUserInteraction = 1;

    private readonly DBusConnection _connection = connection;

    public async ValueTask<PolkitAuthorizationResult> CheckAuthorizationAsync(
        string senderBusName,
        string actionId,
        IReadOnlyList<KeyValuePair<string, string>> details,
        CancellationToken cancellationToken )
    {
        ArgumentException.ThrowIfNullOrEmpty( senderBusName );
        ArgumentException.ThrowIfNullOrEmpty( actionId );
        cancellationToken.ThrowIfCancellationRequested( );
        var request = BuildRequest( senderBusName, actionId, details );
        return await _connection
            .CallMethodAsync( request, static ( Message message, object? state ) => ReadResult( message ) )
            .ConfigureAwait( false );
    }

    private MessageBuffer BuildRequest(
        string senderBusName,
        string actionId,
        IReadOnlyList<KeyValuePair<string, string>> details )
    {
        using var writer = _connection.GetMessageWriter( );
        writer.WriteMethodCallHeader(
            Destination,
            ObjectPath,
            Interface,
            RequestSignature,
            Member,
            MessageFlags.None );
        writer.WriteStructureStart( );
        writer.WriteString( "system-bus-name" );
        var subject = writer.WriteDictionaryStart( );
        writer.WriteDictionaryEntryStart( );
        writer.WriteString( "name" );
        writer.WriteVariantString( senderBusName );
        writer.WriteDictionaryEnd( subject );
        writer.WriteString( actionId );
        var detailStart = writer.WriteDictionaryStart( );
        foreach ( var detail in details )
        {
            writer.WriteDictionaryEntryStart( );
            writer.WriteString( detail.Key );
            writer.WriteString( detail.Value );
        }

        writer.WriteDictionaryEnd( detailStart );
        writer.WriteUInt32( AllowUserInteraction );
        writer.WriteString( string.Empty );
        return writer.CreateMessage( );
    }

    private static PolkitAuthorizationResult ReadResult( Message message )
    {
        var reader = message.GetBodyReader( );
        reader.AlignStruct( );
        var authorized = reader.ReadBool( );
        var challenge = reader.ReadBool( );
        return new PolkitAuthorizationResult( authorized, challenge );
    }
}
