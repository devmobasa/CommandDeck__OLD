namespace CommandDeck.Specs;

public static partial class Program
{
    private const string WindowClients = """
        [{"address":"0xabc","monitor":1,"floating":false,"fullscreen":0,"size":[1000,700],"class":"kitty","title":"shell"},
         {"address":"0xdef","monitor":0,"floating":true,"fullscreen":2,"size":[640,480],"class":"mpv","title":"video"},
         {"address":"0x123","monitor":2,"floating":false,"fullscreen":0,"size":[800,600],"class":"foot","title":""}]
        """;

    private const string WindowMonitors = """
        [{"id":0,"name":"DP-1","x":0,"y":0,"width":1920,"height":1080,"scale":1.0,"transform":0,"reserved":[0,30,0,0],"disabled":false},
         {"id":1,"name":"HDMI-A-1","x":1920,"y":0,"width":2560,"height":1440,"scale":1.25,"transform":0,"reserved":[0,0,0,0],"disabled":false},
         {"id":2,"name":"DP-2","x":3968,"y":0,"width":1920,"height":1080,"scale":1.0,"transform":0,"reserved":[0,0,0,0],"disabled":false},
         {"id":3,"name":"DP-3","x":5888,"y":0,"width":1920,"height":1080,"scale":1.0,"transform":0,"reserved":[0,0,0,0],"disabled":true}]
        """;

    // A scripted hyprctl: inspection answers from fixtures, and dispatches are
    // recorded and acknowledged unless a Lua prefix is set to warn.
    private sealed class HyprctlFake
    {
        public string ActiveWindow { get; set; } = """{"address":"0xabc","monitor":1,"floating":false,"fullscreen":0,"size":[1000,700],"class":"kitty","title":"shell"}""";
        public string Monitors { get; set; } = WindowMonitors;
        public string? WarnOn { get; set; }
        public List<string> Requests { get; } = [];
        public List<string> Dispatches { get; } = [];

        public ValueTask<ProcessResult> RunAsync( string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken token )
        {
            token.ThrowIfCancellationRequested( );
            Equal( "hyprctl", file );
            if ( args[0] == "dispatch" )
            {
                Equal( 2, args.Count );
                Dispatches.Add( args[1] );
                var warn = WarnOn is not null && args[1].StartsWith( WarnOn, StringComparison.Ordinal );
                return ValueTask.FromResult( new ProcessResult { ExitCode = 0, StandardOutput = warn ? "warning: Window is fullscreen" : "ok", StandardError = "" } );
            }

            Equal( "-j", args[0] );
            Requests.Add( args[1] );
            var output = args[1] switch
            {
                "activewindow" => ActiveWindow,
                "clients" => WindowClients,
                "monitors" => Monitors,
                _ => throw new InvalidOperationException( args[1] )
            };
            return ValueTask.FromResult( new ProcessResult { ExitCode = 0, StandardOutput = output, StandardError = "" } );
        }
    }

