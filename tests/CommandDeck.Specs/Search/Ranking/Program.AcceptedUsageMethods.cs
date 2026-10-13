namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class AcceptedUsageHistory( IUsageHistoryStore inner ) : UsageHistoryDecorator( inner )
    {
        public int Writes { get; private set; }
        public bool Fail { get; set; }
        public bool WaitForCancellation { get; set; }
        public string? LastId { get; private set; }
        public string? LastQuery { get; private set; }
        public override async ValueTask RecordInvocationAsync( string resultId, string actionId, string? query, CancellationToken token )
        {
            token.ThrowIfCancellationRequested( );
            Writes++;
            LastId = resultId;
            LastQuery = query;
            if ( Fail )
            {
                throw new IOException( "fixture history failure" );
            }
            if ( WaitForCancellation )
            {
                await Task.Delay( Timeout.Infinite, token );
            }
            await base.RecordInvocationAsync( resultId, actionId, query, token );
        }
    }

    private sealed class AcceptanceExecutor : IActionExecutor
    {
        public int Calls { get; private set; }
        public CancellationTokenSource? CancelAfterAcceptance { get; set; }
        public bool Fail { get; set; }
        public ValueTask<ActionExecutionResult> ExecuteAsync( SearchResult result, CommandAction action, string? query, CancellationToken token )
        {
            token.ThrowIfCancellationRequested( );
            Calls++;
            if ( Fail )
            {
                return ValueTask.FromResult( ActionExecutionResult.Failure( "rejected dispatch" ) );
            }
            CancelAfterAcceptance?.Cancel( );
            return ValueTask.FromResult( ActionExecutionResult.Success( "Accepted", "Fixture output" ) );
        }
    }

    private static CommandDeckRuntime InvocationFixture( CommandDeckOptions options, SqliteCatalog catalog,
        IUsageHistoryStore history, IActionExecutor executor, SearchCoordinator search,
        RuntimeConstructionOptions? constructionOptions = null )
    {
        AiProviderSettingsService providers = new(
            options.Ai,
            new AiProviderSettingsStore( Path.Combine(
                Path.GetDirectoryName( PathExpansion.Expand( options.Storage.DatabasePath ) )!,
                AiProviderSettingsStore.FileName ) ) );
        FilePreviewService previews = new( options.Indexing, options.Privacy );
        AiPreflightService preflight = new( providers, previews, catalog.AiPreferences, ( _, _ ) => null );
        AiGenerationCoordinator generations = new( preflight );
        IndexCoordinator indexer = new( catalog, catalog, options );
        AiOutputService outputs = new( generations, executor );
        RuntimeResources resources = new( );
        resources.Own( catalog );
        resources.Own( indexer );
        resources.Own( providers );
        resources.OwnDisposable( preflight );
        resources.OwnDisposable( generations );
        resources.OwnDisposable( outputs );

        return new CommandDeckRuntime( options, catalog, history, indexer, indexer, search,
            executor, providers, preflight, generations, outputs, previews, catalog, catalog.AiPreferences, resources, constructionOptions );
    }

    private static async Task RuntimeAcceptedUsageSurvivesCancellationAndFailureAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-accepted-usage-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            var row = PromotionRow( "variant", "Signal", metadata: new( )
            {
                ["catalogResultId"] = "app:signal"
            } );
            AcceptedUsageHistory history = new( catalog.UsageHistory );
            AcceptanceExecutor executor = new( );
            SearchCoordinator search = new( [new StaticSearchProvider( [row] )], options.Search );
            await using var runtime = InvocationFixture( options, catalog, history, executor, search );
            await runtime.SearchAsync( new SearchRequest { Query = "sig" }, default );
            Equal( 0, history.Writes );
            using CancellationTokenSource canceled = new( );
            executor.CancelAfterAcceptance = canceled;
            var outcome = await runtime.InvokeAsync( row.Id, "open", false, canceled.Token );
            True( outcome.Succeeded );
            Equal( 1, history.Writes );
            Equal( "app:signal", history.LastId );
            Equal( 1L, (await catalog.UsageHistory.GetUsageSignalsAsync( ["app:signal"], default ))["app:signal"].UseCount );
            executor.CancelAfterAcceptance = null;
            history.Fail = true;
            outcome = await runtime.InvokeAsync( row.Id, "open", false, default );
            True( outcome.Succeeded && outcome.Message!.Contains( "History could not be saved" ) );
            Equal( "Fixture output", outcome.DetailMarkdown );
            Equal( 2, executor.Calls );
            history.Fail = false;
            history.WaitForCancellation = true;
            var watch = System.Diagnostics.Stopwatch.StartNew( );
            outcome = await runtime.InvokeAsync( row.Id, "open", false, default );
            True( outcome.Succeeded && watch.Elapsed < TimeSpan.FromSeconds( 4 ) );
            Equal( 3, executor.Calls );
            history.WaitForCancellation = false;
            executor.Fail = true;
            outcome = await runtime.InvokeAsync( row.Id, "open", false, default );
            True( !outcome.Succeeded );
            Equal( 3, history.Writes );
            try
            {
                await runtime.InvokeAsync( row.Id, "open", false, canceled.Token );
            }
            catch ( OperationCanceledException ) { }
            Equal( 4, executor.Calls );
            Equal( 3, history.Writes );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task AcceptedUsageBoundsRealSqliteContentionAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-usage-contention-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ), BusyTimeoutMilliseconds = 5_000 },
                Privacy = new PrivacyOptions { StoreQueryHistory = true },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage, privacy: options.Privacy );
            await catalog.InitializeAsync( default );
            var row = PromotionRow( "variant", "Signal", metadata: new( )
            {
                ["catalogResultId"] = "app:signal"
            } );
            AcceptanceExecutor executor = new( );
            SearchCoordinator search = new( [new StaticSearchProvider( [row] )], options.Search );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, executor, search );
            await runtime.SearchAsync( new SearchRequest { Query = "sig" }, default );
            await using Microsoft.Data.Sqlite.SqliteConnection writer = new( $"Data Source={options.Storage.DatabasePath};Pooling=False" );
            await writer.OpenAsync( );
            using var transaction = writer.BeginTransaction( deferred: false );
            var watch = System.Diagnostics.Stopwatch.StartNew( );
            var outcome = await runtime.InvokeAsync( row.Id, "open", false, default );
            watch.Stop( );
            transaction.Rollback( );
            True( outcome.Succeeded && outcome.Message!.Contains( "History could not be saved" ) );
            Equal( "Fixture output", outcome.DetailMarkdown );
            Equal( 1, executor.Calls );
            True( watch.Elapsed < TimeSpan.FromSeconds( 2 ), $"accepted invocation under a real write lock returned in {watch.Elapsed.TotalMilliseconds:F0} ms" );
            Equal( 0, (await catalog.UsageHistory.GetUsageSignalsAsync( ["app:signal"], default )).Count );
            Equal( 0L, (await catalog.UsageHistory.GetQueryHistoryAsync( 10, default )).TotalEntries );
            // There is no abandoned background write or launch retry. A later
            // accepted invocation can record normally once the lock is released.
            outcome = await runtime.InvokeAsync( row.Id, "open", false, default );
            True( outcome.Succeeded );
            Equal( 2, executor.Calls );
            Equal( 1L, (await catalog.UsageHistory.GetUsageSignalsAsync( ["app:signal"], default ))["app:signal"].UseCount );
            Equal( 1L, (await catalog.UsageHistory.GetQueryHistoryAsync( 10, default )).TotalEntries );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task UsageAndQueryHistoryGatesRemainIndependentAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-usage-gates-" ).FullName;
        try
        {
            foreach ( var learning in new[] { false, true } )
            {
                foreach ( var adaptive in new[] { false, true } )
                {
                    foreach ( var usage in new[] { false, true } )
                    {
                        foreach ( var query in new[] { false, true } )
                        {
                            var database = Path.Combine( root, $"{learning}-{adaptive}-{usage}-{query}.db" );
                            SearchOptions search = new( )
                            {
                                LearnFromInvocations = learning,
                                Ranking = new RankingOptions { Adaptive = new AdaptiveRankingOptions { Enabled = adaptive } }
                            };
                            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database }, privacy: new PrivacyOptions { StoreUsageHistory = usage, StoreQueryHistory = query }, searchOptions: search );
                            await catalog.UsageHistory.RecordInvocationAsync( "app:signal", "launch", "sig", default );
                            var signals = await catalog.UsageHistory.GetUsageSignalsAsync( ["app:signal"], default );
                            Equal( learning && adaptive && usage, signals.Count == 1 );
                            Equal( query ? 1L : 0L, (await catalog.UsageHistory.GetQueryHistoryAsync( 10, default )).TotalEntries );
                            // Inspect through enabled settings too: disabled reads cannot hide accidental writes.
                            await using SqliteCatalog inspector = new( new StorageOptions { DatabasePath = database } );
                            Equal( learning && adaptive && usage ? 1L : 0L, (await inspector.UsageHistory.GetRankingLearningAsync( 10, default )).TotalEvents );
                        }
                    }
                }
            }
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }
}
