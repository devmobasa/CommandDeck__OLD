using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommandDeck.ExtensionProtocol;

// Private mutable input preserves omitted defaults on the net10 source generator; public models remain init-only.
public sealed class ExtensionCommandConverter : JsonConverter<ExtensionCommand>
{
    public override ExtensionCommand Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
    {
        var wire = JsonSerializer.Deserialize( ref reader, ExtensionCommandWireContext.Default.ExtensionCommandWire );
        if ( wire is null )
        {
            throw new JsonException( "Missing wire model." );
        }

        return new ExtensionCommand
        {
            Id = wire.Id,
            Title = wire.Title,
            Description = wire.Description,
            Mode = wire.Mode,
            Icon = wire.Icon,
            Keywords = wire.Keywords is null ? default : wire.Keywords.ToImmutableArray( ),
            Capabilities = wire.Capabilities is null ? default : wire.Capabilities.ToImmutableArray( ),
            RequiredFeatures = wire.RequiredFeatures is null ? default : wire.RequiredFeatures.ToImmutableArray( ),
        };
    }

    public override void Write( Utf8JsonWriter writer, ExtensionCommand value, JsonSerializerOptions options ) =>
        JsonSerializer.Serialize( writer, new ExtensionCommandWire
        {
            Id = value.Id,
            Title = value.Title,
            Description = value.Description,
            Mode = value.Mode,
            Icon = value.Icon,
            Keywords = value.Keywords.IsDefault ? null! : value.Keywords.ToArray( ),
            Capabilities = value.Capabilities.IsDefault ? null! : value.Capabilities.ToArray( ),
            RequiredFeatures = value.RequiredFeatures.IsDefault ? null! : value.RequiredFeatures.ToArray( ),
        }, ExtensionCommandWireContext.Default.ExtensionCommandWire );
}

internal sealed class ExtensionCommandWire
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string Description { get; set; } = "";
    public string Mode { get; set; } = ExtensionCommandModes.View;
    public string? Icon { get; set; }
    public string[] Keywords { get; set; } = [];
    public string[] Capabilities { get; set; } = [];
    public string[] RequiredFeatures { get; set; } = [];
}

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( ExtensionCommandWire ) )]
internal sealed partial class ExtensionCommandWireContext : JsonSerializerContext;
