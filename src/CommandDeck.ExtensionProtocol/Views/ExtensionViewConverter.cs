using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions.Views;

public sealed class ExtensionViewConverter : JsonConverter<ExtensionView>
{
    public override ExtensionView Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
    {
        using var document = JsonDocument.ParseValue( ref reader );
        var root = document.RootElement;
        if ( root.ValueKind != JsonValueKind.Object || root.EnumerateObject( ).FirstOrDefault( ).Name != "kind" )
        {
            throw new JsonException( "A view must start with its kind discriminator." );
        }
        var kind = root.GetProperty( "kind" ).GetString( );
        ArrayBufferWriter<byte> buffer = new( );
        using ( Utf8JsonWriter writer = new( buffer ) )
        {
            writer.WriteStartObject( );
            foreach ( var property in root.EnumerateObject( ).Skip( 1 ) )
            {
                property.WriteTo( writer );
            }
            writer.WriteEndObject( );
        }

        ExtensionView? view = kind switch
        {
            ExtensionViewKinds.List => JsonSerializer.Deserialize( buffer.WrittenSpan, ExtensionViewJsonContext.Default.ListView ),
            ExtensionViewKinds.Detail => JsonSerializer.Deserialize( buffer.WrittenSpan, ExtensionViewJsonContext.Default.DetailView ),
            ExtensionViewKinds.Form => JsonSerializer.Deserialize( buffer.WrittenSpan, ExtensionViewJsonContext.Default.FormView ),
            _ => throw new JsonException( "Unsupported view kind." )
        };
        if ( view is null )
        {
            throw new JsonException( "Missing view." );
        }

        return view;
    }

    public override void Write( Utf8JsonWriter writer, ExtensionView value, JsonSerializerOptions options )
    {
        var (kind, content) = value switch
        {
            ListView list => (ExtensionViewKinds.List, JsonSerializer.SerializeToElement( list, ExtensionViewJsonContext.Default.ListView )),
            DetailView detail => (ExtensionViewKinds.Detail, JsonSerializer.SerializeToElement( detail, ExtensionViewJsonContext.Default.DetailView )),
            FormView form => (ExtensionViewKinds.Form, JsonSerializer.SerializeToElement( form, ExtensionViewJsonContext.Default.FormView )),
            _ => throw new JsonException( "Unsupported view kind." )
        };
        writer.WriteStartObject( );
        writer.WriteString( "kind", kind );
        foreach ( var property in content.EnumerateObject( ) )
        {
            property.WriteTo( writer );
        }
        writer.WriteEndObject( );
    }
}
