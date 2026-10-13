namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task HerdrProjectValidatesPaneCountsAsync( )
    {
        var directory = Directory.CreateTempSubdirectory( "commanddeck-herdr-snapshot-" ).FullName;
        try
        {
            const string pane = """{"pane_id":"p1","workspace_id":"w1","cwd":"/some/other/project"}""";
            foreach ( var (count, panes) in new[]
            {
                ("1", ""), ("2", pane), ("0", pane), ("2", pane + "," + pane),
                ("null", ""), ("-1", ""), ("\"1\"", "")
            } )
            {
                HerdrFake fake = new( )
                {
                    Respond = args => args[3] != "list" ? ProjectResponse( args, directory, false ) :
                        HerdrResult( args[2] == "workspace"
                            ? "{\"result\":{\"type\":\"workspace_list\",\"workspaces\":[{\"workspace_id\":\"w1\",\"pane_count\":" + count + "}]}}"
                            : "{\"result\":{\"type\":\"pane_list\",\"panes\":[" + panes + "]}}" )
                };
                HerdrClient client = new( fake.Runner, static ( ) => true );
                var outcome = await client.OpenProjectAsync( directory, default );
                True( !outcome.Succeeded, "an inconsistent inventory cannot create or focus a space" );
                Equal( 2, fake.Calls.Count );
                True( fake.Calls.All( static call => call.Args[3] == "list" ) );
            }
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }

    private static async Task HerdrProjectGateCoversActivationAsync( )
    {
        var directory = Directory.CreateTempSubdirectory( "commanddeck-herdr-activation-gate-" ).FullName;
        try
        {
            foreach ( var cancel in new[] { false, true } )
            {
                TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
                TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );
                HerdrFake fake = new( )
                {
                    Respond = args => ProjectResponse( args, directory, true )
                };
                HerdrClient client = new( fake.Runner, static ( ) => true );
                var clears = 0;
                HerdrWindowActivator activator = new( client,
                    async ( _, title, token ) =>
                    {
                        if ( title is not null )
                        {
                            entered.TrySetResult( );
                            await release.Task.WaitAsync( token );
                        }
                        else
                        {
                            clears++;
                        }

                        return HerdrWindowTitleResult.Success( );
                    },
                    ( _, args, _, _ ) => ValueTask.FromResult( HerdrResult( args[0] == "-j"
                        ? """[{"address":"0x1a2b","title":"commanddeck-herdr-test"}]""" : "ok" ) ),
                    static ( ) => true, static ( ) => "commanddeck-herdr-test" );
                LinuxActionExecutor firstExecutor = new( herdr: client, herdrWindowActivator: activator );
                LinuxActionExecutor secondExecutor = new( herdr: client );
                ProjectProvider provider = new( [new ProjectOptions { Title = "Project", Path = directory }] );
                var result = (await provider.SearchAsync( QueryParser.Parse( "proj:" ), new SearchRequest( ), default )).Single( );
                var action = result.Actions.Single( static value => value.Id == "open-herdr" );
                using CancellationTokenSource cancellation = new( );
                var first = firstExecutor.ExecuteAsync( result, action, "", cancellation.Token ).AsTask( );
                try
                {
                    await entered.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
                    var calls = fake.Calls.Count;
                    var second = await secondExecutor.ExecuteAsync( result, action, "", default );
                    True( !second.Succeeded, "another executor must not focus a space while terminal activation is pending" );
                    Equal( calls, fake.Calls.Count );
                }
                finally
                {
                    if ( cancel )
                    {
                        cancellation.Cancel( );
                    }

                    release.TrySetResult( );
                    if ( cancel )
                    {
                        await ThrowsAsync<OperationCanceledException>( async ( ) => await first );
                    }
                    else
                    {
                        True( (await first).Succeeded );
                    }
                }

                Equal( 1, clears );
                True( (await secondExecutor.ExecuteAsync( result, action, "", default )).Succeeded,
                    "the gate is released after activation or cancellation cleanup" );
            }
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }
}
