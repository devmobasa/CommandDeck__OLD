using System.Globalization;

namespace CommandDeck;

// Opening paths and URLs, launching desktop entries, editing at a line, and
// running commands and scripts: the actions of indexed and configured rows.
internal sealed class DesktopActionHandler( EditorLauncher editor ) : ILinuxActionHandler
{
    private static readonly string[] OwnedKinds =
    [
        ActionKinds.OpenPath,
        ActionKinds.RevealPath,
        ActionKinds.EditAtLine,
        ActionKinds.OpenUrl,
        ActionKinds.LaunchDesktop,
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

    private static async ValueTask<ActionExecutionResult> RunTrustedShellAsync(
        CommandAction action,
        CancellationToken cancellationToken )
    {
        var command = LinuxActionArguments.Require( action, "command" );
        var result = await ProcessRunner.RunAsync(
            "/bin/sh",
            ["-lc", command],
            null,
            null,
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
