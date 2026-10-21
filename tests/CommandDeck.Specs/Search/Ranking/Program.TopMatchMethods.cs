namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task LearnedSignalAppearsAboveSourceGroupsAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-top-match-{Guid.NewGuid( ):N}.db" );
        FixedTimeProvider clock = new( new DateTimeOffset( 2026, 9, 7, 12, 0, 0, TimeSpan.Zero ) );
        try
        {
            SearchOptions options = new( );
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database }, clock, searchOptions: options );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( SignalPromotionItems( ), null, default );
            await catalog.UsageHistory.RecordInvocationAsync( "app:signal.desktop", "launch", null, default );
            await catalog.UsageHistory.RecordInvocationAsync( "app:signal.desktop", "launch", null, default );
            SearchCoordinator coordinator = new( [new CatalogSearchProvider( catalog )], options,
                catalog, catalog, catalog.UsageHistory, clock );
            var exact = await coordinator.SearchAsync( new SearchRequest { Query = "signal", Limit = 9 }, default );
            Equal( "app:signal.desktop", exact[0].Id );
            var partial = await coordinator.SearchAsync( new SearchRequest { Query = "sig", Limit = 9 }, default );
            var app = partial.Single( value => value.Id == "app:signal.desktop" );
            var install = partial.Single( value => value.Id == "omarchy:install.signal" );
            True( app.Score > install.Score, "the application already has a higher numeric score" );
            Equal( "app:signal.desktop", partial[0].Id );
            Equal( "top-match", partial[0].Section!.Id );
            Equal( "Top Matches", partial[0].Section!.Title );
            var wire = System.Text.Json.JsonSerializer.Serialize( partial[0], CommandDeckJsonContext.Default.SearchResult );
            var decoded = System.Text.Json.JsonSerializer.Deserialize( wire, CommandDeckJsonContext.Default.SearchResult )!;
            Equal( "learned", decoded.Ranking.Placement!.Tier );
            Equal( "app:signal.desktop", decoded.Ranking.Placement.UsageId );
            True( decoded.Ranking.Placement.Promoted );
            Equal( partial[0].Score, decoded.Score );
            Equal( 1, partial.Count( value => value.Id == install.Id ) );
            await catalog.ReplaceAllAsync( SignalPromotionItems( ), null, default );
            await using SqliteCatalog reopened = new( new StorageOptions { DatabasePath = database }, clock, searchOptions: options );
            SearchCoordinator restarted = new( [new CatalogSearchProvider( reopened )], options, reopened, reopened, reopened.UsageHistory, clock );
            var retained = await restarted.SearchAsync( new SearchRequest { Query = "sig" }, default );
            Equal( "app:signal.desktop", retained[0].Id );
            Equal( "learned", retained[0].Ranking.Placement!.Tier );
            await catalog.UsageHistory.ResetRankingLearningAsync( default );
            var reset = await coordinator.SearchAsync( new SearchRequest { Query = "sig" }, default );
            True( reset.All( result => result.Section!.Id != "top-match" ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> SignalPromotionItems( )
    {
        yield return new IndexedItem
        {
            StableId = "app:signal.desktop",
            Kind = ResultKinds.Application,
            Title = "Signal",
            ProviderId = "applications",
            SourceId = SourceIds.Applications,
            PayloadJson = PayloadWriter.Create( ("desktopId", "signal.desktop"), ("desktopFile", "/fixture/signal.desktop") )
        };
        yield return new IndexedItem
        {
            StableId = "omarchy:install.signal",
            Kind = ResultKinds.Omarchy,
            Title = "Signal",
            Subtitle = "Install › Service",
            ProviderId = "omarchy",
            SourceId = SourceIds.OmarchyControl,
            PayloadJson = PayloadWriter.Create( ("command", "install-signal") )
        };
        await Task.CompletedTask;
    }
}
