using System.Diagnostics;
using System.Globalization;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ProcessRunnerUsesKernelMemoryLimitsAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) || !ProcessRunner.ExistsOnPath( "/usr/bin/prlimit" ) )
        {
            throw new SpecSkippedException( "This check requires Linux prlimit." );
        }

        var result = await ProcessRunner.RunBoundedAsync( "/bin/sh", ["-c", "cat /proc/self/limits"], null,
            TimeSpan.FromSeconds( 5 ), ProcessIdleLimits( 64L * 1024 * 1024 ), CancellationToken.None );
        Equal( 0, result.ExitCode );
        var line = result.StandardOutput.Split( '\n' ).Single( line => line.StartsWith( "Max address space", StringComparison.Ordinal ) );
        var values = line["Max address space".Length..].Split( ( char[]? ) null, StringSplitOptions.RemoveEmptyEntries );
        Equal( "67108864", values[0] );
        Equal( "67108864", values[1] );
        True( !result.TimedOut && !result.AnyOutputLimitExceeded, "limits are reported without truncation" );
    }

    private static async Task ProcessRunnerSleepsWithKernelOrUnlimitedMemoryAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) || !ProcessRunner.ExistsOnPath( "/usr/bin/prlimit" ) )
        {
            throw new SpecSkippedException( "This check requires Linux prlimit." );
        }

        foreach ( var maximum in new[] { 64L * 1024 * 1024, long.MaxValue } )
        {
            var root = Directory.CreateTempSubdirectory( "commanddeck-process-idle-" ).FullName;
            var start = ClipboardPrivateFixtureStart( "--process-idle-parent-fixture", root );
            start.ArgumentList.Add( root );
            start.ArgumentList.Add( maximum.ToString( CultureInfo.InvariantCulture ) );
            start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
            using var parent = Process.Start( start );
            if ( parent is null )
            {
                throw new IOException( "Process runner fixture did not start." );
            }

            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 15 ) );
            var errors = parent.StandardError.ReadToEndAsync( timeout.Token );
            try
            {
                Equal( "ready", await parent.StandardOutput.ReadLineAsync( timeout.Token ) );
                await ProcessSleepsWhileIdleAsync( parent.Id, "bounded process runner", timeout.Token );
                await parent.StandardInput.WriteLineAsync( "stop" );
                await parent.WaitForExitAsync( timeout.Token );
                Equal( 0, parent.ExitCode );
                Equal( "", await errors );
            }
            finally
            {
                if ( !parent.HasExited )
                {
                    parent.Kill( entireProcessTree: true );
                    await parent.WaitForExitAsync( );
                }

                Directory.Delete( root, recursive: true );
            }
        }
    }

    private static ProcessExecutionLimits ProcessIdleLimits( long maximum ) => new( )
    {
        MaximumMemoryBytes = maximum,
        MaximumStandardOutputBytes = 4096,
        MaximumStandardErrorBytes = 4096
    };

    private static async Task<int> RunProcessIdleParentFixtureAsync( string root, string maximum )
    {
        var marker = Path.Combine( root, "started" );
        var release = Path.Combine( root, "release" );
        var script = "echo ready > \"" + marker + "\"; while [ ! -e \"" + release + "\" ]; do sleep 0.1; done";
        var work = ProcessRunner.RunBoundedAsync( "/bin/sh", ["-c", script], null,
            TimeSpan.FromSeconds( 10 ), ProcessIdleLimits( long.Parse( maximum, CultureInfo.InvariantCulture ) ), CancellationToken.None ).AsTask( );
        await ClipboardEventuallyAsync( ( ) => File.Exists( marker ) || work.IsCompleted );
        if ( !File.Exists( marker ) )
        {
            throw new IOException( "Bounded child did not start: " + (await work).StandardError );
        }

        Console.WriteLine( "ready" );
        _ = await Console.In.ReadLineAsync( );
        await File.WriteAllTextAsync( release, "stop" );
        var result = await work;
        return result.ExitCode == 0 && !result.TimedOut && !result.AnyOutputLimitExceeded ? 0 : 1;
    }
}
