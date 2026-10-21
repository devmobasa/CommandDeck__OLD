using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static Task ExtensionProcessStopsAfterStartupDeadlineAsync( ) =>
        ExtensionProcessDisposalAsync( ExtensionLaunchProfile.Interactive );

    private static Task ExtensionSearchProcessSkipsGracefulStopAsync( ) =>
        ExtensionProcessDisposalAsync( ExtensionLaunchProfile.Search );

    private static async Task ExtensionProcessDisposalAsync( ExtensionLaunchProfile profile )
    {
        if ( !OperatingSystem.IsLinux( ) || !File.Exists( "/usr/bin/systemd-run" ) )
        {
            SkipSpec( "Graceful extension disposal requires a Linux systemd user manager." );
        }
        var manager = await ProcessRunner.RunAsync( "/usr/bin/systemctl", ["--user", "is-active", "default.target"],
            null, null, TimeSpan.FromSeconds( 2 ), CancellationToken.None );
        if ( manager.ExitCode != 0 )
        {
            SkipSpec( "The systemd user manager is unavailable." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-extension-disposal-", preservePrefix: true ).FullName;
        var unit = "commanddeck-extension-test-" + Guid.NewGuid( ).ToString( "N" ) + ".service";
        ProcessStartInfo start = new( "/usr/bin/systemd-run" )
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // Keep failed units until their exit record has been checked, then reset them in cleanup.
        List<string> arguments = ["--user", "--quiet", "--wait", "--pipe", "--unit=" + unit,
            "-p", "TimeoutStopSec=2s",
            "--setenv=COMMANDDECK_SPEC_TEMP_ROOT=" + Environment.GetEnvironmentVariable( "COMMANDDECK_SPEC_TEMP_ROOT" ),
            "--", Environment.ProcessPath!];
        if ( Path.GetFileNameWithoutExtension( Environment.ProcessPath ) == "dotnet" )
        {
            arguments.Add( typeof( Program ).Assembly.Location );
        }
        arguments.AddRange( ["--extension-disposal-fixture", root, profile.ToString( )] );
        foreach ( var argument in arguments )
        {
            start.ArgumentList.Add( argument );
        }

        using var process = Process.Start( start );
        if ( process is null )
        {
            throw new InvalidOperationException( "The disposal fixture could not start." );
        }

        using CancellationTokenSource startup = new( );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 45 ) );
        try
        {
            Equal( "READY", await process.StandardOutput.ReadLineAsync( deadline.Token ) );
            var group = (await ExtensionSupervisor.ManagerAsync( ["show", "--value", "-p", "ControlGroup", unit], deadline.Token )).Trim( );
            True( group.StartsWith( "/user.slice/", StringComparison.Ordinal ) );
            var child = new ExtensionProcess( process, unit, group, startup.Token, profile );
            startup.Cancel( );
            await child.DisposeAsync( ).AsTask( ).WaitAsync( deadline.Token );
            if ( profile == ExtensionLaunchProfile.Search )
            {
                string outcome;
                do
                {
                    var status = await ProcessRunner.RunAsync( "/usr/bin/systemctl",
                        ["--user", "show", "-p", "ActiveState,Result,ExecMainCode,ExecMainStatus", unit],
                        null, null, TimeSpan.FromSeconds( 45 ), deadline.Token );
                    Equal( 0, status.ExitCode );
                    outcome = status.StandardOutput;
                    if ( outcome.Contains( "ActiveState=failed\n", StringComparison.Ordinal ) ||
                        outcome.Contains( "ActiveState=inactive\n", StringComparison.Ordinal ) )
                    {
                        break;
                    }

                    await Task.Delay( 20, deadline.Token );
                } while ( true );

                // A graceful stop can also end in SIGKILL, but records a timeout or SIGTERM first.
                True( outcome.Contains( "Result=signal\n", StringComparison.Ordinal ), "Search cleanup exits by signal: " + outcome );
                True( outcome.Contains( "ExecMainCode=2\n", StringComparison.Ordinal ), "The child was killed (CLD_KILLED): " + outcome );
                True( outcome.Contains( "ExecMainStatus=9\n", StringComparison.Ordinal ), "The child received SIGKILL: " + outcome );
                True( !File.Exists( Path.Combine( root, "graceful-stop" ) ), "Search cleanup kills the cgroup without a graceful-stop round trip." );
            }
            else
            {
                True( File.Exists( Path.Combine( root, "graceful-stop" ) ), "Disposal sends systemd stop even after startup cancellation." );
            }

            await child.DisposeAsync( ).AsTask( ).WaitAsync( deadline.Token );
        }
        finally
        {
            using CancellationTokenSource cleanup = new( TimeSpan.FromSeconds( 45 ) );
            await ExtensionSupervisor.StopUnitAsync( unit, cleanup.Token );
            await ExtensionSupervisor.ManagerAsync( ["reset-failed", unit], cleanup.Token, allowFailure: true );
            Directory.Delete( root, true );
        }
    }

    private static async Task<int> RunExtensionDisposalFixtureAsync( string root, string profile )
    {
        if ( !Path.GetFileName( root ).StartsWith( "commanddeck-extension-disposal-", StringComparison.Ordinal ) || !Directory.Exists( root ) )
        {
            return 2;
        }
        using var signal = PosixSignalRegistration.Create( PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            File.WriteAllText( Path.Combine( root, "graceful-stop" ), "stopped" );
            if ( profile == nameof( ExtensionLaunchProfile.Interactive ) )
            {
                Environment.Exit( 0 );
            }
        } );
        Console.WriteLine( "READY" );
        await Task.Delay( Timeout.InfiniteTimeSpan );
        return 3;
    }
}
