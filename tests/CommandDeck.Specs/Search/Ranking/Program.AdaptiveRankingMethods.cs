namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task AdaptiveRankingLifecycleIsReversibleAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-adaptive-ranking-{Guid.NewGuid( ):N}.db" );
        MutableTimeProvider clock = new( new DateTimeOffset( 2026, 1, 1, 12, 0, 0, TimeSpan.Zero ) );
        SearchOptions options = new( )
        {
            Ranking = new RankingOptions
            {
                Adaptive = new AdaptiveRankingOptions
                {
                    Enabled = true,
                    RetentionDays = 30,
                    MaximumEvents = 3
                }
            }
        };
        try
        {
            await using SqliteCatalog catalog = new(
                new StorageOptions { DatabasePath = database },
                clock,
                searchOptions: options );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync( AdaptiveRankingItems( ), null, CancellationToken.None );
            await catalog.UsageHistory.RecordInvocationAsync( "app:beta", "launch", "browser", CancellationToken.None );
            await catalog.UsageHistory.RecordInvocationAsync( "app:beta", "launch", "browser", CancellationToken.None );

            var snapshot = await catalog.UsageHistory.GetRankingLearningAsync(
                20,
                CancellationToken.None );
            Equal( 2L, snapshot.TotalEvents );
            Equal( 2, snapshot.Events.Count );
            True( snapshot.Enabled );
            var exported = System.Text.Json.JsonSerializer.Serialize(
                snapshot,
                CommandDeckJsonContext.Default.RankingLearningSnapshot );
            True( exported.Contains( "app:beta", StringComparison.Ordinal ) );

            SearchCoordinator coordinator = new(
                [new CatalogSearchProvider( catalog )],
                options );
            var learned = (await coordinator.SearchAsync(
                new SearchRequest { Query = "browser", Limit = 10 },
                CancellationToken.None ))[0];
            Equal( "app:beta", learned.Id );
            True( learned.Ranking.Contributions.Any( static contribution =>
                contribution.Kind == RankingContributionKinds.Adaptive ) );

            clock.Advance( TimeSpan.FromDays( 31 ) );
            await catalog.UsageHistory.RecordInvocationAsync( "app:alpha", "launch", "browser", CancellationToken.None );
            var expired = await catalog.UsageHistory.GetRankingLearningAsync(
                20,
                CancellationToken.None );
            Equal( 1L, expired.TotalEvents );
            Equal( "app:alpha", expired.Events.Single( ).StableId );

            await catalog.UsageHistory.ResetRankingLearningAsync( CancellationToken.None );
            var reset = await catalog.UsageHistory.GetRankingLearningAsync(
                20,
                CancellationToken.None );
            Equal( 0L, reset.TotalEvents );
            Equal( 0, reset.Events.Count );

            SearchOptions disabledOptions = new( )
            {
                Ranking = new RankingOptions
                {
                    Adaptive = new AdaptiveRankingOptions { Enabled = false }
                }
            };
            await using SqliteCatalog disabled = new(
                new StorageOptions { DatabasePath = database },
                clock,
                searchOptions: disabledOptions );
            SearchCoordinator disabledCoordinator = new(
                [new CatalogSearchProvider( disabled )],
                disabledOptions );
            var disabledResults = await disabledCoordinator.SearchAsync(
                new SearchRequest { Query = "browser", Limit = 10 },
                CancellationToken.None );
            True( disabledResults.All( static result => result.Ranking.Contributions.All(
                static contribution => contribution.Kind != RankingContributionKinds.Adaptive ) ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task RankingCliInspectsExportsAndConfirmsResetAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var database = Path.Combine( root, "catalog.db" );
            var config = Path.Combine( root, "config.json" );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = database },
                Indexing = new IndexingOptions
                {
                    Roots = [],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false
                },
                Privacy = new PrivacyOptions
                {
                    StoreUsageHistory = true,
                    StoreQueryHistory = true,
                    NeverIndex = []
                },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            await File.WriteAllTextAsync(
                config,
                System.Text.Json.JsonSerializer.Serialize(
                    options,
                    CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using ( SqliteCatalog catalog = new(
                options.Storage,
                privacy: options.Privacy,
                searchOptions: options.Search ) )
            {
                await catalog.InitializeAsync( CancellationToken.None );
                await catalog.ReplaceAllAsync( EmptyIndexedItems( ), null, CancellationToken.None );
                await catalog.UsageHistory.RecordInvocationAsync(
                    "app:cli-ranking",
                    "launch",
                    "cli query",
                    CancellationToken.None );
            }

            var inspected = await RunCommandDeckProcessAsync(
                "ranking", "inspect", "--json", "--config", config );
            Equal( 0, inspected.ExitCode );
            True( inspected.StandardOutput.Contains( "app:cli-ranking", StringComparison.Ordinal ) );
            var exported = await RunCommandDeckProcessAsync(
                "ranking", "export", "--config", config );
            Equal( 0, exported.ExitCode );
            True( !exported.StandardOutput.Contains( "cli query", StringComparison.Ordinal ) );

            var rejected = await RunCommandDeckProcessAsync(
                "ranking", "reset", "--config", config );
            Equal( 2, rejected.ExitCode );
            True( rejected.StandardError.Contains( "requires --yes", StringComparison.Ordinal ) );

            var reset = await RunCommandDeckProcessAsync(
                "ranking", "reset", "--yes", "--config", config );
            Equal( 0, reset.ExitCode );
            await using SqliteCatalog reopened = new(
                options.Storage,
                searchOptions: options.Search );
            var empty = await reopened.UsageHistory.GetRankingLearningAsync(
                20,
                CancellationToken.None );
            Equal( 0L, empty.TotalEvents );
            var retainedHistory = await reopened.UsageHistory.GetQueryHistoryAsync(
                20,
                CancellationToken.None );
            Equal( 1L, retainedHistory.TotalEntries );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> AdaptiveRankingItems( )
    {
        yield return new IndexedItem
        {
            StableId = "app:alpha",
            Kind = ResultKinds.Application,
            Title = "Alpha Browser",
            ProviderId = "spec",
            SourceId = SourceIds.Applications
        };
        yield return new IndexedItem
        {
            StableId = "app:beta",
            Kind = ResultKinds.Application,
            Title = "Beta Browser",
            ProviderId = "spec",
            SourceId = SourceIds.Applications
        };
        await Task.CompletedTask;
    }

    private sealed class MutableTimeProvider( DateTimeOffset value ) : TimeProvider
    {
        private DateTimeOffset _value = value;
        public override DateTimeOffset GetUtcNow( ) => _value;
        public void Advance( TimeSpan duration ) => _value += duration;
    }
}
