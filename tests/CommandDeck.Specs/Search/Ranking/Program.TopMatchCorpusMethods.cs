using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class MeasuredPromotionProvider( ICommandProvider inner ) : ICommandProvider
    {
        public string Id => inner.Id;
        public int CandidateCount { get; private set; }
        public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync( ParsedQuery query, SearchRequest request, CancellationToken token )
        {
            var results = await inner.SearchAsync( query, request, token );
            CandidateCount = results.Count;
            return results;
        }
    }

    private sealed class MeasuredPromotionHistory( IUsageHistoryStore inner ) : UsageHistoryDecorator( inner )
    {
        public int ReadCount { get; set; }
        public override ValueTask<IReadOnlyDictionary<string, UsageSignal>> GetUsageSignalsAsync( IReadOnlyList<string> ids, CancellationToken token )
        {
            ReadCount++;
            return base.GetUsageSignalsAsync( ids, token );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> DensePromotionItems( JsonElement items )
    {
        await foreach ( var item in CorpusItems( items ) )
        {
            yield return item;
        }
        for ( var index = 0; index < 5_000; index++ )
        {
            yield return new IndexedItem
            {
                StableId = $"file:dense-{index}",
                Kind = ResultKinds.File,
                Title = $"Signal Background Notes {index}",
                Path = $"/fixture/notes/{index}.md",
                SourceId = SourceIds.Files,
                ProviderId = "files"
            };
        }
        for ( var index = 0; index < 5_000; index++ )
        {
            yield return new IndexedItem
            {
                StableId = $"file:unrelated-{index}",
                Kind = ResultKinds.File,
                Title = $"Unrelated item {index}",
                Path = $"/fixture/unrelated/{index}.md",
                SourceId = SourceIds.Files,
                ProviderId = "files"
            };
        }
    }

    private static async Task RunLearnedTopMatchCorpusAsync( bool benchmark )
    {
        using var document = JsonDocument.Parse( File.ReadAllText( Path.Combine( AppContext.BaseDirectory, "fixtures", "ranking-corpus-v1.json" ) ) );
        var fixture = document.RootElement.GetProperty( "learnedTopMatches" );
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-learned-corpus-{Guid.NewGuid( ):N}.db" );
        FixedTimeProvider clock = new( PromotionNow );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database }, clock );
            await catalog.ReplaceAllAsync( DensePromotionItems( fixture.GetProperty( "items" ) ), null, default );
            MeasuredPromotionProvider provider = new( new CatalogSearchProvider( catalog ) );
            MeasuredPromotionHistory history = new( catalog.UsageHistory );
            SearchOptions options = new( )
            {
                ProviderTimeoutMilliseconds = 5_000
            };
            SearchCoordinator coordinator = new( [provider], options, catalog, catalog, history, clock );
            List<object> ranks = [];
            foreach ( var learned in new[] { false, true } )
            {
                if ( learned )
                {
                    foreach ( var usage in fixture.GetProperty( "usage" ).EnumerateArray( ) )
                    {
                        for ( var count = 0; count < usage.GetProperty( "count" ).GetInt32( ); count++ )
                        {
                            await catalog.UsageHistory.RecordInvocationAsync( usage.GetProperty( "id" ).GetString( )!, "launch", null, default );
                        }
                    }
                }
                foreach ( var testCase in fixture.GetProperty( "cases" ).EnumerateArray( ) )
                {
                    var query = testCase.GetProperty( "query" ).GetString( )!;
                    var expected = testCase.GetProperty( learned ? "learnedFirst" : "coldFirst" ).GetString( );
                    var results = await coordinator.SearchAsync( new SearchRequest { Query = query, Limit = 9 }, default );
                    Equal( expected, results[0].Id );
                    if ( learned )
                    {
                        Equal( testCase.GetProperty( "learnedSection" ).GetString( ), results[0].Section!.Id );
                    }
                    ranks.Add( new
                    {
                        query,
                        learned,
                        first = results[0].Id,
                        topThree = results.Take( 3 ).Select( result => result.Id ).ToArray( ),
                        candidates = provider.CandidateCount
                    } );
                }
            }
            foreach ( var limit in new[] { 1, 2, 3 } )
            {
                var small = await coordinator.SearchAsync( new SearchRequest { Query = "sig", Limit = limit }, default );
                Equal( "app:signal.desktop", small[0].Id );
                Equal( "top-match", small[0].Section!.Id );
            }
            if ( benchmark )
            {
                SearchCoordinator legacy = new( [provider], new SearchOptions
                {
                    ProviderTimeoutMilliseconds = 5_000,
                    Ranking = new RankingOptions { TopMatches = new TopMatchesOptions { Enabled = false } }
                }, catalog, catalog, history, clock );
                var timing = await MeasureTopMatchComparisonAsync( coordinator, legacy, history );
                Console.WriteLine( JsonSerializer.Serialize( new
                {
                    fixtureItems = 10004,
                    ranks,
                    timing
                }, QualityJsonOptions ) );
            }
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task<List<object>> MeasureTopMatchComparisonAsync( SearchCoordinator current,
        SearchCoordinator legacy, MeasuredPromotionHistory history )
    {
        List<object> reports = [];
        var request = new SearchRequest { Query = "sig", Limit = 9 };
        for ( var warmup = 0; warmup < 30; warmup++ )
        {
            await legacy.SearchAsync( request, default );
            await current.SearchAsync( request, default );
        }
        foreach ( var freshConnection in new[] { false, true } )
        {
            List<double> before = [], after = [];
            history.ReadCount = 0;
            for ( var sample = 0; sample < 120; sample++ )
            {
                // Alternate execution order to avoid assigning temperature/scheduling drift to one policy.
                foreach ( var learned in sample % 2 == 0 ? new[] { false, true } : new[] { true, false } )
                {
                    if ( freshConnection )
                    {
                        SqliteConnection.ClearAllPools( );
                    }
                    var started = Stopwatch.GetTimestamp( );
                    await (learned ? current : legacy).SearchAsync( request, default );
                    (learned ? after : before).Add( Stopwatch.GetElapsedTime( started ).TotalMilliseconds );
                }
            }
            before.Sort( );
            after.Sort( );
            reports.Add( new
            {
                freshConnection,
                samples = 120,
                beforeP50Ms = before[60],
                beforeP95Ms = before[113],
                afterP50Ms = after[60],
                afterP95Ms = after[113],
                p95Ratio = after[113] / before[113],
                usageReadsPerLearnedSearch = history.ReadCount / 120d
            } );
        }
        return reports;
    }
}
