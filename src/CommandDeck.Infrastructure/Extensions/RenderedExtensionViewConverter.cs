using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CommandDeck;

internal sealed class RenderedExtensionViewConverter : JsonConverter<RenderedExtensionView>
{
    public override RenderedExtensionView Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options ) =>
        throw new NotSupportedException( "Renderer payloads are host output only." );

    public override void Write( Utf8JsonWriter writer, RenderedExtensionView value, JsonSerializerOptions options )
    {
        writer.WriteStartObject( );
        writer.WriteString( "kind", value.Kind );
        Write( writer, "search", value.Search );
        Write( writer, "items", value.Items );
        Write( writer, "emptyMessage", value.EmptyMessage );
        Write( writer, "previousPage", value.PreviousPage );
        Write( writer, "nextPage", value.NextPage );
        Write( writer, "asset", value.Asset );
        Write( writer, "body", value.Body );
        if ( value.Metadata is { } metadata )
        {
            Write( writer, "metadata", metadata );
        }
        if ( value.Fields is { } fields )
        {
            Write( writer, "fields", fields );
        }
        Write( writer, "submit", value.Submit );
        Write( writer, "fieldErrors", value.FieldErrors );
        if ( value.ValidatedFormRevision is { } revision )
        {
            writer.WriteNumber( "validatedFormRevision", revision );
        }
        writer.WriteString( "id", value.Id );
        writer.WriteString( "title", value.Title );
        Write( writer, "actions", value.Actions );
        writer.WriteBoolean( "loading", value.Loading );
        Write( writer, "error", value.Error );
        writer.WriteNumber( "depth", value.Depth );
        Write( writer, "backHandle", value.BackHandle );
        Write( writer, "imageSource", value.ImageSource );
        Write( writer, "backHandle", value.RestoredBackHandle );
        writer.WriteEndObject( );
    }

    private static void Write<T>( Utf8JsonWriter writer, string name, T value )
    {
        if ( value is null )
        {
            return;
        }
        writer.WritePropertyName( name );
        var metadata = ( JsonTypeInfo<T> ) RenderedExtensionJsonContext.Default.GetTypeInfo( typeof( T ) )!;
        JsonSerializer.Serialize( writer, value, metadata );
    }
}
