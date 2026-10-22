using System.Globalization;

namespace CommandDeck;

// Opening paths and URLs, launching desktop entries and typed programs, editing
// at a line, and running commands and scripts: the actions of indexed,
// configured and command-mode rows.
internal sealed class DesktopActionHandler( EditorLauncher editor ) : ILinuxActionHandler
{
    private static readonly string[] OwnedKinds =
    [
        ActionKinds.OpenPath,
        ActionKinds.RevealPath,
        ActionKinds.EditAtLine,
        ActionKinds.OpenUrl,
        ActionKinds.LaunchDesktop,
        ActionKinds.LaunchArgv,
        ActionKinds.RunArgv,
        ActionKinds.RunTrustedShell,
        ActionKinds.RunScript
    ];

    public IReadOnlyList<string> Kinds => OwnedKinds;

    public async ValueTask<ActionExecutionResult> ExecuteAsync(
        SearchResult result,
        CommandAction action,
        string query,
        CancellationToken cancellationToken ) => action.Kind switch
        {
            ActionKinds.OpenPath => await OpenPathAsync( action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.RevealPath => await RevealPathAsync( action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.EditAtLine => await editor.EditAtLineAsync( action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.OpenUrl => await OpenUrlAsync( query, action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.LaunchDesktop => await LaunchDesktopAsync( action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.LaunchArgv => await LaunchArgvAsync( action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.RunArgv => await RunArgvAsync( action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.RunTrustedShell => await RunTrustedShellAsync( action, cancellationToken ).ConfigureAwait( false ),
            ActionKinds.RunScript => await ScriptCommandProvider.ExecuteAsync( action, cancellationToken ).ConfigureAwait( false ),
            _ => LinuxActionArguments.UnknownKind( action )
        };

    private static async ValueTask<ActionExecutionResult> OpenPathAsync(
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var path = PathActionPayload.Read( action.Arguments ).Path;
        return await ManagedLaunch.LaunchApplicationAsync(
            new DesktopEntryCommand { Executable = "xdg-open", Arguments = [path] },
            terminal: false,
            cancellationToken ).ConfigureAwait( false );
    }

    private static async ValueTask<ActionExecutionResult> RevealPathAsync(
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var path = PathActionPayload.Read( action.Arguments ).Path;
        var directory = Directory.Exists( path )
            ? path
            : Path.GetDirectoryName( path ) ?? path;
        return await ManagedLaunch.LaunchApplicationAsync(
            new DesktopEntryCommand { Executable = "xdg-open", Arguments = [directory] },
            terminal: false,
            cancellationToken ).ConfigureAwait( false );
    }

    private static async ValueTask<ActionExecutionResult> OpenUrlAsync(
        string query,
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var url = UrlActionPayload.Read( action.Arguments ).Expand( query );

        return await ManagedLaunch.LaunchApplicationAsync(
            new DesktopEntryCommand { Executable = "xdg-open", Arguments = [url] },
            terminal: false,
            cancellationToken ).ConfigureAwait( false );
    }

    private static async ValueTask<ActionExecutionResult> LaunchDesktopAsync(
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var encodedArguments = action.Arguments.GetValueOrDefault( "launchArguments" );
        if ( !string.IsNullOrEmpty( encodedArguments ) )
        {
            var exec = LinuxActionArguments.Require( action, "exec" );
            var desktopFileWithArguments = LinuxActionArguments.Require( action, "desktopFile" );
            var applicationName = action.Arguments.GetValueOrDefault(
                "applicationName",
                Path.GetFileNameWithoutExtension( desktopFileWithArguments ) );
            var userArguments = LaunchArgumentCodec.Decode( encodedArguments );
            var command = DesktopEntryCommand.Create(
                exec,
                applicationName,
                action.Arguments.GetValueOrDefault( "icon" ),
                desktopFileWithArguments,
                userArguments );
            if ( string.Equals(
                action.Arguments.GetValueOrDefault( "terminal" ),
                "true",
                StringComparison.OrdinalIgnoreCase ) )
            {
                return await ManagedLaunch.LaunchApplicationAsync(
                    command,
                    terminal: true,
                    cancellationToken ).ConfigureAwait( false );
            }

            return await ManagedLaunch.LaunchApplicationAsync(
                command,
                terminal: false,
                cancellationToken ).ConfigureAwait( false );
        }

        var desktopFile = action.Arguments.GetValueOrDefault( "desktopFile" );
        if ( !string.IsNullOrWhiteSpace( desktopFile ) && ProcessRunner.ExistsOnPath( "gio" ) )
        {
            return await ManagedLaunch.LaunchApplicationAsync(
                new DesktopEntryCommand { Executable = "gio", Arguments = ["launch", desktopFile] },
                terminal: false,
                cancellationToken ).ConfigureAwait( false );
        }

        var desktopId = LinuxActionArguments.Require( action, "desktopId" );
        if ( !ProcessRunner.ExistsOnPath( "gtk-launch" ) )
        {
            return ActionExecutionResult.Failure(
                "Neither 'gio launch' nor 'gtk-launch' could launch the desktop entry." );
        }

        var idWithoutSuffix = desktopId.EndsWith(
            ".desktop",
            StringComparison.OrdinalIgnoreCase )
                ? desktopId[..^8]
                : desktopId;

        return await ManagedLaunch.LaunchApplicationAsync(
            new DesktopEntryCommand { Executable = "gtk-launch", Arguments = [idWithoutSuffix] },
            terminal: false,
            cancellationToken ).ConfigureAwait( false );
    }

    // Detached like an application launch, through the same uwsm-app or
    // systemd-run path; "terminal" uses the terminal of Terminal=true entries.
    private static async ValueTask<ActionExecutionResult> LaunchArgvAsync(
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var argv = LaunchArgumentCodec.Decode( LinuxActionArguments.Require( action, "argv" ) );
        if ( argv.Count == 0 || string.IsNullOrEmpty( argv[0] ) )
        {
            return ActionExecutionResult.Failure( "The command has no executable." );
        }

        return await ManagedLaunch.LaunchApplicationAsync(
            new DesktopEntryCommand { Executable = argv[0], Arguments = argv.Skip( 1 ).ToArray( ) },
            terminal: action.Arguments.GetValueOrDefault( "terminal" ) == "true",
            cancellationToken ).ConfigureAwait( false );
    }

    private static async ValueTask<ActionExecutionResult> RunArgvAsync(
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var encoded = LinuxActionArguments.Require( action, "argv" );
        var argv = encoded.Split(
            '\u001f',
            StringSplitOptions.RemoveEmptyEntries );
        if ( argv.Length == 0 )
        {
            return ActionExecutionResult.Failure( "The command has no executable." );
        }

        var workingDirectory = action.Arguments.GetValueOrDefault( "workingDirectory" );
        return await ManagedLaunch.RunAndReportAsync(
            argv[0],
            argv.Skip( 1 ),
            workingDirectory,
            cancellationToken ).ConfigureAwait( false );
    }

    // An Omarchy menu command runs in the user session, as from Omarchy's own menu:
    // in the daemon, NoNewPrivileges would block sudo and pkexec, the root is
    // read-only, files would get UMask=0077, and a daemon restart would kill it.
    private static async ValueTask<ActionExecutionResult> RunTrustedShellAsync(
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var command = LinuxActionArguments.Require( action, "command" );
        var result = await ManagedLaunch.RunInSessionAsync(
            new DesktopEntryCommand { Executable = "/bin/sh", Arguments = ["-lc", command] },
            TimeSpan.FromMinutes( 5 ),
            cancellationToken ).ConfigureAwait( false );

        var exceeded = result.OutputLimitExceeded || result.StandardErrorLimitExceeded;
        return result.ExitCode == 0 && !result.TimedOut && !exceeded
            ? ActionExecutionResult.Success( )
            : ActionExecutionResult.Failure(
                exceeded
                    ? "Command exceeded its output limit."
                    : result.TimedOut
                        ? "Command timed out."
                        : $"Command exited with {result.ExitCode.ToString( CultureInfo.InvariantCulture )}.",
                $"```\n{result.StandardError}\n```" );
    }
}
