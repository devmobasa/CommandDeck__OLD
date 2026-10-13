namespace CommandDeck.Specs;

public static partial class Program
{
    // Fixtures are Herdr 0.9.0 shaped: extra and unknown fields, one record with
    // a control character in its id, one non-object entry, an orphan tab, and a
    // tab without an id. Labels are invented.
    private const string HerdrStatusRunningJson = """
        {"status":"running","running":true,"version":"0.9.0","protocol":22,"capabilities":{"live_handoff":true},
         "compatible":true,"endpoint_compatible":true,"socket":"/tmp/herdr.sock","session":null,"restart_needed":false}
        """;
    private const string HerdrStatusStoppedJson = """
        {"status":"not_running","running":false,"version":null,"protocol":null,"compatible":null,"endpoint_compatible":null,"socket":"/tmp/herdr.sock"}
        """;
    private const string HerdrWorkspaceListJson = """
        {"id":"cli:workspace:list","result":{"type":"workspace_list","workspaces":[
          {"workspace_id":"w1","number":1,"label":"CommandDeck","focused":true,"pane_count":5,"tab_count":2,"active_tab_id":"w1:t1","agent_status":"working","worktree":{"repo_name":"commanddeck"}},
          {"workspace_id":"w2","number":2,"label":"Notes","focused":false,"pane_count":1,"tab_count":2,"agent_status":"idle"},
          {"workspace_id":"w3","number":3,"label":"Notes","focused":false,"pane_count":1,"tab_count":0},
          {"workspace_id":"bad\u0001id","number":4,"label":"Broken"},
          "not-an-object"
        ]}}
        """;
    private const string HerdrTabListJson = """
        {"id":"cli:tab:list","result":{"tabs":[
          {"tab_id":"w1:t1","workspace_id":"w1","number":1,"label":"tests","focused":true,"pane_count":2,"agent_status":"idle"},
          {"tab_id":"w1:t2","workspace_id":"w1","number":2,"label":"release","focused":false,"pane_count":1},
          {"tab_id":"w2:t1","workspace_id":"w2","number":1,"label":"scratch","focused":false,"pane_count":1},
          {"tab_id":"w2:t2","workspace_id":"w2","number":2,"label":"commanddeck","focused":false,"pane_count":1},
          {"tab_id":"gone:t1","workspace_id":"gone","number":1,"label":"orphan","focused":false,"pane_count":3},
          {"tab_id":"","workspace_id":"w1","number":9,"label":"invalid"}
        ],"type":"tab_list"}}
        """;

    private static async Task HerdrProviderIsGatedAsync( )
    {
        HerdrFake fake = new( );
        HerdrProvider provider = new( new HerdrClient( fake.Runner, static ( ) => true ) );
        SearchRequest disabled = new( )
        {
            Query = "tests",
            Limit = 20,
            EnabledSearchSources = [SourceIds.Files]
        };
        SearchRequest enabled = new( )
        {
            Query = "tests",
            Limit = 20,
            EnabledSearchSources = [SourceIds.Herdr]
        };

        Equal( 0, (await provider.SearchAsync( QueryParser.Parse( "tests" ), disabled, CancellationToken.None )).Count );
        Equal( 0, (await provider.SearchAsync( QueryParser.Parse( "herdr: tests" ), disabled, CancellationToken.None )).Count );
        Equal( 0, (await provider.SearchAsync( QueryParser.Parse( "file: tests" ), enabled, CancellationToken.None )).Count );
        Equal( 0, (await provider.SearchAsync( QueryParser.Parse( "-herdr: tests" ), enabled, CancellationToken.None )).Count );
        Equal( 0, (await provider.SearchAsync( QueryParser.Parse( "-hspace: -htab: tests" ), enabled, CancellationToken.None )).Count );
        Equal( 0, (await provider.SearchAsync( QueryParser.Parse( "-ws: " ), enabled, CancellationToken.None )).Count );
        Equal( 0, fake.Calls.Count );

        HerdrProvider missing = new( new HerdrClient( fake.Runner, static ( ) => false ) );
        Equal( 0, (await missing.SearchAsync( QueryParser.Parse( "tests" ), enabled, CancellationToken.None )).Count );
        Equal( 0, fake.Calls.Count );

        using CancellationTokenSource cancelled = new( );
        cancelled.Cancel( );
        await ThrowsAsync<OperationCanceledException>( async ( ) =>
            await provider.SearchAsync( QueryParser.Parse( "tests" ), enabled, cancelled.Token ) );
        Equal( 0, fake.Calls.Count );
    }

