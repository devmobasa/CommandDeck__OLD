using System.Text;
using System.Text.Json;

namespace CommandDeck.ExtensionProtocol;

internal static class StrictJson
{
    private static readonly UTF8Encoding Utf8 = new( false, true );

    public static JsonDocument Parse( ReadOnlyMemory<byte> bytes, int maximumDepth )
    {
        _ = Utf8.GetCharCount( bytes.Span );
        var document = JsonDocument.Parse( bytes, new JsonDocumentOptions { MaxDepth = maximumDepth } );
        try
        {
            ValidateProperties( document.RootElement );
            return document;
        }
        catch
        {
            document.Dispose( );
            throw;
        }
    }

    private static void ValidateProperties( JsonElement element )
    {
        if ( element.ValueKind == JsonValueKind.Object )
        {
            HashSet<string> names = new( StringComparer.Ordinal );
            foreach ( var property in element.EnumerateObject( ) )
            {
                if ( !names.Add( property.Name ) )
                {
                    throw new InvalidDataException( "An extension object has duplicate properties." );
                }
                ValidateProperties( property.Value );
            }
        }
        else if ( element.ValueKind == JsonValueKind.Array )
        {
            foreach ( var child in element.EnumerateArray( ) )
            {
                ValidateProperties( child );
            }
        }
    }
}
