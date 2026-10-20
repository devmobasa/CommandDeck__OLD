using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    // Settings choices about history follow the configuration until made, then
    // override it at run time for the store, the workflow and search: query
    // history and usage learning start and stop with them, an unrelated save
    // keeps an unmade choice unmade, and a reset returns to the configuration.
    private static async Task HistoryChoicesOverrideTheConfigurationAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-history-choices-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] },
                Privacy = new PrivacyOptions { StoreQueryHistory = false }
            };
            SqliteCatalog catalog = new( options.Storage, privacy: options.Privacy, searchOptions: options.Search );
            await catalog.InitializeAsync( default );
            var row = PromotionRow( "app:signal", "Signal" );
            AcceptanceExecutor executor = new( );
            SearchCoordinator search = new( [new StaticSearchProvider( [row] )], options.Search, catalog, catalog.LauncherPreferences,
                catalog.UsageHistory, privacy: options.Privacy, history: catalog.History );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, executor, search,
                historyPolicy: catalog.History );
            async Task<(long Queries, bool Used)> RunAsync( string query )
            {
                await runtime.SearchAsync( new SearchRequest { Query = query }, default );
                True( (await runtime.InvokeAsync( row.Id, "open", false, default )).Succeeded );
                var queries = (await catalog.UsageHistory.GetQueryHistoryAsync( 10, default )).TotalEntries;
                var used = (await catalog.UsageHistory.GetUsageSignalsAsync( [row.Id], default )).ContainsKey( row.Id );
                return (queries, used);
            }

            var snapshot = await runtime.GetPreferencesAsync( false, default );
            True( snapshot.Preferences.RememberSearches is null && snapshot.Defaults is { RememberSearches: false, LearnFromUse: true } );
            Equal( (0L, true), await RunAsync( "sig" ) );

            await runtime.UpdatePreferencesAsync( new LauncherPreferences( )
            {
                Sources = snapshot.Preferences.Sources,
                RememberSearches = true,
                LearnFromUse = false
            }, default );
            await catalog.UsageHistory.ResetRankingLearningAsync( default );
            Equal( (1L, false), await RunAsync( "sign" ) );
            True( (await runtime.QueryHistory.GetAsync( 10, default )).Enabled );

            var saved = (await runtime.GetPreferencesAsync( false, default )).Preferences;
            True( saved.RememberSearches == true && saved.LearnFromUse == false && saved.OmarchyMenuState is null );
            await runtime.UpdatePreferencesAsync( new LauncherPreferences( )
            {
                Sources = saved.Sources,
                RememberSearches = saved.RememberSearches,
                LearnFromUse = saved.LearnFromUse,
                ReduceMotion = true
            }, default );
            True( (await runtime.GetPreferencesAsync( false, default )).Preferences.OmarchyMenuState is null );

            await catalog.LauncherPreferences.ResetAsync( default );
            var reset = (await runtime.GetPreferencesAsync( false, default )).Preferences;
            True( reset.RememberSearches is null && reset.LearnFromUse is null );
            Equal( (1L, true), await RunAsync( "signal" ) );
            True( PreferencesTextFormatter.Format( await runtime.GetPreferencesAsync( false, default ) )
                .Contains( "Remember searches: off (configuration)" ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    // A read that began before a choice was applied never restores the older
    // choice it read; a search after a choice saved elsewhere, and a runtime
    // starting up, apply that choice.
    private static async Task HistoryChoicesReadBeforeAChangeNeverRestoreItAsync( )
    {
        HistoryPolicy policy = new( new PrivacyOptions { StoreQueryHistory = false } );
        var version = policy.Version;
        policy.Apply( new LauncherPreferences( ) { RememberSearches = false } );
        policy.ApplyRead( new LauncherPreferences( ) { RememberSearches = true }, version );
        True( !policy.RememberSearches );
        policy.ApplyRead( new LauncherPreferences( ) { RememberSearches = true }, policy.Version );
        True( policy.RememberSearches );

        var root = Directory.CreateTempSubdirectory( "commanddeck-history-reads-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Indexing = new IndexingOptions { Roots = [], IndexDesktopApplications = false, IndexOmarchyCommands = false },
                Privacy = new PrivacyOptions { StoreQueryHistory = false },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage, privacy: options.Privacy, searchOptions: options.Search );
            await catalog.InitializeAsync( default );
            var current = await catalog.LauncherPreferences.GetPreferencesAsync( default );
            await catalog.LauncherPreferences.ReplacePreferencesAsync( new LauncherPreferences( )
            {
                Sources = current.Sources,
                RememberSearches = true
            }, default );
            True( !catalog.History.RememberSearches );
            SearchCoordinator search = new( [new StaticSearchProvider( [PromotionRow( "app:signal", "Signal" )] )], options.Search,
                catalog, catalog.LauncherPreferences, catalog.UsageHistory, privacy: options.Privacy, history: catalog.History );
            await search.SearchWithOutcomeAsync( new SearchRequest { Query = "sig" }, SearchPurpose.Interactive, default );
            True( catalog.History.RememberSearches );

            var config = Path.Combine( root, "config.json" );
            await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, CancellationToken.None );
            True( (await runtime.QueryHistory.GetAsync( 10, default )).Enabled );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    // The Omarchy menu state choice is asked on every search: off, no condition runs.
    private static async Task OmarchyMenuStateFollowsItsChoiceAsync( )
    {
        HistoryPolicy history = new( search: new SearchOptions { OmarchyMenuState = true } );
        var calls = 0;
        using OmarchyMenuState state = new( ( _, _, _, _, _ ) =>
        {
            calls++;
            return ValueTask.FromResult( new ProcessResult { ExitCode = 0, StandardOutput = "0:1\n", StandardError = "" } );
        }, wait: TimeSpan.FromSeconds( 5 ), enabled: ( ) => history.OmarchyMenuState );
        SearchResult row = new( )
        {
            Id = "omarchy:style.dark",
            Kind = ResultKinds.Omarchy,
            Title = "Dark",
            SourceId = SourceIds.OmarchyControl,
            Metadata = new( StringComparer.Ordinal ) { [OmarchyMenuState.CheckedKey] = "true" }
        };
        history.Apply( new LauncherPreferences( ) { OmarchyMenuState = false } );
        True( !(await state.ApplyAsync( [row], default ))[0].Metadata.ContainsKey( ResultMetadataKeys.OmarchyChecked ) );
        Equal( 0, calls );
        history.Apply( new LauncherPreferences( ) { OmarchyMenuState = true } );
        Equal( "true", (await state.ApplyAsync( [row], default ))[0].Metadata.GetValueOrDefault( ResultMetadataKeys.OmarchyChecked ) );
        Equal( 1, calls );
    }
}