    private static async Task HerdrProviderListsConcurrentlyAsync( )
    {
        TaskCompletionSource<ProcessResult> workspaces = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource<ProcessResult> tabs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        var started = 0;
        BoundedProcessRunner runner = ( _, args, _, _, _ ) =>
        {
            Interlocked.Increment( ref started );
            return new ValueTask<ProcessResult>( args[2] == "workspace" ? workspaces.Task : tabs.Task );
        };
        HerdrProvider provider = new( new HerdrClient( runner, static ( ) => true ) );

        var search = provider.SearchAsync(
            QueryParser.Parse( "tests" ),
            new SearchRequest { Query = "tests", Limit = 20, EnabledSearchSources = [SourceIds.Herdr] },
            CancellationToken.None ).AsTask( );
        await Task.Delay( 50 );

        Equal( 2, started );
        True( !search.IsCompleted );

        workspaces.SetResult( HerdrResult( HerdrWorkspaceListJson ) );
        tabs.SetResult( HerdrResult( HerdrTabListJson ) );
        var results = await search.WaitAsync( TimeSpan.FromSeconds( 5 ) );

        Equal( "herdr:tab:w1:t1", results.Single( ).Id );
    }

    private static async Task HerdrProviderProjectsInventoryAsync( )
    {
        var results = await SearchHerdrAsync( "herdr: " );

        var space = results.Single( result => result.Id == "herdr:workspace:w1" );
        Equal( ResultKinds.Workspace, space.Kind );
        Equal( "CommandDeck", space.Title );
        Equal( "Space 1 · 2 tabs · 5 panes · focused", space.Subtitle );
        Equal( HerdrProvider.Icon, space.Icon );
        Equal( SourceIds.Herdr, space.SourceId );
        Equal( SearchCoordinator.HerdrProviderId, space.ProviderId );
        Equal( "Number=1|Tabs=2|Panes=5|Agent status=working|Herdr ID=w1|tests=2 panes · idle|release=1 pane", string.Join( '|', space.Details.Select( detail => $"{detail.Label}={detail.Value}" ) ) );
        Equal( HerdrProvider.SwitchSpaceActionId, space.Actions[0].Id );
        Equal( 3, space.Actions.Count );
        Equal( ActionKinds.HerdrFocus, space.Actions[0].Kind );
        Equal( "workspace", space.Actions[0].Arguments["targetKind"] );
        Equal( "w1", space.Actions[0].Arguments["targetId"] );
        Equal( "workspace", space.Metadata[HerdrProvider.TargetKindMetadata] );
        Equal( "w1", space.Metadata[HerdrProvider.TargetIdMetadata] );
        Equal( "true", space.Metadata[HerdrProvider.FocusedMetadata] );
        True( !space.Metadata.ContainsKey( "workspace" ) );

        var tab = results.Single( result => result.Id == "herdr:tab:w1:t2" );
        Equal( ResultKinds.Tab, tab.Kind );
        Equal( "release", tab.Title );
        Equal( "In CommandDeck · Tab 2 · 1 pane", tab.Subtitle );
        Equal( "Space=CommandDeck|Number=2|Panes=1|Herdr ID=w1:t2", string.Join( '|', tab.Details.Select( detail => $"{detail.Label}={detail.Value}" ) ) );
        Equal( HerdrProvider.SwitchTabActionId, tab.Actions[0].Id );
        Equal( 2, tab.Actions.Count );
        Equal( "tab", tab.Actions[0].Arguments["targetKind"] );
        Equal( "w1:t2", tab.Actions[0].Arguments["targetId"] );
        Equal( "w1", tab.Metadata[HerdrProvider.WorkspaceIdMetadata] );
        Equal( "CommandDeck", tab.Metadata[HerdrProvider.WorkspaceLabelMetadata] );

        var orphan = results.Single( result => result.Id == "herdr:tab:gone:t1" );
        Equal( $"In {HerdrProvider.UnknownSpaceLabel} · Tab 1 · 3 panes", orphan.Subtitle );

        // Duplicate labels stay distinct by id; the sanitized records are absent.
        Equal( 2, results.Count( result => result.Title == "Notes" ) );
        True( results.All( result => !result.Id.Contains( '' ) && result.Id != "herdr:tab:" ) );
        foreach ( var result in results )
        {
            True( result.Details.All( static detail => detail.Value.Length <= 16 * 1024 && !detail.Value.Any( char.IsControl ) ) );
        }
    }

