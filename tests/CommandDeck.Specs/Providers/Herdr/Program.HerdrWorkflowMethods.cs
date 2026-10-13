namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task HerdrSpacePreviewIsBoundedAsync( )
    {
        var tabs = Enumerable.Range( 1, 20 ).Select( number =>
            $$$$"""{"tab_id":"w1:t{{{{number}}}}","workspace_id":"w1","number":{{{{number}}}},"label":"preview {{{{number}}}}","pane_count":2,"agent_status":"blocked"}""" );
        var json = "{\"result\":{\"type\":\"tab_list\",\"tabs\":[" + string.Join( ',', tabs ) + "]}}";
        HerdrFake fake = new( )
        {
            Respond = args => HerdrResult( args[2] == "workspace" ? HerdrWorkspaceListJson : json )
        };
        var results = await SearchHerdrAsync( "hspace: CommandDeck", fake );
        var space = results.Single( );

        Equal( 8, space.Details.Count( static detail => detail.Label.StartsWith( "preview ", StringComparison.Ordinal ) ) );
        Equal( "2 panes · blocked", space.Details.Single( static detail => detail.Label == "preview 8" ).Value );
        Equal( "12 more — choose Browse tabs", space.Details.Single( static detail => detail.Label == "More tabs" ).Value );
    }

    private static async Task HerdrAttentionAndScopeAsync( )
    {
        HerdrFake fake = new( )
        {
            Respond = args => HerdrResult( args[2] == "workspace" ? HerdrWorkspaceListJson :
                HerdrTabListJson.Replace( "\"label\":\"orphan\"", "\"label\":\"orphan\",\"agent_status\":\"blocked\"", StringComparison.Ordinal ) )
        };
        HerdrProvider provider = new( new HerdrClient( fake.Runner, static ( ) => true ) );
        SearchCoordinator coordinator = new( [provider], new SearchOptions( ) );

        // The blocked tab is beyond the ordinary four-row candidate budget.
        var attention = await coordinator.SearchAsync( new SearchRequest { Query = "hattention: -file:", Limit = 1 }, default );
        Equal( "herdr:tab:gone:t1", attention.Single( ).Id );
        var union = await coordinator.SearchAsync( new SearchRequest { Query = "herdr: hattention:", Limit = 20 }, default );
        Equal( 8, union.Count );
        var excluded = await coordinator.SearchAsync( new SearchRequest { Query = "herdr: -hattention:", Limit = 20 }, default );
        Equal( 7, excluded.Count );
        True( excluded.All( static row => !HerdrSearch.NeedsAttention( row ) ) );

        foreach ( var query in new[] { "", "htab:", "herdr: -file:" } )
        {
            var scoped = await coordinator.SearchAsync( new SearchRequest { Query = query, HerdrWorkspaceId = "w1", Limit = 20 }, default );
            Equal( "herdr:tab:w1:t1|herdr:tab:w1:t2", string.Join( '|', scoped.Select( static row => row.Id ) ) );
        }

        var filtered = await coordinator.SearchAsync( new SearchRequest { Query = "title:tests metadata:CommandDeck", HerdrWorkspaceId = "w1" }, default );
        Equal( "herdr:tab:w1:t1", filtered.Single( ).Id );
        foreach ( var scope in new[] { "w", "w10", "", "w1\n" } )
        {
            Equal( 0, (await coordinator.SearchAsync( new SearchRequest { Query = "herdr:", HerdrWorkspaceId = scope }, default )).Count );
        }

        Equal( 0, (await coordinator.SearchAsync( new SearchRequest { Query = "hattention:", HerdrWorkspaceId = "w1" }, default )).Count );
        Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new ProtocolRequest
        {
            RequestId = 1,
            Operation = ProtocolOperations.Search,
            HerdrWorkspaceId = "w1\n"
        } ) );
    }

    private static async Task HerdrTabEditsUseCachedTargetsAsync( )
    {
        var directory = Directory.CreateTempSubdirectory( "commanddeck-herdr-edits-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( directory, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            AcceptedUsageHistory history = new( catalog.UsageHistory );
            HerdrFake fake = new( )
            {
                Respond = TabEditResponse
            };
            HerdrClient client = new( fake.Runner, static ( ) => true );
            HerdrProvider provider = new( client );
            SearchCoordinator coordinator = new( [provider], options.Search );
            LinuxActionExecutor executor = new( herdr: client );
            await using var runtime = InvocationFixture( options, catalog, history, executor, coordinator );
            var results = await runtime.SearchAsync( new SearchRequest { Query = "herdr:", Limit = 20 }, default );
            var tab = results.Single( static row => row.Id == "herdr:tab:w1:t1" );
            var space = results.Single( static row => row.Id == "herdr:workspace:w1" );
            const string literal = "--literal $(never) ; café";
            var values = new Dictionary<string, string> { ["Name"] = literal };

            True( (await runtime.InvokeAsync( space.Id, "new-tab", false, default, values )).Succeeded );
            True( (await runtime.InvokeAsync( tab.Id, "rename-tab", false, default, values )).Succeeded );
            var create = fake.Calls.Single( static call => call.Args[3] == "create" );
            Equal( "--session|default|tab|create|--workspace|w1|--label|" + literal + "|--no-focus", string.Join( '|', create.Args ) );
            Equal( "--session|default|tab|rename|w1:t1|" + literal,
                string.Join( '|', fake.Calls.Single( static call => call.Args[3] == "rename" ).Args ) );
            True( create.Limits.ClearEnvironment );
            Equal( HerdrClient.FocusTimeout, create.Timeout );
            Equal( 0, history.Writes );
            Equal( AgentActionRisks.Denied, AgentLauncherTools.ClassifyRisk( space, space.Actions[1] ) );
            Equal( AgentActionRisks.Confirm, AgentLauncherTools.ClassifyRisk( tab, tab.Actions[1] ) );

            var calls = fake.Calls.Count;
            True( !(await runtime.InvokeAsync( "herdr:tab:w9:t9", "rename-tab", false, default, values )).Succeeded );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await runtime.InvokeAsync(
                tab.Id, "rename-tab", false, default, new Dictionary<string, string> { ["targetId"] = "w9:t9" } ) );
            var forged = ActionInputValues.Bind( tab.Actions[1], values );
            forged.Arguments["targetId"] = "w9:t9";
            True( !(await executor.ExecuteAsync( tab, forged, "", default )).Succeeded );
            True( !(await executor.ExecuteAsync( new SearchResult( tab ) { SourceId = SourceIds.Files },
                ActionInputValues.Bind( tab.Actions[1], values ), "", default )).Succeeded );
            Equal( calls, fake.Calls.Count );
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }

    private static ProcessResult TabEditResponse( IReadOnlyList<string> args )
    {
        if ( args[3] == "list" )
        {
            return HerdrResult( args[2] == "workspace" ? HerdrWorkspaceListJson : HerdrTabListJson );
        }

        var create = args[3] == "create";
        var name = System.Text.Json.JsonSerializer.Serialize( args[create ? 7 : 5], CommandDeckJsonContext.Default.String );
        return HerdrResult( $$$$"""{"result":{"type":"{{{{(create ? "tab_created" : "tab_info")}}}}","tab":{"tab_id":"w1:t1","workspace_id":"w1","label":{{{{name}}}}}}} """ );
    }

    private static async Task HerdrTabEditsRejectUnconfirmedChangesAsync( )
    {
        foreach ( var response in new[]
        {
            HerdrResult( "warning: failed" ), HerdrResult( "{}" ),
            HerdrResult( """{"result":{"type":"tab_info","tab":{"tab_id":"other","label":"name"}}}""" ),
            HerdrResult( """{"result":{"type":"tab_created","tab":{"tab_id":"w1:t1","workspace_id":"other","label":"name"}}}""" ),
            HerdrResult( "", 1, "tab_not_found" ),
            new ProcessResult { ExitCode = 0, StandardOutput = "", StandardError = "", TimedOut = true },
            new ProcessResult { ExitCode = 0, StandardOutput = "", StandardError = "", OutputLimitExceeded = true }
        } )
        {
            HerdrFake fake = new( )
            {
                Respond = _ => response
            };
            HerdrClient client = new( fake.Runner, static ( ) => true );
            True( !(await client.CreateTabAsync( "w1", "name", default )).Succeeded );
            True( !(await client.RenameTabAsync( "w1:t1", "name", default )).Succeeded );
            Equal( 2, fake.Calls.Count );
        }

        HerdrFake invalid = new( );
        HerdrClient invalidClient = new( invalid.Runner, static ( ) => true );
        foreach ( var name in new[] { " ", "bad\nname", new string( 'x', 513 ) } )
        {
            True( !(await invalidClient.CreateTabAsync( "w1", name, default )).Succeeded );
            True( !(await invalidClient.RenameTabAsync( "w1:t1", name, default )).Succeeded );
        }

        using CancellationTokenSource canceled = new( );
        canceled.Cancel( );
        await ThrowsAsync<OperationCanceledException>( async ( ) => await invalidClient.CreateTabAsync( "w1", "name", canceled.Token ) );
        Equal( 0, invalid.Calls.Count );
    }
}
