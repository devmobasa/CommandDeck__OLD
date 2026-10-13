using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.Extensions.Views;

namespace CommandDeck.ExtensionProtocol;

// Private mutable input preserves omitted defaults on the net10 source generator; public models remain init-only.
public sealed class SearchFieldConverter : JsonConverter<SearchField>
{
    public override SearchField Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
    {
        var wire = JsonSerializer.Deserialize( ref reader, SearchFieldWireContext.Default.SearchFieldWire );
        if ( wire is null )
        {
            throw new JsonException( "Missing wire model." );
        }

        return new SearchField
        {
            Mode = wire.Mode,
            Placeholder = wire.Placeholder,
        };
    }

    public override void Write( Utf8JsonWriter writer, SearchField value, JsonSerializerOptions options ) =>
        JsonSerializer.Serialize( writer, new SearchFieldWire
        {
            Mode = value.Mode,
            Placeholder = value.Placeholder,
        }, SearchFieldWireContext.Default.SearchFieldWire );
}

internal sealed class SearchFieldWire
{
    public SearchMode Mode { get; set; }
    public string Placeholder { get; set; } = "Search…";
}

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( SearchFieldWire ) )]
internal sealed partial class SearchFieldWireContext : JsonSerializerContext;
