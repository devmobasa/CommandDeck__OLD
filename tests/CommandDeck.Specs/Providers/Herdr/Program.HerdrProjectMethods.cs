using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task HerdrProjectActionsUseCachedFoldersAsync( )
    {
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-herdr-project-" ).FullName;
        try
        {
            var path = Directory.CreateDirectory( Path.Combine( directory, "--literal $(never) ; café" ) ).FullName;
            await File.WriteAllTextAsync( Path.Combine( directory, "file.txt" ), "test" );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( directory, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            AcceptedUsageHistory history = new( catalog.UsageHistory );
            var created = false;
            HerdrFake fake = new( )
            {
                Respond = args =>
                {
                    var response = ProjectResponse( args, path, created );
                    created |= args[3] == "create";
                    return response;
                }
            };
            HerdrClient client = new( fake.Runner, static ( ) => true );
            LinuxActionExecutor executor = new( herdr: client );
            SearchCoordinator coordinator = new( [new FolderBrowserProvider( ),
                new ProjectProvider( [new ProjectOptions { Title = "Project", Path = path }] )],
                new SearchOptions { ProviderTimeoutMilliseconds = 2000 } );
            await using var runtime = InvocationFixture( options, catalog, history, executor, coordinator );
            var rows = await runtime.SearchAsync( new SearchRequest { Query = "browse:", BrowseDirectory = directory }, default );
            var folder = rows.Single( row => row.Metadata.GetValueOrDefault( "path" ) == path );
            var action = folder.Actions.Single( static action => action.Kind == ActionKinds.HerdrOpenProject );
            True( rows.Single( static row => row.Title == "file.txt" ).Actions.All( static action => action.Kind != ActionKinds.HerdrOpenProject ) );
            Equal( AgentActionRisks.Confirm, AgentLauncherTools.ClassifyRisk( folder, action ) );
            True( (await runtime.InvokeAsync( folder.Id, action.Id, false, default )).Succeeded );

            var project = (await runtime.SearchAsync( new SearchRequest { Query = "proj:" }, default )).Single( );
            True( (await runtime.InvokeAsync( project.Id, "open-herdr", false, default )).Succeeded );
            Equal( 1, fake.Calls.Count( static call => call.Args[3] == "create" ) );
            Equal( 2, fake.Calls.Count( static call => call.Args[3] == "focus" ) );
            var create = fake.Calls.Single( static call => call.Args[3] == "create" );
            Equal( "--session|default|workspace|create|--cwd|" + path + "|--label|" + Path.GetFileName( path ) + "|--no-focus",
                string.Join( '|', create.Args ) );
            True( create.Limits.ClearEnvironment );
            Equal( HerdrClient.FocusTimeout, create.Timeout );

            var before = fake.Calls.Count;
            var forged = new CommandAction
            {
                Id = action.Id,
                Title = action.Title,
                Kind = action.Kind,
                Arguments = new( StringComparer.Ordinal ) { ["path"] = directory }
            };
            True( !(await executor.ExecuteAsync( folder, forged, "", default )).Succeeded );
            True( !(await executor.ExecuteAsync( new SearchResult( folder ) { SourceId = SourceIds.Files }, action, "", default )).Succeeded );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await runtime.InvokeAsync( project.Id, "open-herdr", false,
                default, new Dictionary<string, string> { ["path"] = directory } ) );
            Directory.Delete( path );
            True( !(await executor.ExecuteAsync( folder, action, "", default )).Succeeded );
            Equal( before, fake.Calls.Count );
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }

    private static ProcessResult ProjectResponse( IReadOnlyList<string> args, string path, bool exists )
    {
        if ( args[2] == "status" )
        {
            return HerdrResult( HerdrStatusRunningJson );
        }

        var quotedPath = ProjectJsonString( path );
        var name = ProjectJsonString( Path.GetFileName( path ) );
        return HerdrResult( (args[2], args[3]) switch
        {
            ("workspace", "list" ) => exists
                ? """{"result":{"type":"workspace_list","workspaces":[{"workspace_id":"w1","label":"renamed","number":1,"pane_count":1}]}}"""
                : """{"result":{"type":"workspace_list","workspaces":[]}}""",
            ("pane", "list" ) => exists
                ? $$$$"""{"result":{"type":"pane_list","panes":[{"pane_id":"w1:t1:p1","workspace_id":"w1","cwd":{{{{quotedPath}}}}}]}}"""
                : """{"result":{"type":"pane_list","panes":[]}}""",
            ("workspace", "create" ) => $$$$"""{"result":{"type":"workspace_created","workspace":{"workspace_id":"w1","label":{{{{name}}}}},"root_pane":{"pane_id":"w1:t1:p1","workspace_id":"w1","cwd":{{{{quotedPath}}}}}}} """,
            ("workspace", "focus" ) => $$$$"""{"result":{"type":"workspace_info","workspace":{"workspace_id":{{{{ProjectJsonString( args[4] )}}}},"focused":true}}}""",
            _ => "{}"
        } );
    }

    private static string ProjectJsonString( string value ) => JsonSerializer.Serialize( value, CommandDeckJsonContext.Default.String );

    private static async Task HerdrProjectReuseMatchesDirectoriesAsync( )
    {
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-herdr-reuse-" ).FullName;
        try
        {
            var path = ProjectJsonString( directory + "/./" );
            foreach ( var worktree in new[] { false, true } )
            {
                HerdrFake fake = new( )
                {
                    Respond = args => args[3] != "list" ? ProjectResponse( args, directory, true ) :
                        HerdrResult( args[2] == "workspace"
                            ? $$$$"""{"result":{"type":"workspace_list","workspaces":[{"workspace_id":"w1","label":"same","number":1,"pane_count":1},{"workspace_id":"w2","label":"different","number":2,"pane_count":1,"focused":true,"worktree":{"checkout_path":{{{{(worktree ? path : "null")}}}}}}]}}"""
                            : $$$$"""{"result":{"type":"pane_list","panes":[{"pane_id":"p1","workspace_id":"w1","cwd":{{{{path}}}}},{"pane_id":"p2","workspace_id":"w2","cwd":{{{{(worktree ? "null" : path)}}}}}]}}""" )
                };
                HerdrClient client = new( fake.Runner, static ( ) => true );
                True( (await client.OpenProjectAsync( directory, default )).Succeeded );
                Equal( "w2", fake.Calls.Single( static call => call.Args[3] == "focus" ).Args[4] );
                True( fake.Calls.All( static call => call.Args[3] != "create" ) );
            }

            HerdrFake sameName = new( )
            {
                Respond = args => args[3] == "list" ? ProjectResponse( args, directory + "-other", true )
                    : ProjectResponse( args, directory, false )
            };
            True( (await new HerdrClient( sameName.Runner, static ( ) => true ).OpenProjectAsync( directory, default )).Succeeded );
            Equal( 1, sameName.Calls.Count( static call => call.Args[3] == "create" ) );
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }

    private static async Task HerdrProjectFailuresNeverRetryAsync( )
    {
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-herdr-failure-" ).FullName;
        try
        {
            foreach ( var operation in new[] { "list", "create", "focus" } )
            {
                foreach ( var response in new[] { HerdrResult( "warning: failed" ), HerdrResult( "{}" ),
                    HerdrResult( "{\"error\":{},\"result\":{}}" ), HerdrResult( "", 1 ),
                    new ProcessResult { ExitCode = 0, StandardOutput = "", StandardError = "", TimedOut = true },
                    new ProcessResult { ExitCode = 0, StandardOutput = "", StandardError = "", OutputLimitExceeded = true } } )
                {
                    HerdrFake fake = new( )
                    {
                        Respond = args => args[3] == operation ? response : ProjectResponse( args, directory, false )
                    };
                    HerdrClient client = new( fake.Runner, static ( ) => true );
                    True( !(await client.OpenProjectAsync( directory, default )).Succeeded );
                    Equal( operation == "list" ? 0 : 1, fake.Calls.Count( static call => call.Args[3] == "create" ) );
                    Equal( operation == "focus" ? 1 : 0, fake.Calls.Count( static call => call.Args[3] == "focus" ) );
                }
            }

            HerdrFake unavailable = new( );
            var missing = new HerdrClient( unavailable.Runner, static ( ) => false );
            True( !(await missing.OpenProjectAsync( directory, default )).Succeeded );
            using CancellationTokenSource canceled = new( );
            canceled.Cancel( );
            await ThrowsAsync<OperationCanceledException>( async ( ) => await missing.OpenProjectAsync( directory, canceled.Token ) );
            Equal( 0, unavailable.Calls.Count );
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }
}