    private static async Task HerdrProviderFiltersAndRanksAsync( )
    {
        var byParent = await SearchHerdrAsync( "commanddeck" );
        Equal( "herdr:workspace:w1", byParent[0].Id );
        var direct = byParent.Single( result => result.Id == "herdr:tab:w2:t2" );
        var viaParent = byParent.Single( result => result.Id == "herdr:tab:w1:t2" );
        True( direct.Score > viaParent.Score, "an exact tab label outranks a tab found through its space" );
        True( byParent.Any( result => result.Id == "herdr:tab:w1:t1" ) );
        True( !byParent.Any( result => result.Id == "herdr:tab:w2:t1" ) );

        var focusedFirst = await SearchHerdrAsync( "notes" );
        True( focusedFirst.Any( result => result.Id == "herdr:workspace:w2" ) && focusedFirst.Any( result => result.Id == "herdr:workspace:w3" ) );

        var spaces = await SearchHerdrAsync( "hspace: notes" );
        True( spaces.Count > 0 && spaces.All( result => result.Kind == ResultKinds.Workspace ) );
        var tabs = await SearchHerdrAsync( "htab: tests" );
        Equal( "herdr:tab:w1:t1", tabs.Single( ).Id );
        var withoutTabs = await SearchHerdrAsync( "-htab: commanddeck" );
        True( withoutTabs.Count > 0 && withoutTabs.All( result => result.Kind == ResultKinds.Workspace ) );
        var withoutSpaces = await SearchHerdrAsync( "-hspace: commanddeck" );
        True( withoutSpaces.Count > 0 && withoutSpaces.All( result => result.Kind == ResultKinds.Tab ) );
        Equal( 0, (await SearchHerdrAsync( "zzz-no-match" )).Count );
    }

    private static async Task HerdrProviderBrowsesDeterministicallyAsync( )
    {
        var browse = await SearchHerdrAsync( "herdr: " );

        Equal(
            "herdr:workspace:w1|herdr:tab:w1:t1|herdr:workspace:w2|herdr:workspace:w3|herdr:tab:w1:t2|herdr:tab:w2:t1|herdr:tab:w2:t2|herdr:tab:gone:t1",
            string.Join( '|', browse.Select( static result => result.Id ) ) );

        var limited = await SearchHerdrAsync( "herdr: ", limit: 2 );
        Equal( 2, limited.Count );

        // Oversized inventories are cut at the record caps before projection.
        var manyTabs = string.Join( ',', Enumerable.Range( 0, HerdrClient.MaximumTabs + 50 ).Select( index =>
            $$"""{"tab_id":"w1:t{{index}}","workspace_id":"w1","number":{{index}},"label":"tab {{index}}","pane_count":1}""" ) );
        HerdrFake fake = new( )
        {
            Respond = args => args[2] == "tab"
                ? HerdrResult( "{\"result\":{\"type\":\"tab_list\",\"tabs\":[" + manyTabs + "]}}" )
                : HerdrResult( HerdrWorkspaceListJson )
        };
        HerdrProvider provider = new( new HerdrClient( fake.Runner, static ( ) => true ) );
        var capped = await provider.SearchAsync(
            QueryParser.Parse( "htab: " ),
            new SearchRequest { Query = "htab: ", Limit = 2000, EnabledSearchSources = [SourceIds.Herdr] },
            CancellationToken.None );
        Equal( HerdrClient.MaximumTabs, capped.Count );
    }

