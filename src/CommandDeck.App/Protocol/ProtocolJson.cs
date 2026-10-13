using System.Text;
using System.Text.Json;

namespace CommandDeck;

public static class ProtocolJson
{
    public static ProtocolRequest? DeserializeRequest( string line ) =>
        JsonSerializer.Deserialize( line, CommandDeckJsonContext.Default.ProtocolRequest );

    public static ProtocolMessage? DeserializeMessage( string line ) =>
        JsonSerializer.Deserialize( line, CommandDeckJsonContext.Default.ProtocolMessage );

    public static string SerializeMessage( ProtocolMessage message ) =>
        JsonSerializer.Serialize( message, CommandDeckJsonContext.Default.ProtocolMessage );

    public static string SerializeRequest( ProtocolRequest request ) =>
        JsonSerializer.Serialize( request, CommandDeckJsonContext.Default.ProtocolRequest );

    public static string SerializeReport( ReindexReport report ) =>
        JsonSerializer.Serialize( report, CommandDeckJsonContext.Default.ReindexReport );

    public static string SerializeBoundedCustomizationExport( CustomizationExport export )
    {
        var json = JsonSerializer.Serialize(
            export,
            CommandDeckJsonContext.Default.CustomizationExport );
        if ( Utf8ByteCount( json ) > ProtocolLimits.MaximumCustomizationExportBytes )
        {
            throw new InvalidOperationException(
                "Customization export exceeds the 256 KiB UTF-8 limit." );
        }

        return json;
    }

    public static int Utf8ByteCount( string value ) => Encoding.UTF8.GetByteCount( value );
}