    private static void WindowPlacementGeometryMatchesGoldenTable( )
    {
        MonitorLayout plain = new( 0, 0, 1920, 1080, 1.0, 0, 0, 30, 0, 0 );
        MonitorLayout scaled = new( 2304, 0, 3840, 2160, 1.6666666, 0, 0, 30, 0, 0 );
        MonitorLayout rounded = scaled with { Scale = 1.66666667 };
        MonitorLayout rotated = new( -1152, 0, 2560, 1440, 1.25, 1, 40, 0, 0, 0 );
        MonitorLayout odd = new( 0, 0, 1366, 768, 1.0, 0, 0, 0, 0, 26 );
        MonitorLayout flipped = new( 0, 0, 1920, 1080, 2.0, 5, 0, 0, 0, 0 );
        Equal( new WindowRect( 0, 30, 1920, 1050 ), HyprlandWindowGeometry.WorkArea( plain ) );
        Equal( new WindowRect( 2304, 30, 2304, 1266 ), HyprlandWindowGeometry.WorkArea( scaled ) );
        Equal( new WindowRect( 2304, 30, 2304, 1266 ), HyprlandWindowGeometry.WorkArea( rounded ) );
        Equal( new WindowRect( -1112, 0, 1112, 2048 ), HyprlandWindowGeometry.WorkArea( rotated ) );
        Equal( new WindowRect( 0, 0, 1366, 742 ), HyprlandWindowGeometry.WorkArea( odd ) );
        Equal( new WindowRect( 0, 0, 540, 960 ), HyprlandWindowGeometry.WorkArea( flipped ) );

        foreach ( (var monitor, var placement, var expected) in new (MonitorLayout, string, WindowRect)[]
        {
            (plain, WindowPlacements.LeftHalf, new( 0, 30, 960, 1050 )),
            (plain, WindowPlacements.RightHalf, new( 960, 30, 960, 1050 )),
            (plain, WindowPlacements.TopHalf, new( 0, 30, 1920, 525 )),
            (plain, WindowPlacements.BottomHalf, new( 0, 555, 1920, 525 )),
            (plain, WindowPlacements.FirstThird, new( 0, 30, 640, 1050 )),
            (plain, WindowPlacements.CenterThird, new( 640, 30, 640, 1050 )),
            (plain, WindowPlacements.LastThird, new( 1280, 30, 640, 1050 )),
            (plain, WindowPlacements.FirstTwoThirds, new( 0, 30, 1280, 1050 )),
            (plain, WindowPlacements.LastTwoThirds, new( 640, 30, 1280, 1050 )),
            (plain, WindowPlacements.Maximize, new( 0, 30, 1920, 1050 )),
            (plain, WindowPlacements.AlmostMaximize, new( 96, 83, 1728, 945 )),
            (scaled, WindowPlacements.LeftHalf, new( 2304, 30, 1152, 1266 )),
            (scaled, WindowPlacements.RightHalf, new( 3456, 30, 1152, 1266 )),
            (scaled, WindowPlacements.CenterThird, new( 3072, 30, 768, 1266 )),
            (scaled, WindowPlacements.BottomHalf, new( 2304, 663, 2304, 633 )),
            (rotated, WindowPlacements.LeftHalf, new( -1112, 0, 556, 2048 )),
            (rotated, WindowPlacements.TopHalf, new( -1112, 0, 1112, 1024 )),
            (rotated, WindowPlacements.LastThird, new( -371, 0, 371, 2048 )),
            (odd, WindowPlacements.FirstThird, new( 0, 0, 455, 742 )),
            (odd, WindowPlacements.CenterThird, new( 455, 0, 456, 742 )),
            (odd, WindowPlacements.LastThird, new( 911, 0, 455, 742 )),
            (odd, WindowPlacements.BottomHalf, new( 0, 371, 1366, 371 )),
            (flipped, WindowPlacements.LeftHalf, new( 0, 0, 270, 960 ))
        } )
        {
            Equal( expected, HyprlandWindowGeometry.Place( placement, HyprlandWindowGeometry.WorkArea( monitor ), 800, 600 ) );
        }

        // Center keeps the window's size and clamps it to the work area.
        var area = HyprlandWindowGeometry.WorkArea( plain );
        Equal( new WindowRect( 560, 255, 800, 600 ), HyprlandWindowGeometry.Place( WindowPlacements.Center, area, 800, 600 ) );
        Equal( area, HyprlandWindowGeometry.Place( WindowPlacements.Center, area, 2500, 1200 ) );

        True( HyprlandWindowGeometry.IsPlacement( WindowPlacements.Center ) );
        True( !HyprlandWindowGeometry.IsPlacement( "diagonal" ) );
        Throws<InvalidOperationException>( ( ) => HyprlandWindowGeometry.Place( "diagonal", area, 1, 1 ) );
        Throws<InvalidOperationException>( ( ) => HyprlandWindowGeometry.WorkArea( plain with { Scale = 0 } ) );
        Throws<InvalidOperationException>( ( ) => HyprlandWindowGeometry.WorkArea( plain with { ReservedLeft = 1920 } ) );
    }

