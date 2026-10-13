using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.Extensions.Views;

namespace CommandDeck.ExtensionProtocol;

// Private mutable input preserves omitted defaults on the net10 source generator; public models remain init-only.
public sealed class ListViewConverter : JsonConverter<ListView>
{
    public override ListView Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
    {
        var wire = JsonSerializer.Deserialize( ref reader, ListViewWireContext.Default.ListViewWire );
        if ( wire is null )
        {
            throw new JsonException( "Missing wire model." );
        }

        return new ListView
        {
            Search = wire.Search,
            Items = wire.Items is null ? default : wire.Items.ToImmutableArray( ),
            EmptyMessage = wire.EmptyMessage,
            PreviousPage = wire.PreviousPage,
            NextPage = wire.NextPage,
            Id = wire.Id,
            Title = wire.Title,
            Actions = wire.Actions is null ? default : wire.Actions.ToImmutableArray( ),
            Loading = wire.Loading,
            Error = wire.Error,
        };
    }

    public override void Write( Utf8JsonWriter writer, ListView value, JsonSerializerOptions options ) =>
        JsonSerializer.Serialize( writer, new ListViewWire
        {
            Search = value.Search,
            Items = value.Items.IsDefault ? null! : value.Items.ToArray( ),
            EmptyMessage = value.EmptyMessage,
            PreviousPage = value.PreviousPage,
            NextPage = value.NextPage,
            Id = value.Id,
            Title = value.Title,
            Actions = value.Actions.IsDefault ? null! : value.Actions.ToArray( ),
            Loading = value.Loading,
            Error = value.Error,
        }, ListViewWireContext.Default.ListViewWire );
}

internal sealed class ListViewWire
{
    public SearchField Search { get; set; } = new( );
    public ListItem[] Items { get; set; } = [];
    public string EmptyMessage { get; set; } = "No results";
    public string? PreviousPage { get; set; }
    public string? NextPage { get; set; }
    public required string Id { get; set; }
    public required string Title { get; set; }
    public ViewAction[] Actions { get; set; } = [];
    public bool Loading { get; set; }
    public string? Error { get; set; }
}

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( ListViewWire ) )]
internal sealed partial class ListViewWireContext : JsonSerializerContext;
