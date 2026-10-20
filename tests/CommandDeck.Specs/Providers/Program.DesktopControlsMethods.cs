namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task DesktopControlsMatchPlainSearchAndHideMissingToolsAsync( )
    {
        HashSet<string> installed = new( StringComparer.Ordinal ) { "wpctl", "rfkill" };
        List<string> probed = [];
        DesktopControlsProvider provider = new( tool =>
        {
            probed.Add( tool );
            return installed.Contains( tool );
        } );
        SearchCoordinator search = new( [provider], new SearchOptions( ) );

        async Task<IReadOnlyList<SearchResult>> FindAsync( string query ) =>
            await search.SearchAsync( new SearchRequest { Query = query, Limit = 50 }, default );
        static string Argv( SearchResult result ) =>
            result.Actions.Single( ).Arguments["argv"].Replace( '\u001f', '|' );

        var volume = await FindAsync( "volume" );
        True( volume.All( static result => result.SourceId == SourceIds.DesktopControls ) );
        foreach ( var id in new[] { "volume-up", "volume-down", "volume-mute", "volume-0", "volume-25", "volume-50", "volume-75", "volume-100" } )
        {
            True( volume.Any( result => result.Id == DesktopControlResultIds.For( id ) ), id );
        }

        var up = volume.Single( static result => result.Id == "desktop:volume-up" );
        Equal( ActionKinds.RunArgv, up.Actions.Single( ).Kind );
        Equal( ResultKinds.CustomCommand, up.Kind );
        Equal( "wpctl|set-volume|-l|1.0|@DEFAULT_AUDIO_SINK@|5%+", Argv( up ) );
        Equal( "wpctl|set-volume|@DEFAULT_AUDIO_SINK@|5%-", Argv( volume.Single( static result => result.Id == "desktop:volume-down" ) ) );

        var fifty = await FindAsync( "volume 50" );
        Equal( "desktop:volume-50", fifty[0].Id );
        Equal( "wpctl|set-volume|@DEFAULT_AUDIO_SINK@|50%", Argv( fifty[0] ) );

        var mute = await FindAsync( "mute" );
        Equal( "wpctl|set-mute|@DEFAULT_AUDIO_SINK@|toggle", Argv( mute.Single( static result => result.Id == "desktop:volume-mute" ) ) );
        Equal( "wpctl|set-mute|@DEFAULT_AUDIO_SOURCE@|toggle", Argv( mute.Single( static result => result.Id == "desktop:microphone-mute" ) ) );

        var wifi = await FindAsync( "wifi" );
        Equal( "desktop:wifi-toggle", wifi.Single( ).Id );
        Equal( "rfkill|toggle|wlan", Argv( wifi[0] ) );
        Equal( "rfkill|toggle|bluetooth", Argv( (await FindAsync( "bluetooth" )).Single( ) ) );

        // A missing tool hides its commands instead of adding error rows.
        Equal( 0, (await FindAsync( "next track" )).Count );
        Equal( 0, (await FindAsync( "play pause" )).Count );
        Equal( 0, (await FindAsync( "firefox" )).Count );
        var browsed = await FindAsync( "ctl:" );
        Equal( DesktopControlCatalog.System.Count( static control => control.Tool != "playerctl" ) + 1, browsed.Count );
        Equal( "desktop:color-picker-unavailable", browsed.Single( static result => result.Kind == ResultKinds.Status ).Id );
        True( (await FindAsync( "> volume up" )).Any( static result => result.Id == "desktop:volume-up" ),
            "command mode offers desktop controls" );
        installed.Add( "playerctl" );
        Equal( "playerctl|next", Argv( (await FindAsync( "next track" ))[0] ) );

        // Home, one letter, calculator, a disabled source, and an exclusion do no work.
        probed.Clear( );
        foreach ( (var query, var enabled) in new (string, string[]?)[]
        {
            ("", null), ("v", null), ("= 5 + volume", null), ("volume", []), ("volume -ctl:", null)
        } )
        {
            var parsed = QueryParser.Parse( query );
            var rows = await provider.SearchAsync( parsed,
                new SearchRequest { Query = query, Limit = 50, EnabledSearchSources = enabled }, default );
            Equal( 0, rows.Count );
        }

        Equal( 0, probed.Count );
    }

    private static async Task DesktopControlsRunLiteralArgvThroughRunArgvAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-desktop-controls-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var capture = Path.Combine( root, "calls" );
            var recorder = ArgvRecorder( capture );
            await WriteExecutableAsync( Path.Combine( root, "wpctl" ), recorder );
            await WriteExecutableAsync( Path.Combine( root, "rfkill" ), recorder );
            await WriteExecutableAsync( Path.Combine( root, "playerctl" ),
                "#!/bin/sh\nprintf 'No players found\\n' >&2\nexit 1\n" );

            // Only the fixture directory is searched, so no real tool can run.
            Environment.SetEnvironmentVariable( "PATH", root );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, new LinuxActionExecutor( ),
                new SearchCoordinator( [new DesktopControlsProvider( )], options.Search ) );

            foreach ( (var query, var id, var expected) in new[]
            {
                ("volume up", "desktop:volume-up", "wpctl|set-volume|-l|1.0|@DEFAULT_AUDIO_SINK@|5%+"),
                ("microphone", "desktop:microphone-mute", "wpctl|set-mute|@DEFAULT_AUDIO_SOURCE@|toggle"),
                ("bluetooth", "desktop:bluetooth-toggle", "rfkill|toggle|bluetooth")
            } )
            {
                var results = await runtime.SearchAsync( new SearchRequest { Query = query, Limit = 20 }, default );
                True( results.Any( result => result.Id == id ), id );
                var outcome = await runtime.InvokeAsync( id, "run", false, default );
                True( outcome.Succeeded, outcome.Message );
                Equal( expected, (await File.ReadAllLinesAsync( capture ))[^1] );
            }

            var usage = await catalog.UsageHistory.GetUsageSignalsAsync( ["desktop:volume-up"], default );
            Equal( 1L, usage["desktop:volume-up"].UseCount );

            await runtime.SearchAsync( new SearchRequest { Query = "play pause", Limit = 20 }, default );
            var failed = await runtime.InvokeAsync( "desktop:media-play-pause", "run", false, default );
            True( !failed.Succeeded );
            Equal( "'playerctl' exited with code 1.", failed.Message );
            True( failed.DetailMarkdown?.Contains( "No players found", StringComparison.Ordinal ) == true );
            Equal( 3, (await File.ReadAllLinesAsync( capture )).Length );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ColorPickerStartsAfterTheLauncherAndReportsAMissingToolAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-color-picker-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var capture = Path.Combine( root, "calls" );
            var ran = Path.Combine( root, "hyprpicker-ran" );
            await WriteExecutableAsync( Path.Combine( root, "systemd-run" ), ArgvRecorder( capture ) );

            // Only the fixture directory is searched, so the real picker cannot start.
            Environment.SetEnvironmentVariable( "PATH", root );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, new LinuxActionExecutor( ),
                new SearchCoordinator( [new DesktopControlsProvider( )], options.Search ) );

            // Asked for by name, a missing hyprpicker is reported on an inert row.
            var unavailable = (await runtime.SearchAsync( new SearchRequest { Query = "color picker", Limit = 20 }, default )).Single( );
            Equal( ResultKinds.Status, unavailable.Kind );
            Equal( 0, unavailable.Actions.Count );
            Equal( "Unavailable: install hyprpicker to pick a color from the screen", unavailable.Subtitle );

            await WriteExecutableAsync( Path.Combine( root, "hyprpicker" ), $"#!/bin/sh\n: > '{ran}'\n" );
            var picker = (await runtime.SearchAsync( new SearchRequest { Query = "pick color", Limit = 20 }, default )).Single( );
            Equal( "desktop:color-picker", picker.Id );
            Equal( "Color · hyprpicker", picker.Subtitle );
            True( picker.Actions.Single( ).CloseAfterAction,
                "the cached picker action tells the launcher to close regardless of the ordinary action preference" );
            var outcome = await runtime.InvokeAsync( picker.Id, "run", false, default );
            True( outcome.Succeeded, outcome.Message );
            Equal( "systemd-run|--user|--quiet|--collect|--on-active=300ms|--timer-property=AccuracySec=10ms|--|hyprpicker|--autocopy",
                (await File.ReadAllLinesAsync( capture )).Single( ) );
            True( !File.Exists( ran ), "the timer starts hyprpicker once the launcher has closed" );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( root, recursive: true );
        }
    }

    // A fake tool that appends its name and arguments, separated by |, to a file.
    private static string ArgvRecorder( string capture ) => $$"""
        #!/bin/sh
        printf '%s' "${0##*/}" >> '{{capture}}'
        for argument in "$@"; do printf '|%s' "$argument" >> '{{capture}}'; done
        printf '\n' >> '{{capture}}'
        """.ReplaceLineEndings( "\n" );

    [System.Runtime.Versioning.SupportedOSPlatform( "linux" )]
    private static async Task WriteExecutableAsync( string path, string content )
    {
        await File.WriteAllTextAsync( path, content );
        File.SetUnixFileMode( path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
    }
}
