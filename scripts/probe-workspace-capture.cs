#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:include ./WorkspaceCaptureProbeEvidence.cs

using System.Diagnostics;
using System.Text.Json;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform( "linux" )]

return await CaptureProbe.RunAsync( args );

static class CaptureProbe
{
    private static string instance = "";
    private static string socket = "";
    private static string output = "";
    private static string fixture = "";

    public static async Task<int> RunAsync( string[] args )
    {
        // Explicitly targets a disposable nested compositor. Never captures user windows.
        if ( args.Length != 3 )
        {
            Console.Error.WriteLine( "Usage: dotnet scripts/probe-workspace-capture.cs <nested-instance> <wayland-socket> <new-output-directory>" );
            return 2;
        }

        instance = args[0];
        socket = args[1];
        output = Path.GetFullPath( args[2] );
        if ( instance == Environment.GetEnvironmentVariable( "HYPRLAND_INSTANCE_SIGNATURE" )
            || socket == Environment.GetEnvironmentVariable( "WAYLAND_DISPLAY" ) || Directory.Exists( output ) )
        {
            throw new InvalidOperationException( "Use a separate compositor and a new output directory." );
        }

        var root = Directory.GetCurrentDirectory( );
        fixture = Path.Combine( root, "tests", "CommandDeck.CaptureProbe" );
        if ( !File.Exists( Path.Combine( fixture, "Grid.qml" ) ) )
        {
            throw new InvalidOperationException( "Run from the repository root." );
        }

        using var instances = JsonDocument.Parse( await Run( "hyprctl", ["-j", "instances"] ) );
        var matching = instances.RootElement.EnumerateArray( ).Any( value => value.GetProperty( "instance" ).GetString( ) == instance
            && value.GetProperty( "wl_socket" ).GetString( ) == socket );
        using var status = JsonDocument.Parse( await Control( "-j", "status" ) );
        using var clients = JsonDocument.Parse( await Control( "-j", "clients" ) );
        if ( !matching || status.RootElement.GetProperty( "backend" ).GetString( ) != "wayland"
            || clients.RootElement.GetArrayLength( ) != 0 )
        {
            throw new InvalidOperationException( "The matching nested Wayland compositor must contain no windows." );
        }

        Directory.CreateDirectory( output, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        foreach ( var count in new[] { 1, 3 } )
        {
            await Probe( count );
        }

        return 0;
    }

    static async Task Probe( int count )
    {
        var directory = Path.Combine( output, count.ToString( ) );
        Directory.CreateDirectory( directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        foreach ( var file in Directory.EnumerateFiles( Path.Combine( Directory.GetCurrentDirectory( ), "qml", "common" ) )
            .Concat( Directory.EnumerateFiles( fixture ) ) )
        {
            if ( Path.GetExtension( file ) is ".qml" or ".js" )
            {
                File.Copy( file, Path.Combine( directory, Path.GetFileName( file ) ) );
            }
        }

        await Control( "dispatch", "hl.dsp.focus({workspace = 1})" );
        using var sources = StartQml( "Sources.qml", count, directory );
        var sourceOutput = sources.StandardOutput.ReadToEndAsync( );
        var sourceError = sources.StandardError.ReadToEndAsync( );
        try
        {
            await Task.Delay( 2500 );
            if ( sources.HasExited )
            {
                throw new InvalidOperationException( "Synthetic source exited: " + await sourceError );
            }

            await Control( "dispatch", "hl.dsp.focus({workspace = 2})" );
            using var grid = StartQml( "Grid.qml", count, directory );
            var gridOutput = grid.StandardOutput.ReadToEndAsync( );
            var gridError = grid.StandardError.ReadToEndAsync( );
            var samples = new List<(long Elapsed, long Rss)>( );
            var clock = Stopwatch.StartNew( );
            try
            {
                while ( !grid.HasExited && clock.Elapsed < TimeSpan.FromSeconds( 22 ) )
                {
                    grid.Refresh( );
                    samples.Add( (clock.ElapsedMilliseconds, grid.WorkingSet64) );
                    await Task.Delay( 100 );
                }

                if ( !grid.HasExited )
                {
                    throw new TimeoutException( "Capture/lifecycle probe did not finish within 22 seconds." );
                }

                var log = await gridOutput + await gridError;
                await File.WriteAllTextAsync( Path.Combine( directory, "grid.log" ), log );
                await File.WriteAllLinesAsync( Path.Combine( directory, "rss.csv" ),
                    samples.Select( value => $"{value.Elapsed},{value.Rss}" ).Prepend( "elapsedMilliseconds,rssBytes" ) );
                if ( grid.ExitCode != 0 || !log.Contains( "\"event\":\"teardown\"", StringComparison.Ordinal ) )
                {
                    throw new InvalidOperationException( "Probe failed; inspect " + directory );
                }
            }
            finally
            {
                if ( !grid.HasExited )
                {
                    grid.Kill( entireProcessTree: true );
                    await grid.WaitForExitAsync( );
                }
            }
        }
        finally
        {
            if ( !sources.HasExited )
            {
                sources.Kill( entireProcessTree: true );
                await sources.WaitForExitAsync( );
            }

            await File.WriteAllTextAsync( Path.Combine( directory, "source.log" ), await sourceOutput + await sourceError );
        }

        await WorkspaceCaptureProbeEvidence.VerifyAsync( directory, count );
        Console.WriteLine( $"Verified {count}-window synthetic frames and lifecycle in {directory}." );
    }

    static Process StartQml( string file, int count, string directory )
    {
        var start = new ProcessStartInfo( "qs" ) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add( "-p" );
        start.ArgumentList.Add( Path.Combine( directory, file ) );
        start.Environment["WAYLAND_DISPLAY"] = socket;
        start.Environment["HYPRLAND_INSTANCE_SIGNATURE"] = instance;
        start.Environment["CD_PROBE_COUNT"] = count.ToString( );
        start.Environment["CD_PROBE_OUTPUT"] = directory;
        var process = Process.Start( start );
        if ( process is null )
        {
            throw new InvalidOperationException( "Could not start synthetic QML fixture." );
        }

        return process;
    }

    static Task<string> Control( params string[] command ) => Run( "hyprctl", ["-i", instance, .. command] );

    static async Task<string> Run( string executable, string[] arguments )
    {
        var start = new ProcessStartInfo( executable ) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach ( var argument in arguments )
        {
            start.ArgumentList.Add( argument );
        }

        using var process = Process.Start( start );
        if ( process is null )
        {
            throw new InvalidOperationException( "Could not start " + executable );
        }

        var output = process.StandardOutput.ReadToEndAsync( );
        var error = process.StandardError.ReadToEndAsync( );
        using var deadline = new CancellationTokenSource( TimeSpan.FromSeconds( 5 ) );
        try
        {
            await process.WaitForExitAsync( deadline.Token );
        }
        catch ( OperationCanceledException )
        {
            process.Kill( entireProcessTree: true );
            await process.WaitForExitAsync( );
            throw;
        }

        if ( process.ExitCode != 0 )
        {
            throw new InvalidOperationException( await error );
        }

        return await output;
    }
}