    private static async Task HerdrProviderStaysSilentOnFailureAsync( )
    {
        HerdrFake tabsFail = new( )
        {
            Respond = args => args[2] == "tab"
                ? HerdrResult( "", 1, """{"id":"cli:tab:list","error":{"code":"server_not_running","message":"x"}}""" )
                : HerdrResult( HerdrWorkspaceListJson )
        };
        Equal( 0, (await SearchHerdrAsync( "commanddeck", tabsFail )).Count );

        HerdrFake malformed = new( )
        {
            Respond = args => args[2] == "tab" ? HerdrResult( "{not json" ) : HerdrResult( HerdrWorkspaceListJson )
        };
        Equal( 0, (await SearchHerdrAsync( "commanddeck", malformed )).Count );

        HerdrFake timedOut = new( )
        {
            Respond = args => args[2] == "workspace"
                ? new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", TimedOut = true }
                : HerdrResult( HerdrTabListJson )
        };
        Equal( 0, (await SearchHerdrAsync( "commanddeck", timedOut )).Count );
    }

    private static async Task HerdrCoordinatorGatesAndHomeAsync( )
    {
        var database = Path.Combine( Path.GetTempPath( ), $"commanddeck-herdr-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            HerdrFake fake = new( );
            HerdrProvider provider = new( new HerdrClient( fake.Runner, static ( ) => true ) );
            SearchCoordinator coordinator = new( [provider], new SearchOptions( ), catalog, catalog );

            // Enabled by default in typed search, off Home by default.
            var typed = await coordinator.SearchAsync( new SearchRequest { Query = "commanddeck", Limit = 20 }, CancellationToken.None );
            True( typed.Any( result => result.Id == "herdr:workspace:w1" ) );
            True( typed.Any( result => result.Id == "herdr:tab:w2:t2" ) );
            Equal( 2, fake.Calls.Count );
            var direct = typed.Single( result => result.Id == "herdr:tab:w2:t2" );
            var viaParent = typed.Single( result => result.Id == "herdr:tab:w1:t2" );
            True( direct.Score > viaParent.Score );

            _ = await coordinator.SearchAsync( new SearchRequest { Query = "", Limit = 20 }, CancellationToken.None );
            _ = await coordinator.SearchAsync( new SearchRequest { Query = "> commanddeck", Limit = 20 }, CancellationToken.None );
            _ = await coordinator.SearchAsync( new SearchRequest { Query = "-herdr: commanddeck", Limit = 20 }, CancellationToken.None );
            _ = await coordinator.SearchAsync( new SearchRequest { Query = "file: commanddeck", Limit = 20 }, CancellationToken.None );
            Equal( 2, fake.Calls.Count );

            // Disabling the source stops process creation even for herdr: queries.
            await SetHerdrPreferenceAsync( catalog, enabledInSearch: false, enabledOnHome: false );
            _ = await coordinator.SearchAsync( new SearchRequest { Query = "commanddeck", Limit = 20 }, CancellationToken.None );
            _ = await coordinator.SearchAsync( new SearchRequest { Query = "herdr: commanddeck", Limit = 20 }, CancellationToken.None );
            _ = await coordinator.SearchAsync( new SearchRequest { Query = "hattention:", HerdrWorkspaceId = "w1", Limit = 20 }, CancellationToken.None );
            _ = await coordinator.SearchAsync( new SearchRequest { HerdrWorkspaceId = "w1", Limit = 20 }, CancellationToken.None );
            Equal( 2, fake.Calls.Count );

            // Re-enabling with Home adds one bounded section without a restart.
            await SetHerdrPreferenceAsync( catalog, enabledInSearch: true, enabledOnHome: true );
            var home = await coordinator.SearchAsync( new SearchRequest { Query = "", Limit = 20 }, CancellationToken.None );
            var section = home.Where( result => result.Section?.Id == "herdr" ).ToArray( );
            Equal( 4, section.Length );
            Equal( "Herdr", section[0].Section!.Title );
            Equal( "herdr:workspace:w1", section[0].Id );
            Equal( 4, fake.Calls.Count );

            await SetHerdrPreferenceAsync( catalog, enabledInSearch: true, enabledOnHome: false );
            _ = await coordinator.SearchAsync( new SearchRequest { Query = "", Limit = 20 }, CancellationToken.None );
            Equal( 4, fake.Calls.Count );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task HerdrActionExecutesFocusAsync( )
    {
        var results = await SearchHerdrAsync( "herdr: " );
        var tab = results.Single( result => result.Id == "herdr:tab:w1:t2" );
        var space = results.Single( result => result.Id == "herdr:workspace:w2" );
        HerdrFake fake = new( )
        {
            Respond = args => args[2] == "tab"
                ? HerdrResult( """{"id":"cli:tab:focus","result":{"tab":{"tab_id":"w1:t2","focused":true},"type":"tab_info"}}""" )
                : HerdrResult( """{"id":"cli:workspace:focus","result":{"type":"workspace_info","workspace":{"workspace_id":"w2","focused":true}}}""" )
        };
        LinuxActionExecutor executor = new( herdr: new HerdrClient( fake.Runner, static ( ) => true ) );

        var switched = await executor.ExecuteAsync( tab, tab.Actions[0], "", CancellationToken.None );
        True( switched.Succeeded, switched.Message );
        Equal( "--session default tab focus w1:t2", string.Join( ' ', fake.Calls.Single( ).Args ) );

        var switchedSpace = await executor.ExecuteAsync( space, space.Actions[0], "", CancellationToken.None );
        True( switchedSpace.Succeeded, switchedSpace.Message );
        Equal( "--session default workspace focus w2", string.Join( ' ', fake.Calls[1].Args ) );

        // Forged arguments never reach the client.
        CommandAction forged = new( )
        {
            Id = tab.Actions[0].Id,
            Title = tab.Actions[0].Title,
            Kind = ActionKinds.HerdrFocus,
            Arguments = new Dictionary<string, string>( StringComparer.Ordinal ) { ["targetKind"] = "tab", ["targetId"] = "w9:t9" }
        };
        True( !(await executor.ExecuteAsync( tab, forged, "", CancellationToken.None )).Succeeded );
        CommandAction wrongKind = new( )
        {
            Id = "x",
            Title = "x",
            Kind = ActionKinds.HerdrFocus,
            Arguments = new Dictionary<string, string>( StringComparer.Ordinal ) { ["targetKind"] = "workspace", ["targetId"] = "w1:t2" }
        };
        True( !(await executor.ExecuteAsync( tab, wrongKind, "", CancellationToken.None )).Succeeded );
        SearchResult foreign = new( tab )
        {
            SourceId = SourceIds.Files,
            Kind = ResultKinds.File
        };
        True( !(await executor.ExecuteAsync( foreign, tab.Actions[0], "", CancellationToken.None )).Succeeded );
        True( !(await executor.ExecuteAsync( tab, new CommandAction { Id = "x", Title = "x", Kind = ActionKinds.HerdrFocus }, "", CancellationToken.None )).Succeeded );
        Equal( 2, fake.Calls.Count );

        True( !(await new LinuxActionExecutor( ).ExecuteAsync( tab, tab.Actions[0], "", CancellationToken.None )).Succeeded );

        HerdrFake stale = new( )
        {
            Respond = _ => HerdrResult( "", 1, """{"error":{"code":"tab_not_found","message":"tab w1:t2 not found"},"id":"cli:tab:focus"}""" )
        };
        var failed = await new LinuxActionExecutor( herdr: new HerdrClient( stale.Runner, static ( ) => true ) )
            .ExecuteAsync( tab, tab.Actions[0], "", CancellationToken.None );
        True( !failed.Succeeded );
        Equal( "That Herdr tab no longer exists. Search again.", failed.Message );
        Equal( 1, stale.Calls.Count );
    }

    private static void HerdrFocusRiskClassification( )
    {
        var results = SearchHerdrAsync( "herdr: " ).GetAwaiter( ).GetResult( );
        var tab = results.Single( result => result.Id == "herdr:tab:w1:t1" );
        var space = results.Single( result => result.Id == "herdr:workspace:w1" );

        Equal( AgentActionRisks.Immediate, AgentLauncherTools.ClassifyRisk( tab, tab.Actions[0] ) );
        Equal( AgentActionRisks.Immediate, AgentLauncherTools.ClassifyRisk( space, space.Actions[0] ) );

        SearchResult forgedFile = new( tab )
        {
            SourceId = SourceIds.Files
        };
        Equal( AgentActionRisks.Confirm, AgentLauncherTools.ClassifyRisk( forgedFile, tab.Actions[0] ) );
        SearchResult forgedKind = new( tab )
        {
            Kind = ResultKinds.File
        };
        Equal( AgentActionRisks.Confirm, AgentLauncherTools.ClassifyRisk( forgedKind, tab.Actions[0] ) );
    }

    private static async Task HerdrPreferenceMergesAsync( )
    {
        var database = Path.Combine( Path.GetTempPath( ), $"commanddeck-herdr-prefs-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );

            // A stored preference set from before Herdr existed, with custom order.
            await using ( var connection = await catalog.OpenAsync( CancellationToken.None ) )
            await using ( var seed = connection.CreateCommand( ) )
            {
                seed.CommandText = """
                    INSERT INTO source_preferences(source_id, enabled_in_search, enabled_on_home, position, updated_unix_ms)
                    VALUES ('windows-workspaces', 0, 0, 1, 0),
                           ('applications', 1, 1, 2, 0),
                           ('files', 1, 0, 3, 0);
                    """;
                await seed.ExecuteNonQueryAsync( );
            }

            var preferences = await catalog.GetPreferencesAsync( CancellationToken.None );

            // Stored toggles and relative order survive; positions are resequenced.
            var windows = preferences.Sources.Single( source => source.SourceId == SourceIds.WindowsWorkspaces );
            True( !windows.EnabledInSearch && !windows.EnabledOnHome );
            var applications = preferences.Sources.Single( source => source.SourceId == SourceIds.Applications );
            var files = preferences.Sources.Single( source => source.SourceId == SourceIds.Files );
            True( windows.Position < applications.Position && applications.Position < files.Position );
            True( applications.EnabledOnHome && !files.EnabledOnHome );
            var herdr = preferences.Sources.Single( source => source.SourceId == SourceIds.Herdr );
            True( herdr.EnabledInSearch && !herdr.EnabledOnHome );
            Equal( preferences.Sources.Count, preferences.Sources.Select( static source => source.Position ).Distinct( ).Count( ) );

            var defaults = LauncherPreferences.CreateDefault( );
            Equal( defaults.Sources.Count, defaults.Sources.Select( static source => source.Position ).Distinct( ).Count( ) );
            Equal(
                defaults.Sources.Single( static source => source.SourceId == SourceIds.WindowsWorkspaces ).Position + 1,
                defaults.Sources.Single( static source => source.SourceId == SourceIds.Herdr ).Position );
            Equal( "Herdr", LauncherSourceRegistry.Label( SourceIds.Herdr ) );
            True( SourceIds.All.Contains( SourceIds.Herdr ) );
            Equal( "herdr", QueryParser.Parse( "herdr: x" ).IncludedFilters.Single( ).Id );
            Equal( "herdr-spaces", QueryParser.Parse( "hspaces: x" ).IncludedFilters.Single( ).Id );
            Equal( "herdr-tabs", QueryParser.Parse( "htab: x" ).IncludedFilters.Single( ).Id );
            Equal( "herdr-tabs", QueryParser.Parse( "-herdr-tab: x" ).ExcludedFilters.Single( ).Id );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task SetHerdrPreferenceAsync( SqliteCatalog catalog, bool enabledInSearch, bool enabledOnHome )
    {
        var current = await catalog.GetPreferencesAsync( CancellationToken.None );
        await catalog.ReplacePreferencesAsync( new LauncherPreferences
        {
            Sources = current.Sources.Select( source => new SourcePreference
            {
                SourceId = source.SourceId,
                EnabledInSearch = source.SourceId == SourceIds.Herdr ? enabledInSearch : source.EnabledInSearch,
                EnabledOnHome = source.SourceId == SourceIds.Herdr ? enabledOnHome : source.EnabledOnHome,
                Position = source.Position
            } ).ToList( )
        }, CancellationToken.None );
    }

    private static async Task<HerdrStatus> StatusFor( string json )
    {
        HerdrFake fake = new( )
        {
            Respond = _ => HerdrResult( json )
        };

        return await new HerdrClient( fake.Runner, static ( ) => true ).GetStatusAsync( CancellationToken.None );
    }

    private static async Task<HerdrFocusOutcome> FocusFor( ProcessResult response, string targetKind = HerdrTargetKinds.Tab )
    {
        HerdrFake fake = new( )
        {
            Respond = _ => response
        };

        return await new HerdrClient( fake.Runner, static ( ) => true ).FocusAsync( targetKind, "w1:t1", CancellationToken.None );
    }

    private static async Task<IReadOnlyList<SearchResult>> SearchHerdrAsync( string query, HerdrFake? fake = null, int limit = 50 )
    {
        HerdrProvider provider = new( new HerdrClient( (fake ?? new HerdrFake( )).Runner, static ( ) => true ) );

        return await provider.SearchAsync(
            QueryParser.Parse( query ),
            new SearchRequest { Query = query, Limit = limit, EnabledSearchSources = [SourceIds.Herdr] },
            CancellationToken.None );
    }

    private static ProcessResult HerdrResult( string standardOutput, int exitCode = 0, string error = "" ) => new( )
    {
        ExitCode = exitCode,
        StandardOutput = standardOutput,
        StandardError = error
    };

    private sealed class HerdrFake
    {
        public List<(string File, IReadOnlyList<string> Args, TimeSpan Timeout, ProcessExecutionLimits Limits)> Calls { get; } = [];

        public Func<IReadOnlyList<string>, ProcessResult> Respond { get; init; } = static args => args[2] switch
        {
            "status" => HerdrResult( HerdrStatusRunningJson ),
            "workspace" => HerdrResult( HerdrWorkspaceListJson ),
            "tab" => HerdrResult( HerdrTabListJson ),
            _ => HerdrResult( "", 2, "usage" )
        };

        public BoundedProcessRunner Runner => ( file, args, timeout, limits, _ ) =>
        {
            lock ( Calls )
            {
                Calls.Add( (file, args, timeout, limits) );
            }

            return ValueTask.FromResult( Respond( args ) );
        };
    }

    // Keeps the environment assertion readable: fixed order, no set semantics.
    private sealed class EnvironmentKeyOrder : IComparer<string>
    {
        private static readonly string[] Order = ["PATH", "HOME", "XDG_CONFIG_HOME", "XDG_RUNTIME_DIR", "LANG", "LC_ALL"];

        public int Compare( string? x, string? y ) =>
            Array.IndexOf( Order, x ).CompareTo( Array.IndexOf( Order, y ) );
    }
}
