namespace CommandDeck;

public sealed class ApplicationSearchProvider( ICatalogQueryStore catalog ) : ICommandProvider
{
    public string Id => SearchCoordinator.ApplicationFastPathProviderId;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        if ( request.EnabledSearchSources is { } sources &&
            !sources.Contains( SourceIds.Applications, StringComparer.Ordinal ) )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }

        return catalog.SearchSourcesAsync(
            query,
            [SourceIds.Applications],
            request.Limit,
            cancellationToken );
    }
}
