namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task DesktopLaunchDoesNotWaitForApplicationExitAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }
        var root = Directory.CreateTempSubdirectory( "commanddeck-desktop-launch-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        var release = Path.Combine( root, "release" );
        try
        {
            var capture = Path.Combine( root, "argv" );
            var started = Path.Combine( root, "started" );
            var exited = Path.Combine( root, "exited" );
            var launcher = Path.Combine( root, "uwsm-app" );
            var gio = Path.Combine( root, "gio" );
            var gtk = Path.Combine( root, "gtk-launch" );
            await File.WriteAllTextAsync( launcher, "#!/bin/sh\nwhile [ \"$1\" != -- ]; do shift; done\nshift\n\"$@\" >/dev/null 2>&1 &\n" );
            var application = $"""
                #!/bin/sh
                printf '%s\n' "$@" > '{capture}'
                : > '{started}'
                while [ ! -f '{release}' ]; do /bin/sleep 0.02; done
                : > '{exited}'
                """.ReplaceLineEndings( "\n" );
            await File.WriteAllTextAsync( gio, application );
            await File.WriteAllTextAsync( gtk, application );
            foreach ( var file in new[] { launcher, gio, gtk } )
            {
                File.SetUnixFileMode( file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            }
            Environment.SetEnvironmentVariable( "PATH", root );
            var desktopFile = Path.Combine( root, "Signal test.desktop" );
            var result = new SearchResult
            {
                Id = "app:test",
                Title = "Test",
                Kind = ResultKinds.Application,
                ProviderId = "applications",
                SourceId = SourceIds.Applications
            };
            var action = new CommandAction
            {
                Id = "launch",
                Title = "Launch",
                Kind = ActionKinds.LaunchDesktop,
                Arguments = new( StringComparer.Ordinal ) { ["desktopFile"] = desktopFile, ["desktopId"] = "signal.desktop" }
            };
            result = new SearchResult( result ) { Actions = [action] };
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            SearchCoordinator search = new( [new StaticSearchProvider( [result] )], options.Search );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, new LinuxActionExecutor( ), search );
            await runtime.SearchAsync( new SearchRequest { Query = "Test" }, default );
            foreach ( var launchMode in new[] { 0, 1, 2 } )
            {
                var useGio = launchMode == 0;
                if ( launchMode == 2 )
                {
                    File.Delete( launcher );
                    var fallback = Path.Combine( root, "systemd-run" );
                    await File.WriteAllTextAsync( fallback, "#!/bin/sh\nshift 3\n\"$@\" >/dev/null 2>&1 &\n" );
                    File.SetUnixFileMode( fallback, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
                }
                if ( !useGio )
                {
                    File.Delete( gio );
                }
                File.Delete( release );
                File.Delete( started );
                File.Delete( exited );
                using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 2 ) );
                var outcome = await runtime.InvokeAsync( result.Id, action.Id, false, timeout.Token );
                True( outcome.Succeeded, "desktop launch is acknowledged while the application is still running" );
                for ( var attempt = 0; attempt < 200 && !File.Exists( started ); attempt++ )
                {
                    await Task.Delay( 10 );
                }
                True( File.Exists( started ) && !File.Exists( exited ), "the application remains active after acknowledgement" );
                var usage = await catalog.UsageHistory.GetUsageSignalsAsync( [result.Id], default );
                Equal( launchMode + 1L, usage[result.Id].UseCount );
                var argv = await File.ReadAllLinesAsync( capture );
                Equal( useGio ? "launch|" + desktopFile : "signal", string.Join( '|', argv ) );
                await File.WriteAllTextAsync( release, "" );
                for ( var attempt = 0; attempt < 200 && !File.Exists( exited ); attempt++ )
                {
                    await Task.Delay( 10 );
                }
                True( File.Exists( exited ), "fixture application exits after release" );
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            await File.WriteAllTextAsync( release, "" );
            await Task.Delay( 100 );
            Directory.Delete( root, recursive: true );
        }
    }
}
