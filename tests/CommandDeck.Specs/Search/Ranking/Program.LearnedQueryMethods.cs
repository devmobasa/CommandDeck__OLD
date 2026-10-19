namespace CommandDeck.Specs;

public static partial class Program
{
    // Typing "ff" and accepting Firefox makes "ff" promote Firefox next time,
    // though "ff" is no word prefix of its title. One accepted use suffices for
    // the same query; a query that only begins a learned one needs two. The
    // reason never quotes the query. Query history, every learning gate, the
    // 21-day window, both ranking resets and the search purpose all apply.
    private static async Task LearnedQueriesPromoteAbbreviationsAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-learned-queries-" ).FullName;
        try
        {
            var now = DateTimeOffset.UnixEpoch.AddDays( 20_000 );
            FixedTimeProvider clock = new( now );
            var firefox = PromotionRow( "app:firefox", "Firefox", score: 0.4 );
            var fetch = PromotionRow( "app:fetchmail", "Fetchmail", score: 0.5 );
            var ffmpeg = PromotionRow( "app:ffmpeg", "FFmpeg Studio", score: 0.6 );
            async Task<SearchResult[]> SearchAsync( SqliteCatalog catalog, string query, SearchOptions? options = null,
                PrivacyOptions? privacy = null, TimeProvider? time = null, SearchPurpose purpose = SearchPurpose.Interactive )
            {
                SearchCoordinator search = new( [new StaticSearchProvider( [firefox, fetch, ffmpeg] )], options ?? new( ),
                    usageHistory: catalog.UsageHistory, timeProvider: time ?? clock,
                    privacy: privacy ?? new PrivacyOptions { StoreQueryHistory = true } );
                return [.. (await search.SearchWithOutcomeAsync( new SearchRequest { Query = query }, purpose, default )).Results];
            }
            static SearchResult? TopMatch( SearchResult[] results ) =>
                results.FirstOrDefault( static value => value.Section?.Id == "top-match" );

            var database = Path.Combine( root, "catalog.db" );
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database }, timeProvider: clock,
                privacy: new PrivacyOptions { StoreQueryHistory = true } );
            await catalog.InitializeAsync( default );
            True( TopMatch( await SearchAsync( catalog, "ff" ) )?.Id != firefox.Id );

            await catalog.UsageHistory.RecordInvocationAsync( firefox.Id, "open", "ff", default );
            var learned = TopMatch( await SearchAsync( catalog, "ff" ) );
            Equal( firefox.Id, learned?.Id );
            Equal( "learned", learned!.Ranking.Placement!.Tier );
            Equal( "Accepted after this same search", learned.Ranking.Placement.Reason );
            True( !learned.Ranking.Placement.Reason.Contains( "ff", StringComparison.Ordinal ) );
            Equal( firefox.Id, TopMatch( await SearchAsync( catalog, "  FF " ) )?.Id );

            await catalog.UsageHistory.RecordInvocationAsync( fetch.Id, "open", "fetchm", default );
            True( TopMatch( await SearchAsync( catalog, "fetch" ) )?.Id != fetch.Id );
            await catalog.UsageHistory.RecordInvocationAsync( fetch.Id, "open", "fetchm", default );
            Equal( fetch.Id, TopMatch( await SearchAsync( catalog, "fetch" ) )?.Id );
            True( TopMatch( await SearchAsync( catalog, "f" ) )?.Id is not ("app:firefox" or "app:fetchmail") );

            True( TopMatch( await SearchAsync( catalog, "ff", privacy: new PrivacyOptions { StoreQueryHistory = false } ) )?.Id
                != firefox.Id );
            True( TopMatch( await SearchAsync( catalog, "ff", new SearchOptions { LearnFromInvocations = false } ) )?.Id
                != firefox.Id );
            True( TopMatch( await SearchAsync( catalog, "ff", purpose: SearchPurpose.AssistantTool ) )?.Id != firefox.Id );
            True( TopMatch( await SearchAsync( catalog, "ff", time: new FixedTimeProvider( now.AddDays( 22 ) ) ) )?.Id
                != firefox.Id );

            True( await catalog.UsageHistory.ResetRankingForAsync( firefox.Id, default ) );
            True( TopMatch( await SearchAsync( catalog, "ff" ) )?.Id != firefox.Id );
            await catalog.UsageHistory.RecordInvocationAsync( firefox.Id, "open", "ff", default );
            Equal( firefox.Id, TopMatch( await SearchAsync( catalog, "ff" ) )?.Id );
            await catalog.UsageHistory.ResetRankingLearningAsync( default );
            True( TopMatch( await SearchAsync( catalog, "ff" ) )?.Id != firefox.Id );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
