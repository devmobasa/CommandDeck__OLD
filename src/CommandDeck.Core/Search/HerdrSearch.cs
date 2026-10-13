namespace CommandDeck;

public static class HerdrSearch
{
    public const string AttentionFilterId = "herdr-attention";
    public const string WorkspaceIdMetadata = "herdrWorkspaceId";
    public const string AgentStatusMetadata = "herdrAgentStatus";

    public static bool NeedsAttention( SearchResult result ) =>
        result.SourceId == SourceIds.Herdr && result.Kind == ResultKinds.Tab &&
        result.Metadata.GetValueOrDefault( AgentStatusMetadata ) == "blocked";

    public static bool MatchesScope( SearchResult result, string? workspaceId ) =>
        workspaceId is null || (result.SourceId == SourceIds.Herdr && result.Kind == ResultKinds.Tab &&
            result.Metadata.GetValueOrDefault( WorkspaceIdMetadata ) == workspaceId);

    public static bool MatchesAttention( ParsedQuery query, SearchResult result ) =>
        (query.IncludedFilters.Count == 0 || query.IncludedFilters.Any( filter =>
            filter.SourceId == SourceIds.Herdr &&
            (filter.Kind is null || filter.Kind == result.Kind) &&
            (filter.Id != AttentionFilterId || NeedsAttention( result )) )) &&
        (!query.ExcludedFilters.Any( static filter => filter.Id == AttentionFilterId ) || !NeedsAttention( result ));
}
