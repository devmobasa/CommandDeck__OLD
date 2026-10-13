namespace CommandDeck;

public sealed class ParsedQuery
{
    public required string Original { get; init; }
    public required string Text { get; init; }
    public QueryExpression Expression { get; init; } = new( );
    public string? ProviderFilter { get; init; }
    public IReadOnlyList<FilterDescriptor> IncludedFilters { get; init; } = [];
    public IReadOnlyList<FilterDescriptor> ExcludedFilters { get; init; } = [];
    public bool HasExplicitFilter { get; init; }
    public bool IsCalculator { get; init; }
    public bool IsCommandMode { get; init; }
    public bool IsAiMode { get; init; }
    // The reserved backend named by an explicit prefix (codex:, claude:,
    // cursor:, grok:); null for ai:/ask:, which route to the selected backend.
    public string? ExplicitBackendId { get; init; }
    public IReadOnlyList<string> Terms { get; init; } = [];

    public bool IncludesFilter( string filterId ) => IncludedFilters.Any( value =>
        string.Equals( value.Id, filterId, StringComparison.OrdinalIgnoreCase ) );
}
