namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task HerdrMixedFocusActionsShareGateAsync( )
    {
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-herdr-mixed-focus-" ).FullName;
        try
        {
            ProjectProvider provider = new( [new ProjectOptions { Title = "Project", Path = directory }] );
            var project = (await provider.SearchAsync( QueryParser.Parse( "proj:" ), new SearchRequest( ), default )).Single( );
            var rows = await SearchHerdrAsync( "herdr:" );
            var workspace = rows.Single( static row => row.Id == "herdr:workspace:w2" );
            var tab = rows.Single( static row => row.Id == "herdr:tab:w1:t1" );
            foreach ( var (first, second) in new[]
            {
                (project, workspace), (project, tab), (workspace, project), (tab, project),
                (workspace, tab), (tab, workspace)
            } )
            {
                foreach ( var phase in new[] { "mark", "cleanup", "cancel", "failure" } )
                {
                    await AssertHerdrFocusGateAsync( directory, first, second, phase );
                }
            }
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }

    private static async Task AssertHerdrFocusGateAsync(
        string directory, SearchResult firstResult, SearchResult secondResult, string phase )
    {
        TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );
        HerdrFake fake = new( )
        {
            Respond = args => args.Count > 3 && args[2] == "tab" && args[3] == "focus"
                ? HerdrResult( $$$$"""{"result":{"type":"tab_info","tab":{"tab_id":{{{{ProjectJsonString( args[4] )}}}},"focused":true}}}""" )
                : ProjectResponse( args, directory, true )
        };
        HerdrClient client = new( fake.Runner, static ( ) => true );
        var clears = 0;
        HerdrWindowActivator activator = new( client,
            async ( _, title, token ) =>
            {
                if ( (title is null) == (phase == "cleanup") )
                {
                    entered.TrySetResult( );
                    await release.Task.WaitAsync( token );
                }

                if ( title is null )
                {
                    clears++;
                }

                return HerdrWindowTitleResult.Success( );
            },
            ( _, args, _, _ ) => ValueTask.FromResult( HerdrResult( args[0] == "-j"
                ? """[{"address":"0x1a2b","title":"commanddeck-herdr-test"}]"""
                : phase == "failure" ? "warning: hl.focus: window not found" : "ok" ) ),
            static ( ) => true, static ( ) => "commanddeck-herdr-test" );
        LinuxActionExecutor firstExecutor = new( herdr: client, herdrWindowActivator: activator );
        LinuxActionExecutor secondExecutor = new( herdr: client );
        var firstAction = firstResult.SourceId == SourceIds.Projects
            ? firstResult.Actions.Single( static action => action.Id == "open-herdr" ) : firstResult.Actions[0];
        var secondAction = secondResult.SourceId == SourceIds.Projects
            ? secondResult.Actions.Single( static action => action.Id == "open-herdr" ) : secondResult.Actions[0];
        using CancellationTokenSource cancellation = new( );
        var first = firstExecutor.ExecuteAsync( firstResult, firstAction, "", cancellation.Token ).AsTask( );
        try
        {
            await entered.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
            var calls = fake.Calls.Count;
            var second = await secondExecutor.ExecuteAsync( secondResult, secondAction, "", default );
            True( !second.Succeeded, "mixed focus actions cannot overlap during " + phase );
            Equal( calls, fake.Calls.Count );
        }
        finally
        {
            if ( phase == "cancel" )
            {
                cancellation.Cancel( );
            }

            release.TrySetResult( );
            if ( phase == "cancel" )
            {
                await ThrowsAsync<OperationCanceledException>( async ( ) => await first );
            }
            else
            {
                Equal( phase != "failure", (await first).Succeeded );
            }
        }

        Equal( 1, clears );
        True( (await secondExecutor.ExecuteAsync( secondResult, secondAction, "", default )).Succeeded,
            "the shared gate releases after " + phase );
    }
}
