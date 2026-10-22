using System.Globalization;

namespace CommandDeck;

// Process launches shared by the action handlers: a detached application under
// uwsm-app or systemd-run, a bounded run whose exit code is the result, and the
// same bounded run in the user session.
internal static class ManagedLaunch
{
    private static readonly TimeSpan SessionStopMargin = TimeSpan.FromSeconds( 5 );

    public static async ValueTask<ActionExecutionResult> LaunchApplicationAsync(
        DesktopEntryCommand command,
        bool terminal,
        CancellationToken cancellationToken )
    {
        if ( ProcessRunner.ExistsOnPath( "uwsm-app" ) )
        {
            List<string> arguments = ["-t", "service", "-S", "both"];
            if ( terminal )
            {
                arguments.Add( "-T" );
            }

            arguments.Add( "--" );
            arguments.AddRange( LiteralWords( command ) );
            return await RunAndReportAsync(
                "uwsm-app",
                arguments,
                null,
                cancellationToken,
                TimeSpan.FromSeconds( 15 ) ).ConfigureAwait( false );
        }

        if ( !ProcessRunner.ExistsOnPath( "systemd-run" ) )
        {
            return ActionExecutionResult.Failure(
                "Neither uwsm-app nor systemd-run is available for a detached application launch." );
        }

        List<string> fallbackArguments = ["--user", "--quiet", "--collect"];
        if ( terminal )
        {
            if ( !ProcessRunner.ExistsOnPath( "xdg-terminal-exec" ) )
            {
                return ActionExecutionResult.Failure(
                    "This application needs a terminal, but xdg-terminal-exec is unavailable." );
            }

            fallbackArguments.Add( "xdg-terminal-exec" );
            fallbackArguments.Add( "--" );
        }

        fallbackArguments.AddRange( LiteralWords( command ) );
        return await RunAndReportAsync(
            "systemd-run",
            fallbackArguments,
            null,
            cancellationToken,
            TimeSpan.FromSeconds( 15 ) ).ConfigureAwait( false );
    }

    // Every route here starts a transient unit, whose service manager expands
    // $NAME and ${NAME} in the arguments; "$$" is its literal dollar sign.
    // It expands no % specifiers there, so % stays as it is. systemd-run
    // resolves the executable itself, so the executable stays as it is.
    private static IEnumerable<string> LiteralWords( DesktopEntryCommand command ) =>
        command.Arguments.Select( static word => word.Replace( "$", "$$", StringComparison.Ordinal ) )
            .Prepend( command.Executable );

    public static async ValueTask<ActionExecutionResult> RunAndReportAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null )
    {
        var result = await ProcessRunner.RunAsync(
            executable,
            arguments,
            null,
            workingDirectory,
            timeout ?? TimeSpan.FromMinutes( 2 ),
            cancellationToken ).ConfigureAwait( false );

        var exceeded = result.OutputLimitExceeded || result.StandardErrorLimitExceeded;
        if ( result.ExitCode == 0 && !result.TimedOut && !exceeded )
        {
            return ActionExecutionResult.Success( );
        }

        var message = exceeded
            ? $"'{executable}' exceeded its output limit."
            : result.TimedOut
            ? $"'{executable}' timed out."
            : $"'{executable}' exited with code {result.ExitCode.ToString( CultureInfo.InvariantCulture )}.";
        return ActionExecutionResult.Failure(
            message,
            string.IsNullOrWhiteSpace( result.StandardError )
                ? null
                : $"```\n{result.StandardError.Trim( )}\n```" );
    }

    // Runs outside the daemon's sandbox (a read-only root, NoNewPrivileges,
    // UMask=0077) and outside its cgroup, so a daemon restart does not kill it:
    // the user's service manager starts a transient service, and --wait
    // and --pipe return its exit status and output. With ExitType=cgroup the
    // wait lasts until every process the command started in its own unit has
    // exited. Stopping the local client would leave the unit running, so
    // RuntimeMaxSec stops it just after the wait's bound, which reports the
    // timeout first; a cancelled wait leaves it to the same bound. Omarchy's
    // launchers hand apps to uwsm-app through setsid, into units of their own
    // that neither keep this unit running nor stop with it.
    public static ValueTask<ProcessResult> RunInSessionAsync(
        DesktopEntryCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken )
    {
        if ( !ProcessRunner.ExistsOnPath( "systemd-run" ) )
        {
            throw new InvalidOperationException( "systemd-run is unavailable to run the command in the user session." );
        }

        var runtimeLimit = ( long ) Math.Ceiling( (timeout + SessionStopMargin).TotalSeconds );
        return ProcessRunner.RunAsync(
            "systemd-run",
            ["--user", "--quiet", "--collect", "--wait", "--pipe", "--property=ExitType=cgroup",
                $"--property=RuntimeMaxSec={runtimeLimit.ToString( CultureInfo.InvariantCulture )}", "--", .. LiteralWords( command )],
            null,
            null,
            timeout,
            cancellationToken );
    }
}
