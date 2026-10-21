using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    // With default preferences a query with enough matches used to start one
    // supplement FTS query per underrepresented source, most of them live-only
    // sources (emoji, timezone, web search, ...) that own no catalog rows.
    private static async Task CatalogSupplementsSkipSourcesWithoutRowsAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-supplements-{Guid.NewGuid( ):N}.db" );
        FixedTimeProvider clock = new( PromotionNow );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database }, clock );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( SupplementFixtureItems( ), null, default );
            foreach ( var id in new[] { "file:report-7", "app:report-viewer-2", "config:report-1" } )
            {
                await catalog.UsageHistory.RecordInvocationAsync( id, "open", null, default );
            }

            CountingCatalogStore pruned = new( catalog, reportPopulatedSources: true );
            CountingCatalogStore unpruned = new( catalog, reportPopulatedSources: false );
            var prunedSearch = CatalogCoordinator( pruned, catalog, clock );
            var unprunedSearch = CatalogCoordinator( unpruned, catalog, clock );
            foreach ( var query in new[] { "r", "rep", "report", "report 1", "viewer", "conf" } )
            {
                pruned.Reset( );
                unpruned.Reset( );
                SearchRequest request = new( )
                {
                    Query = query,
                    Limit = 50
                };

                var expected = DescribeOrder( await unprunedSearch.SearchAsync( request, default ) );
                var actual = DescribeOrder( await prunedSearch.SearchAsync( request, default ) );

                Equal( expected, actual );
                True( pruned.SourceSearches <= unpruned.SourceSearches,
                    $"'{query}' made {pruned.SourceSearches} catalog queries; unpruned made {unpruned.SourceSearches}" );
            }

            pruned.Reset( );
            unpruned.Reset( );
            _ = await prunedSearch.SearchAsync( new SearchRequest { Query = "r", Limit = 50 }, default );
            _ = await unprunedSearch.SearchAsync( new SearchRequest { Query = "r", Limit = 50 }, default );
            True( pruned.SourceSearches <= 4 && unpruned.SourceSearches > 10,
                $"'r' made {pruned.SourceSearches} catalog queries (unpruned {unpruned.SourceSearches}); " +
                "only the application fast path, the initial query and populated sources may run" );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            DeleteDatabase( database );
        }
    }

    // Pruning must not change a single ordered ID or section across the
    // versioned relevance corpus, including aliases and learned usage.
    private static async Task RelevanceCorpusIsIdenticalWithoutEmptySourceQueriesAsync( )
    {
        var path = Path.Combine( AppContext.BaseDirectory, "fixtures", "ranking-corpus-v1.json" );
        using var document = JsonDocument.Parse( await File.ReadAllTextAsync( path ) );
        var fixture = ParseRelevanceFixture( document.RootElement.GetProperty( "relevanceCases" ) );
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-relevance-pruning-{Guid.NewGuid( ):N}.db" );
        FixedTimeProvider clock = new( PromotionNow );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database }, clock );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( RelevanceItems( fixture.Items, null ), null, default );
            var prunedSearch = CatalogCoordinator( new CountingCatalogStore( catalog, reportPopulatedSources: true ), catalog, clock );
            var unprunedSearch = CatalogCoordinator( new CountingCatalogStore( catalog, reportPopulatedSources: false ), catalog, clock );
            await ApplyRelevanceAliasesAsync( unprunedSearch, catalog, fixture.Items, fixture.Aliases );
            await ApplyRelevanceUsageAsync( catalog, fixture.Usage );

            var steps = 0;
            foreach ( var step in fixture.Cases.SelectMany( static testCase => testCase.Steps ) )
            {
                SearchRequest request = new( )
                {
                    Query = step.Query,
                    Limit = RelevanceProbeLimit
                };

                var expected = DescribeOrder( await unprunedSearch.SearchAsync( request, default ) );
                var actual = DescribeOrder( await prunedSearch.SearchAsync( request, default ) );
                if ( expected != actual )
                {
                    throw new InvalidOperationException(
                        $"Relevance query '{step.Query}' changed order.\nexpected: {expected}\nactual:   {actual}" );
                }
                steps++;
            }

            True( steps > 30, $"expected the whole corpus, compared {steps} queries" );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            DeleteDatabase( database );
        }
    }

    private static SearchCoordinator CatalogCoordinator(
        ICatalogQueryStore store,
        SqliteCatalog catalog,
        TimeProvider clock ) => new(
            [new ApplicationSearchProvider( store ), new CatalogSearchProvider( store, catalog )],
            new SearchOptions { ProviderTimeoutMilliseconds = 10_000 },
            catalog,
            catalog,
            catalog.UsageHistory,
            clock );

    private static string DescribeOrder( IReadOnlyList<SearchResult> results ) =>
        string.Join( " | ", results.Select( static result => $"{result.Id}@{result.Section?.Id}" ) );

    private static async IAsyncEnumerable<IndexedItem> SupplementFixtureItems( )
    {
        for ( var index = 0; index < 300; index++ )
        {
            yield return new IndexedItem
            {
                StableId = $"file:report-{index}",
                Kind = ResultKinds.File,
                Title = $"report {index}.txt",
                Subtitle = $"reports/{index % 7}",
                Path = $"/srv/reports/{index % 7}/report {index}.txt",
                ProviderId = ProviderIds.Files,
                SourceId = SourceIds.Files
            };
        }
        for ( var index = 0; index < 3; index++ )
        {
            yield return new IndexedItem
            {
                StableId = $"app:report-viewer-{index}",
                Kind = ResultKinds.Application,
                Title = $"Report Viewer {index}",
                ProviderId = ProviderIds.Applications,
                SourceId = SourceIds.Applications
            };
        }
        for ( var index = 0; index < 2; index++ )
        {
            yield return new IndexedItem
            {
                StableId = $"config:report-{index}",
                Kind = ResultKinds.Configuration,
                Title = $"report_{index}",
                Path = $"/srv/config/report-{index}.conf",
                ProviderId = ProviderIds.Config,
                SourceId = SourceIds.Configuration
            };
        }
        await Task.CompletedTask;
    }

    private sealed class CountingCatalogStore( SqliteCatalog inner, bool reportPopulatedSources ) : ICatalogQueryStore
    {
        private int _sourceSearches;

        public int SourceSearches => Volatile.Read( ref _sourceSearches );

        public void Reset( ) => Interlocked.Exchange( ref _sourceSearches, 0 );

        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            int limit,
            CancellationToken cancellationToken ) =>
            inner.SearchAsync( query, limit, cancellationToken );

        public ValueTask<IReadOnlyList<SearchResult>> SearchSourcesAsync(
            ParsedQuery query,
            IReadOnlyList<string> sourceIds,
            int limit,
            CancellationToken cancellationToken )
        {
            Interlocked.Increment( ref _sourceSearches );
            return inner.SearchSourcesAsync( query, sourceIds, limit, cancellationToken );
        }

        public ValueTask<IReadOnlyList<SearchResult>> GetHomeResultsAsync(
            LauncherPreferences preferences,
            int limit,
            CancellationToken cancellationToken ) =>
            inner.GetHomeResultsAsync( preferences, limit, cancellationToken );

        // The unpruned store reports every source as populated, as a store that
        // cannot tell does, so both coordinators run the same provider code.
        public ValueTask<IReadOnlySet<string>> GetPopulatedSourcesAsync(
            IReadOnlyList<string> sourceIds,
            CancellationToken cancellationToken ) =>
            reportPopulatedSources
                ? inner.GetPopulatedSourcesAsync( sourceIds, cancellationToken )
                : ValueTask.FromResult<IReadOnlySet<string>>( sourceIds.ToHashSet( StringComparer.Ordinal ) );
    }
}