    private static async Task WindowCommandsBindTheWindowFocusedBeforeTheLauncherAsync( )
    {
        HyprctlFake hyprctl = new( );
        var hyprctlInstalled = true;
        DesktopControlsProvider provider = new( tool => tool == "hyprctl" && hyprctlInstalled, hyprctl.RunAsync );
        SearchCoordinator search = new( [provider], new SearchOptions( ) );
        async Task<IReadOnlyList<SearchResult>> FindAsync( string query ) =>
            await search.SearchAsync( new SearchRequest { Query = query, Limit = 50 }, default );

        var left = (await FindAsync( "left half" ))[0];
        Equal( "desktop:window-left-half", left.Id );
        Equal( "Window · kitty — shell", left.Subtitle );
        var action = left.Actions.Single( );
        Equal( ActionKinds.HyprlandDispatch, action.Kind );
        Equal( HyprlandActionOperations.PlaceWindow, action.Arguments[HyprlandActionOperations.OperationKey] );
        Equal( "address:0xabc", action.Arguments[HyprlandActionOperations.ArgumentKey] );
        Equal( WindowPlacements.LeftHalf, action.Arguments[HyprlandActionOperations.ParameterKey] );
        Equal( "desktop:window-workspace-3", (await FindAsync( "workspace 3" ))[0].Id );
        Equal( "desktop:window-tile", (await FindAsync( "tile window" ))[0].Id );
        True( (await FindAsync( "next monitor" )).Any( static result => result.Id == "desktop:window-next-monitor" ) );

        // A single monitor hides the monitor moves; no matching window command
        // means no compositor call at all.
        hyprctl.Monitors = "[" + WindowMonitors.Split( '\n' )[0].TrimStart( '[' ).TrimEnd( ',' ) + "]";
        True( !(await FindAsync( "next monitor" )).Any( static result => result.Id == "desktop:window-next-monitor" ) );
        hyprctl.Requests.Clear( );
        await FindAsync( "bluetooth" );
        Equal( 0, hyprctl.Requests.Count );

        // No focused window, no hyprctl, or a failed reply shows nothing, not an error row.
        hyprctl.ActiveWindow = "{}";
        Equal( 0, (await FindAsync( "left half" )).Count );
        hyprctl.ActiveWindow = "not json";
        Equal( 0, (await FindAsync( "maximize" )).Count );
        hyprctlInstalled = false;
        hyprctl.Requests.Clear( );
        Equal( 0, (await FindAsync( "left half" )).Count );
        Equal( 0, hyprctl.Requests.Count );
    }

