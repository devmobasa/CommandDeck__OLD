using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly string[] MemoryFallbackSandboxArguments =
        ["--die-with-parent", "--ro-bind", "/", "/", "--tmpfs", "/tmp", "--ro-bind", "/dev/null", "/usr/bin/prlimit", "--"];

    private static async Task ProcessRunnerRetainsResidentMemoryFallbackAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) || !ProcessRunner.ExistsOnPath( "bwrap" ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux Bubblewrap." );
        }

        var start = ClipboardTransportFixtureStart( acknowledge: true );
        start.ArgumentList.RemoveAt( start.ArgumentList.Count - 1 );
        start.ArgumentList.RemoveAt( start.ArgumentList.Count - 1 );
        List<string> command = [start.FileName, .. start.ArgumentList, "--process-memory-fallback-parent-fixture"];
        start.FileName = "bwrap";
        start.ArgumentList.Clear( );
        var temporaryRoot = Environment.GetEnvironmentVariable( "COMMANDDECK_SPEC_TEMP_ROOT" );
        if ( temporaryRoot is null )
        {
            throw new InvalidOperationException( "The memory fixture requires its owned temporary root." );
        }

        string[] sandbox = [.. MemoryFallbackSandboxArguments[..^1], "--bind", temporaryRoot, temporaryRoot, "--"];
        foreach ( var argument in sandbox.Concat( command ) )
        {
            start.ArgumentList.Add( argument );
        }

        start.RedirectStandardOutput = start.RedirectStandardError = true;
        using var process = Process.Start( start );
        if ( process is null )
        {
            throw new IOException( "Memory fallback fixture did not start." );
        }

        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 15 ) );
        var output = process.StandardOutput.ReadToEndAsync( timeout.Token );
        var errors = process.StandardError.ReadToEndAsync( timeout.Token );
        try
        {
            await process.WaitForExitAsync( timeout.Token );
            var error = await errors;
            if ( process.ExitCode != 0 && error.StartsWith( "bwrap:", StringComparison.Ordinal ) )
            {
                throw new SpecSkippedException( "Bubblewrap could not create the isolated fixture: " + error.Trim( ) );
            }

            True( process.ExitCode == 0, $"Memory fallback fixture exited {process.ExitCode}: {error}" );
            Equal( "", error );
            Equal( "rss-enforced", (await output).Trim( ) );
        }
        finally
        {
            if ( !process.HasExited )
            {
                process.Kill( entireProcessTree: true );
                await process.WaitForExitAsync( );
            }
        }
    }

    private static async Task<int> RunProcessMemoryFallbackParentAsync( )
    {
        True( !ProcessRunner.ExistsOnPath( "/usr/bin/prlimit" ), "kernel wrapper is unavailable only inside the fixture" );
        var start = ClipboardTransportFixtureStart( acknowledge: true );
        start.ArgumentList.RemoveAt( start.ArgumentList.Count - 1 );
        start.ArgumentList.RemoveAt( start.ArgumentList.Count - 1 );
        start.ArgumentList.Add( "--process-memory-allocation-fixture" );
        var temporaryRoot = Environment.GetEnvironmentVariable( "COMMANDDECK_SPEC_TEMP_ROOT" );
        if ( temporaryRoot is null )
        {
            throw new InvalidOperationException( "The allocating child requires its owned temporary root." );
        }

        ProcessExecutionLimits limits = new( )
        {
            MaximumMemoryBytes = 64L * 1024 * 1024,
            MaximumStandardOutputBytes = 4096,
            MaximumStandardErrorBytes = 4096,
            EnvironmentVariables = new Dictionary<string, string>( StringComparer.Ordinal )
            {
                ["COMMANDDECK_SPEC_TEMP_ROOT"] = temporaryRoot,
                ["LANG"] = "C.UTF-8",
                ["LC_ALL"] = "C.UTF-8"
            }
        };
        var result = await ProcessRunner.RunBoundedAsync( start.FileName, start.ArgumentList.ToArray( ), null,
            TimeSpan.FromSeconds( 5 ), limits, CancellationToken.None );
        True( result.MemoryLimitExceeded, $"the finite RSS fallback terminates the allocating child; exit={result.ExitCode}, timeout={result.TimedOut}, stderr={result.StandardError}" );
        True( !result.TimedOut && !result.AnyOutputLimitExceeded, "the memory limit, rather than another bound, ended execution" );
        True( result.ExitCode != 0, "the allocating child was terminated" );
        Console.WriteLine( "rss-enforced" );
        return 0;
    }

    private static async Task<int> RunProcessMemoryAllocationFixtureAsync( )
    {
        var bytes = new byte[128 * 1024 * 1024];
        for ( var offset = 0; offset < bytes.Length; offset += 4096 )
        {
            bytes[offset] = 1;
        }

        await Task.Delay( TimeSpan.FromSeconds( 10 ) );
        GC.KeepAlive( bytes );
        return 0;
    }
}
