using System.Globalization;

namespace CommandDeck;

// Process launches shared by the action handlers: a detached application under
// uwsm-app or systemd-run, and a bounded run whose exit code is the result.
internal static class ManagedLaunch
{
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

    // Both routes start a transient unit, whose service manager expands $NAME
    // and ${NAME} in the arguments; "$$" is its literal dollar sign. systemd-run
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
}
