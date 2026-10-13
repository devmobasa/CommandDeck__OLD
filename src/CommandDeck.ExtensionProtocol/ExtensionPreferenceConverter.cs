using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommandDeck.ExtensionProtocol;

// Private mutable input preserves omitted defaults on the net10 source generator; public models remain init-only.
public sealed class ExtensionPreferenceConverter : JsonConverter<ExtensionPreference>
{
    public override ExtensionPreference Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
    {
        var wire = JsonSerializer.Deserialize( ref reader, ExtensionPreferenceWireContext.Default.ExtensionPreferenceWire );
        if ( wire is null )
        {
            throw new JsonException( "Missing wire model." );
        }

        return new ExtensionPreference
        {
            Id = wire.Id,
            Title = wire.Title,
            Type = wire.Type,
            Description = wire.Description,
            Required = wire.Required,
            Default = wire.Default,
            MaximumLength = wire.MaximumLength,
            Minimum = wire.Minimum,
            Maximum = wire.Maximum,
            Choices = wire.Choices is null ? default : wire.Choices.ToImmutableArray( ),
        };
    }

    public override void Write( Utf8JsonWriter writer, ExtensionPreference value, JsonSerializerOptions options ) =>
        JsonSerializer.Serialize( writer, new ExtensionPreferenceWire
        {
            Id = value.Id,
            Title = value.Title,
            Type = value.Type,
            Description = value.Description,
            Required = value.Required,
            Default = value.Default,
            MaximumLength = value.MaximumLength,
            Minimum = value.Minimum,
            Maximum = value.Maximum,
            Choices = value.Choices.IsDefault ? null! : value.Choices.ToArray( ),
        }, ExtensionPreferenceWireContext.Default.ExtensionPreferenceWire );
}

internal sealed class ExtensionPreferenceWire
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Type { get; set; }
    public string Description { get; set; } = "";
    public bool Required { get; set; }
    public JsonElement? Default { get; set; }
    public int MaximumLength { get; set; } = 8192;
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public CommandDeck.Extensions.Views.Choice[] Choices { get; set; } = [];
}

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( ExtensionPreferenceWire ) )]
internal sealed partial class ExtensionPreferenceWireContext : JsonSerializerContext;
