using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.Extensions.Views;

namespace CommandDeck.ExtensionProtocol;

// Private mutable input preserves omitted defaults on the net10 source generator; public models remain init-only.
public sealed class DetailViewConverter : JsonConverter<DetailView>
{
    public override DetailView Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
    {
        var wire = JsonSerializer.Deserialize( ref reader, DetailViewWireContext.Default.DetailViewWire );
        if ( wire is null )
        {
            throw new JsonException( "Missing wire model." );
        }

        return new DetailView
        {
            Asset = wire.Asset,
            Body = wire.Body,
            Metadata = wire.Metadata is null ? default : wire.Metadata.ToImmutableArray( ),
            Id = wire.Id,
            Title = wire.Title,
            Actions = wire.Actions is null ? default : wire.Actions.ToImmutableArray( ),
            Loading = wire.Loading,
            Error = wire.Error,
        };
    }

    public override void Write( Utf8JsonWriter writer, DetailView value, JsonSerializerOptions options ) =>
        JsonSerializer.Serialize( writer, new DetailViewWire
        {
            Asset = value.Asset,
            Body = value.Body,
            Metadata = value.Metadata.IsDefault ? null! : value.Metadata.ToArray( ),
            Id = value.Id,
            Title = value.Title,
            Actions = value.Actions.IsDefault ? null! : value.Actions.ToArray( ),
            Loading = value.Loading,
            Error = value.Error,
        }, DetailViewWireContext.Default.DetailViewWire );
}

internal sealed class DetailViewWire
{
    public string? Asset { get; set; }
    public string Body { get; set; } = "";
    public DetailField[] Metadata { get; set; } = [];
    public required string Id { get; set; }
    public required string Title { get; set; }
    public ViewAction[] Actions { get; set; } = [];
    public bool Loading { get; set; }
    public string? Error { get; set; }
}

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( DetailViewWire ) )]
internal sealed partial class DetailViewWireContext : JsonSerializerContext;