    private static async Task WindowCommandsDispatchToTheBoundAddressAsync( )
    {
        HyprctlFake hyprctl = new( );
        DesktopControlsProvider provider = new( static tool => tool == "hyprctl", hyprctl.RunAsync );
        SearchCoordinator search = new( [provider], new SearchOptions( ) );
        LinuxActionExecutor executor = new( hyprlandRunner: hyprctl.RunAsync );
        async Task<SearchResult> RowAsync( string query, string id ) =>
            (await search.SearchAsync( new SearchRequest { Query = query, Limit = 50 }, default )).Single( result => result.Id == id );
        async Task<(ActionExecutionResult Outcome, string[] Dispatches)> RunAsync( SearchResult row )
        {
            hyprctl.Dispatches.Clear( );
            var outcome = await executor.ExecuteAsync( row, row.Actions.Single( ), "", default );
            return (outcome, [.. hyprctl.Dispatches]);
        }

        // The row binds 0xabc at search time; focus moving afterwards does not retarget it.
        var left = await RowAsync( "left half", "desktop:window-left-half" );
        hyprctl.ActiveWindow = """{"address":"0xdef","monitor":0,"floating":true,"fullscreen":2,"size":[640,480],"class":"mpv","title":"video"}""";
        const string Target = "window = \"address:0xabc\"";
        var placed = await RunAsync( left );
        True( placed.Outcome.Succeeded, placed.Outcome.Message );
        Equal( string.Join( '\n', $$"""hl.dsp.window.float({ action = "enable", {{Target}} })""",
            $$"""hl.dsp.window.resize({ x = 1024, y = 1152, {{Target}} })""",
            $$"""hl.dsp.window.move({ x = 1920, y = 0, {{Target}} })""" ), string.Join( '\n', placed.Dispatches ) );

        // A floating fullscreen window leaves fullscreen and is not floated again.
        var maximize = await RowAsync( "maximize window", "desktop:window-maximize" );
        const string Video = "window = \"address:0xdef\"";
        Equal( string.Join( '\n', $$"""hl.dsp.window.fullscreen({ mode = "fullscreen", action = "unset", {{Video}} })""",
            $$"""hl.dsp.window.resize({ x = 1920, y = 1050, {{Video}} })""",
            $$"""hl.dsp.window.move({ x = 0, y = 30, {{Video}} })""" ), string.Join( '\n', (await RunAsync( maximize )).Dispatches ) );

        hyprctl.ActiveWindow = """{"address":"0xabc","monitor":1,"floating":false,"fullscreen":0,"size":[1000,700],"class":"kitty","title":"shell"}""";
        foreach ( (var query, var id, var expected) in new[]
        {
            ("toggle floating", "desktop:window-toggle-floating", $$"""hl.dsp.window.float({ action = "toggle", {{Target}} })"""),
            ("tile window", "desktop:window-tile", $$"""hl.dsp.window.float({ action = "disable", {{Target}} })"""),
            ("toggle fullscreen", "desktop:window-toggle-fullscreen", $$"""hl.dsp.window.fullscreen({ mode = "fullscreen", {{Target}} })"""),
            ("workspace 3", "desktop:window-workspace-3", $$"""hl.dsp.window.move({ workspace = "3", {{Target}} })"""),
            ("next monitor", "desktop:window-next-monitor", $$"""hl.dsp.window.move({ monitor = "DP-2", {{Target}} })"""),
            ("previous monitor", "desktop:window-previous-monitor", $$"""hl.dsp.window.move({ monitor = "DP-1", {{Target}} })""")
        } )
        {
            var run = await RunAsync( await RowAsync( query, id ) );
            True( run.Outcome.Succeeded, id );
            Equal( expected, run.Dispatches.Single( ) );
        }

        // The last enabled monitor wraps to the first, skipping a disabled one.
        hyprctl.ActiveWindow = """{"address":"0x123","monitor":2,"floating":false,"fullscreen":0,"size":[800,600],"class":"foot","title":""}""";
        var wrap = await RunAsync( await RowAsync( "next monitor", "desktop:window-next-monitor" ) );
        Equal( """hl.dsp.window.move({ monitor = "DP-1", window = "address:0x123" })""", wrap.Dispatches.Single( ) );

        // A Lua warning is a failure even with exit code zero, and stops the sequence.
        hyprctl.WarnOn = "hl.dsp.window.resize";
        var warned = await RunAsync( left );
        True( !warned.Outcome.Succeeded );
        Equal( "Hyprland did not acknowledge the action.", warned.Outcome.Message );
        Equal( 2, warned.Dispatches.Length );
        hyprctl.WarnOn = null;

        // A closed window, a non-address target, or a forged parameter dispatches nothing.
        foreach ( (var selector, var parameter, var message) in new[]
        {
            ("address:0x999", WindowPlacements.LeftHalf, "The window that was focused when the launcher opened has closed. Search again."),
            ("class:kitty", WindowPlacements.LeftHalf, "The window command has no valid window address. Search again."),
            ("address:0xabc", "diagonal", "The window placement is invalid. Search again.")
        } )
        {
            var forged = new SearchResult( left )
            {
                Actions = [HyprlandAction( "run", HyprlandActionOperations.PlaceWindow, selector )]
            };
            forged.Actions[0].Arguments[HyprlandActionOperations.ParameterKey] = parameter;
            var refused = await RunAsync( forged );
            Equal( message, refused.Outcome.Message );
            Equal( 0, refused.Dispatches.Length );
        }

        var workspace = new SearchResult( left ) { Actions = [HyprlandAction( "run", HyprlandActionOperations.MoveWindowToWorkspace, "address:0xabc" )] };
        workspace.Actions[0].Arguments[HyprlandActionOperations.ParameterKey] = "11";
        Equal( "The workspace is invalid. Search again.", (await RunAsync( workspace )).Outcome.Message );
    }
}
