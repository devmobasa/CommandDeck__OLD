namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task HerdrProjectRejectsIncompleteInventoryAsync( )
    {
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-herdr-inventory-" ).FullName;
        try
        {
            var excessive = string.Join( ',', Enumerable.Range( 1, HerdrClient.MaximumWorkspaces + 1 )
                .Select( number => "{\"workspace_id\":\"w" + number + "\"}" ) );
            foreach ( var workspaceJson in new[]
            {
                """{"result":{"type":"workspace_list","workspaces":[null]}}""",
                """{"result":{"type":"workspace_list","workspaces":[{"workspace_id":"w1"},{"workspace_id":"w1"}]}}""",
                "{\"result\":{\"type\":\"workspace_list\",\"workspaces\":[" + excessive + "]}}"
            } )
            {
                HerdrFake fake = new( )
                {
                    Respond = args => args[2] == "workspace" ? HerdrResult( workspaceJson ) : ProjectResponse( args, directory, false )
                };
                True( !(await new HerdrClient( fake.Runner, static ( ) => true ).OpenProjectAsync( directory, default )).Succeeded );
                True( fake.Calls.All( static call => call.Args[3] == "list" ) );
            }

            foreach ( var paneJson in new[]
            {
                """{"result":{"type":"pane_list","panes":[{}]}}""",
                """{"result":{"type":"pane_list","panes":[{"pane_id":"p1","workspace_id":"gone"}]}}""",
                "{\"result\":{\"type\":\"pane_list\",\"panes\":[" + string.Join( ',', Enumerable.Repeat(
                    "{\"pane_id\":\"p1\",\"workspace_id\":\"w1\"}", 1025 ) ) + "]}}"
            } )
            {
                HerdrFake fake = new( )
                {
                    Respond = args => args[2] == "pane" ? HerdrResult( paneJson ) : ProjectResponse( args, directory, true )
                };
                True( !(await new HerdrClient( fake.Runner, static ( ) => true ).OpenProjectAsync( directory, default )).Succeeded );
                True( fake.Calls.All( static call => call.Args[3] == "list" ) );
            }

            foreach ( var incorrect in new[] { "directory", "label", "workspace" } )
            {
                HerdrFake fake = new( )
                {
                    Respond = args =>
                    {
                        var response = ProjectResponse( args, incorrect == "directory" ? directory + "-other" : directory, false );
                        if ( args[3] != "create" )
                        {
                            return response;
                        }

                        var json = response.StandardOutput;
                        json = incorrect switch
                        {
                            "label" => json.Replace( ProjectJsonString( Path.GetFileName( directory ) ), "\"wrong\"", StringComparison.Ordinal ),
                            "workspace" => json.Replace( "\"pane_id\":\"w1:t1:p1\",\"workspace_id\":\"w1\"",
                                "\"pane_id\":\"w2:p1\",\"workspace_id\":\"w2\"", StringComparison.Ordinal ),
                            _ => json
                        };
                        return HerdrResult( json );
                    }
                };
                True( !(await new HerdrClient( fake.Runner, static ( ) => true ).OpenProjectAsync( directory, default )).Succeeded );
                Equal( 1, fake.Calls.Count( static call => call.Args[3] == "create" ) );
                True( fake.Calls.All( static call => call.Args[3] != "focus" ) );
            }
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }

    private static async Task HerdrProjectRejectsOverlappingOpensAsync( )
    {
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-herdr-overlap-" ).FullName;
        try
        {
            TaskCompletionSource<ProcessResult> pending = new( TaskCreationOptions.RunContinuationsAsynchronously );
            var hold = true;
            var calls = 0;
            BoundedProcessRunner runner = ( _, args, _, _, token ) =>
            {
                Interlocked.Increment( ref calls );
                return hold ? new ValueTask<ProcessResult>( pending.Task.WaitAsync( token ) )
                    : ValueTask.FromResult( ProjectResponse( args, directory, true ) );
            };
            HerdrClient client = new( runner, static ( ) => true );
            using CancellationTokenSource cancellation = new( );
            var first = client.OpenProjectAsync( directory, cancellation.Token ).AsTask( );
            True( !(await client.OpenProjectAsync( directory, default )).Succeeded );
            Equal( 2, calls );
            cancellation.Cancel( );
            await ThrowsAsync<OperationCanceledException>( async ( ) => await first );
            hold = false;
            True( (await client.OpenProjectAsync( directory, default )).Succeeded );
            Equal( 5, calls );
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }

    private static async Task HerdrProjectReportsWindowFailureAsync( )
    {
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-herdr-window-" ).FullName;
        try
        {
            HerdrFake fake = new( )
            {
                Respond = args => ProjectResponse( args, directory, true )
            };
            HerdrClient client = new( fake.Runner, static ( ) => true );
            var clears = 0;
            HerdrWindowActivator activator = new( client,
                ( _, title, _ ) =>
                {
                    clears += title is null ? 1 : 0;
                    return ValueTask.FromResult( HerdrWindowTitleResult.Success( ) );
                },
                ( _, args, _, _ ) => ValueTask.FromResult( HerdrResult( args[0] == "-j"
                    ? """[{"address":"0x1a2b","title":"commanddeck-herdr-test"}]"""
                    : "warning: hl.focus: window not found" ) ),
                static ( ) => true, static ( ) => "commanddeck-herdr-test" );
            LinuxActionExecutor executor = new( herdr: client, herdrWindowActivator: activator );
            ProjectProvider provider = new( [new ProjectOptions { Title = "Project", Path = directory }] );
            var result = (await provider.SearchAsync( QueryParser.Parse( "proj:" ), new SearchRequest( ), default )).Single( );
            var outcome = await executor.ExecuteAsync( result, result.Actions.Single( static action => action.Id == "open-herdr" ), "", default );
            True( !outcome.Succeeded );
            Equal( "Switched Herdr space, but could not focus its window.", outcome.Message );
            Equal( 1, clears );
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }
}
