#!/usr/bin/env dotnet
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Text.Json;

if ( !OperatingSystem.IsLinux( ) )
{
    Console.Error.WriteLine( "Native clipboard proof requires Linux." );
    return 2;
}

if ( args.Length != 1 )
{
    Console.Error.WriteLine( "Usage: dotnet run --file scripts/test-clipboard-native.cs -- /absolute/path/to/commanddeck" );
    return 2;
}

var executable = Path.GetFullPath( args[0] );
if ( !File.Exists( executable ) )
{
    throw new FileNotFoundException( "Build the commanddeck child before running native proof." );
}

var root = Directory.CreateTempSubdirectory( "commanddeck-native-clipboard-proof-" ).FullName;
File.SetUnixFileMode( root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
var socketName = "clipboard-proof";
using Process weston = new( );
weston.StartInfo = new( "weston" ) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
foreach ( var argument in new[] { "--backend=headless", "--renderer=noop", "--fake-seat", "--shell=kiosk", "--no-config", "--socket=" + socketName,
    "--idle-time=0", "--log=" + Path.Combine( root, "weston.log" ) } )
{
    weston.StartInfo.ArgumentList.Add( argument );
}

weston.StartInfo.Environment["XDG_RUNTIME_DIR"] = root;
weston.StartInfo.Environment.Remove( "WAYLAND_DISPLAY" );
weston.StartInfo.Environment.Remove( "DISPLAY" );
weston.StartInfo.Environment.Remove( "WAYLAND_SOCKET" );
using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 15 ) );
var started = false;
try
{
    started = weston.Start( );
    if ( !started ) throw new InvalidOperationException( "Headless Weston could not start." );
    var stdout = weston.StandardOutput.ReadToEndAsync( deadline.Token );
    var stderr = weston.StandardError.ReadToEndAsync( deadline.Token );
    while ( !File.Exists( Path.Combine( root, socketName ) ) && !weston.HasExited )
    {
        await Task.Delay( 20, deadline.Token );
    }

    if ( weston.HasExited )
    {
        Console.Error.WriteLine( await stderr );
        throw new InvalidOperationException( "Isolated headless compositor exited before creating its socket." );
    }

    using Process child = new( );
    child.StartInfo = new( executable ) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    child.StartInfo.ArgumentList.Add( "clipboard-host" );
    child.StartInfo.ArgumentList.Add( "--probe" );
    child.StartInfo.Environment["XDG_RUNTIME_DIR"] = root;
    child.StartInfo.Environment["WAYLAND_DISPLAY"] = socketName;
    child.StartInfo.Environment.Remove( "WAYLAND_SOCKET" );
    if ( !child.Start( ) ) throw new InvalidOperationException( "Clipboard child could not start." );
    try
    {
        var output = child.StandardOutput.ReadToEndAsync( deadline.Token );
        var errors = child.StandardError.ReadToEndAsync( deadline.Token );
        await child.WaitForExitAsync( deadline.Token );
        if ( child.ExitCode != 0 )
        {
            Console.Error.WriteLine( await errors );
            throw new InvalidOperationException( "Native clipboard registry probe failed." );
        }

        using var result = JsonDocument.Parse( await output );
        if ( !result.RootElement.GetProperty( "registryReady" ).GetBoolean( ) || result.RootElement.GetProperty( "seatCount" ).GetInt32( ) < 1 )
        {
            throw new InvalidOperationException( "Isolated registry did not expose the expected fake seat." );
        }

        Console.WriteLine( "PASS isolated native registry probe: " + result.RootElement.GetRawText( ) );
        Console.WriteLine( "Registry-only proof. No data device, clipboard offer, payload or installed desktop was accessed." );
    }
    finally
    {
        if ( !child.HasExited ) child.Kill( entireProcessTree: true );
        using CancellationTokenSource reap = new( TimeSpan.FromSeconds( 3 ) );
        await child.WaitForExitAsync( reap.Token );
    }

    if ( !weston.HasExited ) weston.Kill( entireProcessTree: true );
    await weston.WaitForExitAsync( deadline.Token );
    _ = await stdout;
    _ = await stderr;
    return 0;
}
finally
{
    if ( started )
    {
        if ( !weston.HasExited ) weston.Kill( entireProcessTree: true );
        using CancellationTokenSource reap = new( TimeSpan.FromSeconds( 3 ) );
        await weston.WaitForExitAsync( reap.Token );
    }

    Directory.Delete( root, recursive: true );
}
