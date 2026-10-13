using System.Collections.Immutable;
using System.Text.Json.Serialization;
using CommandDeck.Extensions.Views;

namespace CommandDeck;

// Declaration order is the established QML payload order, covered by the renderer golden fixtures.
[JsonConverter( typeof( RenderedExtensionViewConverter ) )]
internal sealed record RenderedExtensionView
{
    public required string Kind { get; init; }
    public SearchField? Search { get; init; }
    public RenderedExtensionItem[]? Items { get; init; }
    public string? EmptyMessage { get; init; }
    public string? PreviousPage { get; init; }
    public string? NextPage { get; init; }
    public string? Asset { get; init; }
    public string? Body { get; init; }
    public ImmutableArray<CommandDeck.Extensions.Views.DetailField>? Metadata { get; init; }
    public ImmutableArray<FormField>? Fields { get; init; }
    public RenderedExtensionAction? Submit { get; init; }
    public ImmutableDictionary<string, string>? FieldErrors { get; init; }
    public long? ValidatedFormRevision { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required RenderedExtensionAction[] Actions { get; init; }
    public bool Loading { get; init; }
    public string? Error { get; init; }
    public int Depth { get; init; }
    [JsonIgnore]
    public string? BackHandle { get; init; }
    public string? ImageSource { get; init; }
    [JsonIgnore]
    public string? RestoredBackHandle { get; init; }
}

internal sealed record RenderedExtensionItem( string Id, string Title, string? Subtitle, string? Asset,
    RenderedExtensionAction[] Actions, string? ImageSource );
internal sealed record RenderedExtensionAction( string Id, string Kind, string Title, string Handle );

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( RenderedExtensionView ) )]
[JsonSerializable( typeof( SearchField ) )]
[JsonSerializable( typeof( RenderedExtensionItem[] ) )]
[JsonSerializable( typeof( RenderedExtensionAction[] ) )]
[JsonSerializable( typeof( RenderedExtensionAction ) )]
[JsonSerializable( typeof( ImmutableArray<CommandDeck.Extensions.Views.DetailField> ) )]
[JsonSerializable( typeof( ImmutableArray<FormField> ) )]
[JsonSerializable( typeof( ImmutableDictionary<string, string> ) )]
[JsonSerializable( typeof( string ) )]
internal sealed partial class RenderedExtensionJsonContext : JsonSerializerContext;
