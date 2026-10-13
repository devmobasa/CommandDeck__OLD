namespace CommandDeck;

public static class QueryFieldIds
{
    public const string Title = "title";
    public const string Metadata = "metadata";
    public const string Path = "path";
    public const string Content = "content";
    public const string Config = "config";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Title, Metadata, Path, Content, Config],
        StringComparer.OrdinalIgnoreCase );
}

public sealed class QueryExpression
{
    // Literal input includes its routing prefix and is bounded like request.Query.
    public string? LiteralQuery { get; init; }
    public List<string> IncludeFilterIds { get; init; } = [];
    public List<string> ExcludeFilterIds { get; init; } = [];
    public List<QueryClause> Clauses { get; init; } = [];
}

public sealed class QueryClause
{
    public string? Field { get; init; }
    public required string Text { get; init; }
    public bool Excluded { get; init; }
}

public sealed class SearchSuggestion
{
    public required string Text { get; init; }
    public required string Source { get; init; }
    public QueryExpression Expression { get; init; } = new( );
}

public sealed class SearchSuggestionSnapshot
{
    public List<SearchSuggestion> Suggestions { get; init; } = [];
}
