using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommandDeck.Extensions.Views;

[JsonConverter( typeof( ExtensionViewConverter ) )]
public abstract record ExtensionView
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public ImmutableArray<ViewAction> Actions { get; init; } = [];
    public bool Loading { get; init; }
    public string? Error { get; init; }
}

[JsonConverter( typeof( CommandDeck.ExtensionProtocol.ListViewConverter ) )]
public sealed record ListView : ExtensionView
{
    public SearchField Search { get; init; } = new( );
    public ImmutableArray<ListItem> Items { get; init; } = [];
    public string EmptyMessage { get; init; } = "No results";
    public string? PreviousPage { get; init; }
    public string? NextPage { get; init; }
}

[JsonConverter( typeof( CommandDeck.ExtensionProtocol.ListItemConverter ) )]
public sealed record ListItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Asset { get; init; }
    public ImmutableArray<ViewAction> Actions { get; init; } = [];
}

[JsonConverter( typeof( SearchModeConverter ) )]
public enum SearchMode
{
    Local,
    [System.Diagnostics.CodeAnalysis.Experimental( "CDEXT0001" )]
    Remote
}

public sealed class SearchModeConverter( ) : JsonStringEnumConverter<SearchMode>( JsonNamingPolicy.CamelCase, allowIntegerValues: false );

[JsonConverter( typeof( CommandDeck.ExtensionProtocol.SearchFieldConverter ) )]
public sealed record SearchField
{
    public SearchMode Mode { get; init; }
    public string Placeholder { get; init; } = "Search…";
}

[JsonConverter( typeof( CommandDeck.ExtensionProtocol.DetailViewConverter ) )]
public sealed record DetailView : ExtensionView
{
    public string? Asset { get; init; }
    public string Body { get; init; } = "";
    public ImmutableArray<DetailField> Metadata { get; init; } = [];
}

public sealed record DetailField( string Label, string Value );

[JsonConverter( typeof( CommandDeck.ExtensionProtocol.FormViewConverter ) )]
public sealed record FormView : ExtensionView
{
    public ImmutableArray<FormField> Fields { get; init; } = [];
    public required CallbackAction Submit { get; init; }
    public ImmutableDictionary<string, string> FieldErrors { get; init; } = ImmutableDictionary<string, string>.Empty.WithComparers( StringComparer.Ordinal );
    public long ValidatedFormRevision { get; init; }
}
