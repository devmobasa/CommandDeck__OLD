namespace CommandDeck.Specs;

public static partial class Program
{
    // Omarchy's pickers return the choice through files in /tmp, which the
    // daemon's private /tmp hides, so a menu action never runs in the daemon:
    // systemd-run starts it in the user session and waits for it, and its exit
    // status and output stay the action's result. uwsm-app cannot wait, so it
    // is never asked. The fake launchers record their argv and never start the
    // command.
    private static async Task OmarchyMenuActionsRunInTheUserSessionAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-omarchy-action-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var capture = Path.Combine( root, "calls" );
            var ran = Path.Combine( root, "ran" );
            var sessionRun = Path.Combine( root, "systemd-run" );
            await WriteExecutableAsync( Path.Combine( root, "uwsm-app" ), ArgvRecorder( capture ) );
            await WriteExecutableAsync( sessionRun, ArgvRecorder( capture ) );
            Environment.SetEnvironmentVariable( "PATH", root );
            var document = $$"""
                {
                  "style": {"label":"Style"},
                  "style.pick": {"label":"Pick","action":"choice=$(synthetic-picker); [[ -n $choice ]] && synthetic-apply \"$choice\" %h; : > '{{ran}}'"}
                }
                """;
            var menu = OmarchyMenu.Parse( [new OmarchyMenuDocument( "menu.jsonc", document )] );
            SearchCoordinator coordinator = new( [new OmarchyMenuProvider( _ => ValueTask.FromResult( menu ) )], new SearchOptions( ) );
            var row = (await coordinator.SearchAsync( new SearchRequest { OmarchyMenuId = "style" }, default )).Single( );
            var run = row.Actions.Single( );

            var outcome = await new LinuxActionExecutor( ).ExecuteAsync( row, run, string.Empty, default );
            True( outcome.Succeeded, "the session run's exit status is the action's result" );
            True( !File.Exists( ran ), "the daemon never runs the command itself" );
            // One literal argument; the service manager reads "$$" as "$" and
            // expands no % specifier. The unit stops 5 s after the 5-minute wait.
            Equal( "systemd-run|--user|--quiet|--collect|--wait|--pipe|--property=ExitType=cgroup|--property=RuntimeMaxSec=305|" +
                $"--|/bin/sh|-lc|choice=$$(synthetic-picker); [[ -n $$choice ]] && synthetic-apply \"$$choice\" %h; : > '{ran}'\n",
                await File.ReadAllTextAsync( capture ) );

            await WriteExecutableAsync( sessionRun, ArgvRecorder( capture ) + "\necho 'no choice' >&2\nexit 3\n" );
            var failed = await new LinuxActionExecutor( ).ExecuteAsync( row, run, string.Empty, default );
            True( !failed.Succeeded );
            Equal( "Command exited with 3.", failed.Message );
            True( failed.DetailMarkdown!.Contains( "no choice", StringComparison.Ordinal ) );

            File.Delete( sessionRun );
            var unavailable = await new LinuxActionExecutor( ).ExecuteAsync( row, run, string.Empty, default );
            True( !unavailable.Succeeded );
            Equal( "systemd-run is unavailable to run the command in the user session.", unavailable.Message );
            True( !File.Exists( ran ), "nothing falls back to running inside the daemon" );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( root, recursive: true );
        }
    }
}
