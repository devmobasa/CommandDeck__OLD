#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;

if ( !OperatingSystem.IsLinux( ) || args.Length != 1 )
{
    Console.Error.WriteLine( "Usage: dotnet scripts/probe-workspace-host.cs <omarchy-source-checkout> (Linux)" );
    return 2;
}

var hostRoot = Path.GetFullPath( args[0] );
var repo = Directory.GetCurrentDirectory( );
var temporary = Path.Combine( Path.GetTempPath( ), "cd-host-" + Guid.NewGuid( ).ToString( "N" )[..12] );
Directory.CreateDirectory( temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
try
{
    foreach ( var file in Directory.EnumerateFiles( Path.Combine( repo, "qml", "common" ) ) )
    {
        if ( Path.GetExtension( file ) is ".qml" or ".js" )
        {
            File.Copy( file, Path.Combine( temporary, Path.GetFileName( file ) ) );
        }
    }

    foreach ( var name in new[] { "AuthServiceStore.js", "PluginShellApi.qml" } )
    {
        File.Copy( Path.Combine( hostRoot, "shell", "services", name ), Path.Combine( temporary, name ) );
    }

    File.Copy( Path.Combine( repo, "tests", "CommandDeck.CaptureProbe", "HostLifecycle.qml" ), Path.Combine( temporary, "shell.qml" ) );
    Directory.CreateDirectory( Path.Combine( temporary, "hypr", "host-probe" ) );
    var start = new ProcessStartInfo( "qs" ) { RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add( "-p" );
    start.ArgumentList.Add( temporary );
    start.Environment["QT_QPA_PLATFORM"] = "offscreen";
    start.Environment["XDG_RUNTIME_DIR"] = temporary;
    start.Environment["HYPRLAND_INSTANCE_SIGNATURE"] = "host-probe";
    using var process = Process.Start( start );
    if ( process is null )
    {
        throw new InvalidOperationException( "Could not start the isolated lifecycle fixture." );
    }

    var stdout = process.StandardOutput.ReadToEndAsync( );
    var stderr = process.StandardError.ReadToEndAsync( );
    using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
    try
    {
        await process.WaitForExitAsync( timeout.Token );
    }
    catch ( OperationCanceledException )
    {
        process.Kill( entireProcessTree: true );
        await process.WaitForExitAsync( );
        throw;
    }

    var output = await stdout + await stderr;
    Console.WriteLine( output );
    return process.ExitCode == 0 && output.Contains( "HOST_CAPTURE_INTEGRATION_PASS", StringComparison.Ordinal )
        && !output.Contains( "HOST_CAPTURE_INTEGRATION_FAIL", StringComparison.Ordinal ) ? 0 : 1;
}
finally
{
    Directory.Delete( temporary, recursive: true );
}
