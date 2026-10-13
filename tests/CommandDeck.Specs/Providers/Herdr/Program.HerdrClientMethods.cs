namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task HerdrClientRunsNothingWithoutExecutableAsync( )
    {
        HerdrFake fake = new( );
        HerdrClient client = new( fake.Runner, available: static ( ) => false );

        Equal( HerdrAvailability.NotInstalled, (await client.GetStatusAsync( CancellationToken.None )).Availability );
        True( (await client.ListAsync( CancellationToken.None )) is null );
        True( !(await client.FocusAsync( HerdrTargetKinds.Tab, "w1:t1", CancellationToken.None )).Succeeded );
        Equal( 0, fake.Calls.Count );
    }

    private static async Task HerdrClientReadsStatusAsync( )
    {
        HerdrFake fake = new( );
        HerdrClient client = new( fake.Runner, available: static ( ) => true );

        var status = await client.GetStatusAsync( CancellationToken.None );

        Equal( HerdrAvailability.Available, status.Availability );
        Equal( "0.9.0", status.Version );
        Equal( 22L, status.Protocol );
        Equal( "/tmp/herdr.sock", status.SocketPath );
        Equal( 1, fake.Calls.Count );
        Equal( HerdrClient.Executable, fake.Calls[0].File );
        Equal( "--session default status server --json", string.Join( ' ', fake.Calls[0].Args ) );
        Equal( HerdrClient.StatusTimeout, fake.Calls[0].Timeout );
        Equal( HerdrClient.StatusStandardOutputBytes, fake.Calls[0].Limits.MaximumStandardOutputBytes );
        Equal( HerdrClient.StatusStandardErrorBytes, fake.Calls[0].Limits.MaximumStandardErrorBytes );
    }

    private static async Task HerdrClientAdmitsOnlyCompatibleServersAsync( )
    {
        // Stopped servers still exit 0; availability is read from JSON.
        Equal( HerdrAvailability.Stopped, (await StatusFor( HerdrStatusStoppedJson )).Availability );

        // endpoint_compatible cannot override the CLI admission field.
        var incompatible = await StatusFor(
            """{"running":true,"version":"0.10.0","protocol":23,"compatible":false,"endpoint_compatible":true}""" );
        Equal( HerdrAvailability.Incompatible, incompatible.Availability );
        True( incompatible.Detail.Contains( "0.10.0", StringComparison.Ordinal ) );

        // A future version is admitted by Herdr's own compatibility result.
        var future = await StatusFor(
            """{"running":true,"version":"1.4.0","protocol":40,"compatible":true,"endpoint_compatible":false,"future_field":{"x":1}}""" );
        Equal( HerdrAvailability.Available, future.Availability );
        Equal( 40L, future.Protocol );

        Equal( HerdrAvailability.Failed, (await StatusFor( """{"version":"0.9.0"}""" )).Availability );
        Equal( HerdrAvailability.Failed, (await StatusFor( "not json" )).Availability );
        Equal( HerdrAvailability.Failed, (await StatusFor( "[]" )).Availability );
    }

    private static async Task HerdrClientReportsTypedFailuresAsync( )
    {
        HerdrFake exit = new( )
        {
            Respond = _ => HerdrResult( "", exitCode: 1, error: "usage" )
        };
        Equal( HerdrAvailability.Failed, (await new HerdrClient( exit.Runner, static ( ) => true ).GetStatusAsync( CancellationToken.None )).Availability );

        HerdrFake timeout = new( )
        {
            Respond = _ => new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", TimedOut = true }
        };
        Equal( HerdrAvailability.Failed, (await new HerdrClient( timeout.Runner, static ( ) => true ).GetStatusAsync( CancellationToken.None )).Availability );
        True( (await new HerdrClient( timeout.Runner, static ( ) => true ).ListAsync( CancellationToken.None )) is null );

        HerdrFake oversized = new( )
        {
            Respond = _ => new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", OutputLimitExceeded = true }
        };
        Equal( HerdrAvailability.Failed, (await new HerdrClient( oversized.Runner, static ( ) => true ).GetStatusAsync( CancellationToken.None )).Availability );

        HerdrFake memory = new( )
        {
            Respond = _ => new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", MemoryLimitExceeded = true }
        };
        True( (await new HerdrClient( memory.Runner, static ( ) => true ).ListAsync( CancellationToken.None )) is null );

        HerdrFake throwing = new( )
        {
            Respond = _ => throw new IOException( "spawn failed" )
        };
        Equal( HerdrAvailability.Failed, (await new HerdrClient( throwing.Runner, static ( ) => true ).GetStatusAsync( CancellationToken.None )).Availability );
        True( (await new HerdrClient( throwing.Runner, static ( ) => true ).ListAsync( CancellationToken.None )) is null );

        // Wrong envelope type on one half discards the whole generation.
        HerdrFake wrongType = new( )
        {
            Respond = args => args[2] == "tab"
                ? HerdrResult( """{"id":"cli:tab:list","result":{"type":"workspace_list","tabs":[]}}""" )
                : HerdrResult( HerdrWorkspaceListJson )
        };
        True( (await new HerdrClient( wrongType.Runner, static ( ) => true ).ListAsync( CancellationToken.None )) is null );

        using CancellationTokenSource cancelled = new( );
        cancelled.Cancel( );
        HerdrFake honoring = new( )
        {
            Respond = _ => throw new OperationCanceledException( cancelled.Token )
        };
        await ThrowsAsync<OperationCanceledException>( async ( ) =>
            await new HerdrClient( honoring.Runner, static ( ) => true ).ListAsync( cancelled.Token ) );
    }

    private static async Task HerdrClientListsWithBoundsAsync( )
    {
        HerdrFake fake = new( );
        Dictionary<string, string?> parent = new( StringComparer.Ordinal )
        {
            ["PATH"] = "/spec/bin",
            ["HOME"] = "/home/spec",
            ["XDG_RUNTIME_DIR"] = "/run/spec",
            ["HERDR_SESSION"] = "hostile",
            ["HERDR_SOCKET_PATH"] = "/tmp/hostile.sock",
            ["OPENAI_API_KEY"] = "secret"
        };
        HerdrClient client = new( fake.Runner, static ( ) => true, name => parent.GetValueOrDefault( name ) );

        var inventory = await client.ListAsync( CancellationToken.None );

        True( inventory is not null );
        Equal( 3, inventory!.Workspaces.Count );
        Equal( 5, inventory.Tabs.Count );
        Equal( 2, fake.Calls.Count );
        var argv = fake.Calls.Select( call => string.Join( ' ', call.Args ) ).Order( StringComparer.Ordinal ).ToArray( );
        Equal( "--session default tab list", argv[0] );
        Equal( "--session default workspace list", argv[1] );
        True( fake.Calls.All( call => call.File == HerdrClient.Executable && call.Timeout == HerdrClient.ListTimeout ) );
        True( fake.Calls.All( call => !call.Args.Contains( "snapshot" ) && !call.Args.Contains( "api" ) ) );

        var tabs = fake.Calls.Single( call => call.Args[2] == "tab" );
        Equal( HerdrClient.TabListStandardOutputBytes, tabs.Limits.MaximumStandardOutputBytes );
        Equal( HerdrClient.TabListStandardErrorBytes, tabs.Limits.MaximumStandardErrorBytes );
        Equal( HerdrClient.MaximumMemoryBytes, tabs.Limits.MaximumMemoryBytes );
        var workspaces = fake.Calls.Single( call => call.Args[2] == "workspace" );
        Equal( HerdrClient.WorkspaceListStandardOutputBytes, workspaces.Limits.MaximumStandardOutputBytes );

        foreach ( var call in fake.Calls )
        {
            True( call.Limits.ClearEnvironment );
            var environment = call.Limits.EnvironmentVariables!;
            Equal( "PATH,HOME,XDG_RUNTIME_DIR,LANG,LC_ALL", string.Join( ',', environment.Keys.Order( new EnvironmentKeyOrder( ) ) ) );
            Equal( "/spec/bin", environment["PATH"] );
            Equal( "/home/spec", environment["HOME"] );
            Equal( "C.UTF-8", environment["LC_ALL"] );
            True( !environment.Keys.Any( static key => key.StartsWith( "HERDR_", StringComparison.Ordinal ) ) );
            True( !environment.ContainsKey( "OPENAI_API_KEY" ) );
        }

        // Sanitized records: the control-bearing id and the non-object were dropped;
        // the orphan tab keeps an empty workspace id; the empty tab id was dropped.
        True( inventory.Workspaces.All( workspace => HerdrClient.IsValidIdentifier( workspace.Id ) ) );
        Equal( "gone", inventory.Tabs.Single( tab => tab.Id == "gone:t1" ).WorkspaceId );
        Equal( "working", inventory.Workspaces[0].AgentStatus );
        Equal( 2L, inventory.Workspaces[0].TabCount );
    }

    private static async Task HerdrClientFocusesAndVerifiesAsync( )
    {
        const string hostileId = "w1:t1; rm -rf ~ $(x) `y` \"z\"";
        HerdrFake fake = new( )
        {
            Respond = args => HerdrResult(
                "{\"id\":\"cli:tab:focus\",\"result\":{\"tab\":{\"tab_id\":" +
                System.Text.Json.JsonSerializer.Serialize( args[4] ) +
                ",\"workspace_id\":\"w1\",\"focused\":true,\"label\":\"x\",\"number\":1,\"pane_count\":1},\"type\":\"tab_info\"}}" )
        };
        HerdrClient client = new( fake.Runner, static ( ) => true );

        var outcome = await client.FocusAsync( HerdrTargetKinds.Tab, hostileId, CancellationToken.None );

        True( outcome.Succeeded, outcome.Message );
        Equal( 1, fake.Calls.Count );
        Equal( 5, fake.Calls[0].Args.Count );
        Equal( "--session default tab focus", string.Join( ' ', fake.Calls[0].Args.Take( 4 ) ) );
        Equal( hostileId, fake.Calls[0].Args[4] );
        Equal( HerdrClient.FocusTimeout, fake.Calls[0].Timeout );

        HerdrFake workspaceFake = new( )
        {
            Respond = _ => HerdrResult( """{"id":"cli:workspace:focus","result":{"type":"workspace_info","workspace":{"workspace_id":"w2","focused":true,"number":2}}}""" )
        };
        True( (await new HerdrClient( workspaceFake.Runner, static ( ) => true ).FocusAsync( HerdrTargetKinds.Workspace, "w2", CancellationToken.None )).Succeeded );
        Equal( "--session default workspace focus w2", string.Join( ' ', workspaceFake.Calls[0].Args ) );

        // Exit 0 without a matching focused acknowledgement is a failure.
        HerdrFake mismatched = new( )
        {
            Respond = _ => HerdrResult( """{"id":"cli:workspace:focus","result":{"type":"workspace_info","workspace":{"workspace_id":"w9","focused":true}}}""" )
        };
        True( !(await new HerdrClient( mismatched.Runner, static ( ) => true ).FocusAsync( HerdrTargetKinds.Workspace, "w2", CancellationToken.None )).Succeeded );
        HerdrFake unfocused = new( )
        {
            Respond = _ => HerdrResult( """{"id":"cli:workspace:focus","result":{"type":"workspace_info","workspace":{"workspace_id":"w2","focused":false}}}""" )
        };
        True( !(await new HerdrClient( unfocused.Runner, static ( ) => true ).FocusAsync( HerdrTargetKinds.Workspace, "w2", CancellationToken.None )).Succeeded );
        HerdrFake wrongType = new( )
        {
            Respond = _ => HerdrResult( """{"id":"cli:workspace:focus","result":{"type":"tab_info","workspace":{"workspace_id":"w2","focused":true}}}""" )
        };
        True( !(await new HerdrClient( wrongType.Runner, static ( ) => true ).FocusAsync( HerdrTargetKinds.Workspace, "w2", CancellationToken.None )).Succeeded );

        True( !(await client.FocusAsync( "pane", "p1", CancellationToken.None )).Succeeded );
        True( !(await client.FocusAsync( HerdrTargetKinds.Tab, "bad\nid", CancellationToken.None )).Succeeded );
        True( !(await client.FocusAsync( HerdrTargetKinds.Tab, new string( 'x', HerdrClient.MaximumIdentifierLength + 1 ), CancellationToken.None )).Succeeded );
        Equal( 1, fake.Calls.Count );
    }

    private static async Task HerdrClientMapsFocusErrorsAsync( )
    {
        Equal(
            "That Herdr tab no longer exists. Search again.",
            (await FocusFor( HerdrResult( "", 1, """{"error":{"code":"tab_not_found","message":"tab w1:t9 not found"},"id":"cli:tab:focus"}""" ) )).Message );
        Equal(
            "That Herdr space no longer exists. Search again.",
            (await FocusFor( HerdrResult( "", 1, """{"id":"cli:workspace:focus","error":{"code":"workspace_not_found","message":"x"}}""" ), HerdrTargetKinds.Workspace )).Message );
        Equal(
            "Herdr is no longer running.",
            (await FocusFor( HerdrResult( "", 1, """{"id":"cli:tab:focus","error":{"code":"server_not_running","message":"no herdr server"}}""" ) )).Message );
        Equal(
            "The Herdr client and server are incompatible.",
            (await FocusFor( HerdrResult( "", 1, """{"error":{"code":"protocol_mismatch","message":"x"}}""" ) )).Message );
        Equal( "Herdr could not switch to this tab.", (await FocusFor( HerdrResult( "", 2, "usage: herdr tab focus <id>" ) )).Message );

        HerdrFake timeout = new( )
        {
            Respond = _ => new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", TimedOut = true }
        };
        var late = await new HerdrClient( timeout.Runner, static ( ) => true ).FocusAsync( HerdrTargetKinds.Tab, "w1:t1", CancellationToken.None );
        True( !late.Succeeded );
        True( late.Message.Contains( "may already have happened", StringComparison.Ordinal ) );
        Equal( 1, timeout.Calls.Count );
    }
}
