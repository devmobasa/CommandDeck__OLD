using System.Diagnostics;
using System.Globalization;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardWorkerRunnerReapsOnCancellationAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-worker-cancellation-", preservePrefix: true ).FullName;
        using CancellationTokenSource cancellation = new( );
        var executable = Environment.ProcessPath!;
        List<string> arguments = [];
        if ( Path.GetFileNameWithoutExtension( executable ) == "dotnet" )
        {
            arguments.Add( typeof( Program ).Assembly.Location );
        }

        arguments.AddRange( ["--worker-cancellation-fixture", root] );
        var work = ProcessRunner.RunAsync( executable, arguments, null, null, TimeSpan.FromSeconds( 60 ), cancellation.Token ).AsTask( );
        try
        {
            var ready = Path.Combine( root, "ready" );
            var started = Stopwatch.GetTimestamp( );
            while ( !File.Exists( ready ) && !work.IsCompleted && Stopwatch.GetElapsedTime( started ) < TimeSpan.FromSeconds( 30 ) )
            {
                await Task.Delay( 10 );
            }

            True( File.Exists( ready ), "worker reached its blocking operation" );
            var pid = int.Parse( await File.ReadAllTextAsync( ready ), CultureInfo.InvariantCulture );
            cancellation.Cancel( );
            await ThrowsAsync<OperationCanceledException>( ( ) => work );
            True( !Directory.Exists( $"/proc/{pid}" ), "runner cannot release caller capacity before reaping" );
        }
        finally
        {
            cancellation.Cancel( );
            try
            {
                await work;
            }
            catch ( OperationCanceledException ) { }

            Directory.Delete( root, recursive: true );
        }
    }

    private static int RunWorkerCancellationFixture( string root )
    {
        if ( !Path.GetFileName( root ).StartsWith( "commanddeck-worker-cancellation-", StringComparison.Ordinal ) || !Directory.Exists( root ) )
        {
            return 2;
        }

        var temporary = Path.Combine( root, "starting" );
        File.WriteAllText( temporary, Environment.ProcessId.ToString( CultureInfo.InvariantCulture ) );
        File.Move( temporary, Path.Combine( root, "ready" ) );
        using ManualResetEvent wait = new( false );
        wait.WaitOne( );
        return 3;
    }

    private static Task ProcessRunnerDeadlineCoversBlockedInputAsync( ) =>
        VerifyBlockedProcessInputAsync( cancel: false );

    private static Task ProcessRunnerCancellationCleansBlockedInputAsync( ) =>
        VerifyBlockedProcessInputAsync( cancel: true );

    private static async Task VerifyBlockedProcessInputAsync( bool cancel )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-process-stdin-", preservePrefix: true ).FullName;
        using CancellationTokenSource watchdog = new( TimeSpan.FromSeconds( 60 ) );
        var executable = Environment.ProcessPath!;
        List<string> arguments = [];
        if ( Path.GetFileNameWithoutExtension( executable ) == "dotnet" )
        {
            arguments.Add( typeof( Program ).Assembly.Location );
        }

        arguments.AddRange( ["--process-runner-stdin-fixture", root] );
        var work = ProcessRunner.RunAsync( executable, arguments, new string( 'x', 2 * 1024 * 1024 ),
            null, TimeSpan.FromSeconds( 45 ), watchdog.Token ).AsTask( );
        var pid = 0;
        try
        {
            var ready = Path.Combine( root, "ready" );
            var started = Stopwatch.GetTimestamp( );
            while ( !File.Exists( ready ) && !work.IsCompleted &&
                    Stopwatch.GetElapsedTime( started ) < TimeSpan.FromSeconds( 30 ) )
            {
                await Task.Delay( 10 );
            }

            True( File.Exists( ready ), "child reached the blocked-stdin operation" );
            pid = int.Parse( await File.ReadAllTextAsync( ready ), CultureInfo.InvariantCulture );
            True( !work.IsCompleted, "stdin write is still blocked after child readiness" );
            if ( cancel )
            {
                watchdog.Cancel( );
                await ThrowsAsync<OperationCanceledException>( ( ) => work );
            }
            else
            {
                var result = await work.WaitAsync( TimeSpan.FromSeconds( 60 ) );
                True( result.TimedOut, "the operation deadline must cover stdin delivery" );
            }

            True( !Directory.Exists( $"/proc/{pid}" ), "blocked-stdin child was reaped" );
        }
        finally
        {
            watchdog.Cancel( );
            try
            {
                await work.WaitAsync( TimeSpan.FromSeconds( 30 ) );
            }
            catch ( OperationCanceledException ) { }

            if ( pid > 0 )
            {
                try
                {
                    using var child = Process.GetProcessById( pid );
                    child.Kill( entireProcessTree: true );
                    await child.WaitForExitAsync( );
                }
                catch ( ArgumentException ) { }
                catch ( InvalidOperationException ) { }
            }

            Directory.Delete( root, recursive: true );
        }
    }

    private static int RunProcessRunnerStdinFixture( string root )
    {
        if ( !Path.GetFileName( root ).StartsWith( "commanddeck-process-stdin-", StringComparison.Ordinal ) ||
            !Directory.Exists( root ) )
        {
            return 2;
        }

        // Model startup slower than the old one-second operation deadline.
        Thread.Sleep( TimeSpan.FromSeconds( 2 ) );

        // Publish readiness only after the PID is complete; file creation alone
        // lets the parent read an empty file before WriteAllText has filled it.
        var ready = Path.Combine( root, "ready" );
        File.WriteAllText( ready + ".pending",
            Environment.ProcessId.ToString( CultureInfo.InvariantCulture ) );
        File.Move( ready + ".pending", ready );

        using ManualResetEvent wait = new( false );
        wait.WaitOne( );
        return 3;
    }

    private static async Task ProcessRunnerReapsOutputDescendantAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) || !File.Exists( "/usr/bin/setsid" ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux and /usr/bin/setsid." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-process-descendant-", preservePrefix: true ).FullName;
        var executable = Environment.ProcessPath!;
        List<string> arguments = [];
        if ( Path.GetFileNameWithoutExtension( executable ) == "dotnet" )
        {
            arguments.Add( typeof( Program ).Assembly.Location );
        }

        arguments.AddRange( ["--process-runner-descendant-fixture", root] );
        var pid = 0;
        try
        {
            var work = ProcessRunner.RunAsync( executable, arguments, null, null,
                TimeSpan.FromSeconds( 45 ), CancellationToken.None ).AsTask( );
            var ready = Path.Combine( root, "ready" );
            var started = Stopwatch.GetTimestamp( );
            while ( !File.Exists( ready ) && !work.IsCompleted &&
                    Stopwatch.GetElapsedTime( started ) < TimeSpan.FromSeconds( 30 ) )
            {
                await Task.Delay( 10 );
            }

            True( File.Exists( ready ), "the parent started the pipe-owning descendant" );
            pid = int.Parse( await File.ReadAllTextAsync( ready ), CultureInfo.InvariantCulture );
            var result = await work.WaitAsync( TimeSpan.FromSeconds( 60 ) );
            True( result.TimedOut, "output draining has the same finite-operation deadline" );

            started = Stopwatch.GetTimestamp( );
            while ( Directory.Exists( $"/proc/{pid}" ) &&
                    Stopwatch.GetElapsedTime( started ) < TimeSpan.FromSeconds( 2 ) )
            {
                await Task.Delay( 10 );
            }

            True( !Directory.Exists( $"/proc/{pid}" ),
                "the output-owning descendant must be terminated and reaped" );
        }
        finally
        {
            if ( pid > 0 )
            {
                try
                {
                    using var descendant = Process.GetProcessById( pid );
                    descendant.Kill( entireProcessTree: true );
                }
                catch ( ArgumentException ) { }
                catch ( InvalidOperationException ) { }
            }

            Directory.Delete( root, recursive: true );
        }
    }

    private static int RunProcessRunnerDescendantFixture( string root )
    {
        if ( !Path.GetFileName( root ).StartsWith( "commanddeck-process-descendant-", StringComparison.Ordinal ) ||
             !Directory.Exists( root ) )
        {
            return 2;
        }

        using Process descendant = new( )
        {
            StartInfo = new ProcessStartInfo( "/usr/bin/sleep" )
            {
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            }
        };
        descendant.StartInfo.ArgumentList.Add( "120" );
        if ( !descendant.Start( ) )
        {
            return 3;
        }

        var ready = Path.Combine( root, "ready" );
        File.WriteAllText( ready + ".pending", descendant.Id.ToString( CultureInfo.InvariantCulture ) );
        File.Move( ready + ".pending", ready );
        return 0;
    }

    private static async Task ProcessRunnerBoundsDefaultOutputAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var result = await ProcessRunner.RunAsync(
            "/usr/bin/head",
            ["-c", (2 * 1024 * 1024).ToString( CultureInfo.InvariantCulture ), "/dev/zero"],
            null,
            null,
            TimeSpan.FromSeconds( 5 ),
            CancellationToken.None );

        True( result.OutputLimitExceeded, "the general runner must report its default output budget" );
        True( result.StandardOutput.Length <= 1024 * 1024,
            "the general runner must not retain output beyond its default budget" );
    }
}
