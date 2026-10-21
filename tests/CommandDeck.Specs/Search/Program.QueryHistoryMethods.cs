namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task QueryHistoryCliIsBoundedAndReversibleAsync( )
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
                    StoreUsageHistory = false,
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
            await using ( SqliteCatalog catalog = new( options.Storage, privacy: options.Privacy ) )
            {
                await catalog.InitializeAsync( default );
                await catalog.UsageHistory.RecordInvocationAsync(
                    "app:cli-history",
                    "launch",
                    "line one\n\u001b[31mred",
                    default );
            }

            var inspected = await RunCommandDeckProcessAsync(
                "history", "inspect", "--config", config );
            Equal( 0, inspected.ExitCode );
            True( inspected.StandardOutput.Contains( "line one\\n\\u001B[31mred", StringComparison.Ordinal ) );
            True( !inspected.StandardOutput.Contains( '\u001b' ) );

            var exported = await RunCommandDeckProcessAsync(
                "history", "export", "--config", config );
            Equal( 0, exported.ExitCode );
            var export = System.Text.Json.JsonSerializer.Deserialize(
                exported.StandardOutput,
                CommandDeckJsonContext.Default.QueryHistorySnapshot );
            ArgumentNullException.ThrowIfNull( export );
            Equal( 1L, export.TotalEntries );
            var id = export.Entries[0].Id;

            var rejectedDelete = await RunCommandDeckProcessAsync(
                "history", "delete", id.ToString( System.Globalization.CultureInfo.InvariantCulture ),
                "--config", config );
            Equal( 2, rejectedDelete.ExitCode );
            var deleted = await RunCommandDeckProcessAsync(
                "history", "delete", id.ToString( System.Globalization.CultureInfo.InvariantCulture ),
                "--yes", "--config", config );
            Equal( 0, deleted.ExitCode );

            await using ( SqliteCatalog catalog = new( options.Storage, privacy: options.Privacy ) )
            {
                await catalog.UsageHistory.RecordInvocationAsync(
                    "app:cli-history-reset", "launch", "reset me", default );
            }
            var rejectedReset = await RunCommandDeckProcessAsync(
                "history", "reset", "--config", config );
            Equal( 2, rejectedReset.ExitCode );
            var reset = await RunCommandDeckProcessAsync(
                "history", "reset", "--yes", "--config", config );
            Equal( 0, reset.ExitCode );
            await using SqliteCatalog reopened = new( options.Storage, privacy: options.Privacy );
            Equal( 0L, (await reopened.UsageHistory.GetQueryHistoryAsync( 20, default )).TotalEntries );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task QueryHistoryLifecycleIsPrivateAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-query-history-{Guid.NewGuid( ):N}.db" );
        MutableTimeProvider clock = new(
            new DateTimeOffset( 2026, 1, 1, 12, 0, 0, TimeSpan.Zero ) );
        PrivacyOptions privacy = new( )
        {
            StoreUsageHistory = false,
            StoreQueryHistory = true,
            QueryHistoryRetentionDays = 2,
            MaximumQueryHistoryEntries = 2,
            NeverIndex = []
        };
        try
        {
            await using SqliteCatalog catalog = new(
                new StorageOptions { DatabasePath = database },
                clock,
                privacy,
                new SearchOptions
                {
                    Ranking = new RankingOptions
                    {
                        Adaptive = new AdaptiveRankingOptions { Enabled = false }
                    }
                } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.UsageHistory.RecordInvocationAsync( "app:first", "launch", "first query", default );
            clock.Advance( TimeSpan.FromHours( 1 ) );
            await catalog.UsageHistory.RecordInvocationAsync( "app:second", "launch", "second query", default );
            clock.Advance( TimeSpan.FromHours( 1 ) );
            await catalog.UsageHistory.RecordInvocationAsync( "app:third", "launch", "third query", default );

            var bounded = await catalog.UsageHistory.GetQueryHistoryAsync( 20, default );
            True( bounded.Enabled );
            Equal( 2L, bounded.TotalEntries );
            Equal( "third query", bounded.Entries[0].Query );
            Equal( "second query", bounded.Entries[1].Query );
            True( !bounded.Entries.Any( entry => entry.Query == "first query" ) );
            True( await catalog.UsageHistory.DeleteQueryHistoryAsync( bounded.Entries[1].Id, default ) );
            True( !await catalog.UsageHistory.DeleteQueryHistoryAsync( long.MaxValue, default ) );

            clock.Advance( TimeSpan.FromDays( 3 ) );
            var expired = await catalog.UsageHistory.GetQueryHistoryAsync( 20, default );
            Equal( 0L, expired.TotalEntries );
            await catalog.UsageHistory.RecordInvocationAsync( "app:fresh", "launch", "fresh query", default );
            await catalog.UsageHistory.ResetQueryHistoryAsync( default );
            Equal( 0L, (await catalog.UsageHistory.GetQueryHistoryAsync( 20, default )).TotalEntries );

            await using SqliteCatalog disabled = new(
                new StorageOptions { DatabasePath = database },
                clock,
                new PrivacyOptions
                {
                    StoreUsageHistory = false,
                    StoreQueryHistory = false,
                    NeverIndex = []
                } );
            await disabled.UsageHistory.RecordInvocationAsync(
                "app:private",
                "launch",
                "MustNeverPersist",
                default );
            var disabledSnapshot = await disabled.UsageHistory.GetQueryHistoryAsync( 20, default );
            True( !disabledSnapshot.Enabled );
            Equal( 0L, disabledSnapshot.TotalEntries );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }
}
