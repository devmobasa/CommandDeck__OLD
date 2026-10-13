#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:property AssemblyName=wl-copy

using System.Diagnostics;

if ( args.Contains( "--background", StringComparer.Ordinal ) )
{
    await Task.Delay( 4000 );
    return;
}

// Private acceptance sink: never changes the user's clipboard.
var directory = Environment.GetEnvironmentVariable( "COMMANDDECK_TEST_EFFECTS" );
if ( directory is null )
{
    throw new InvalidOperationException( "Missing private test directory." );
}

if ( args.Length > 0 )
{
    // Also stand in for uwsm-app/xdg-open in the private URL-action journey.
    var target = args[^1];
    if ( !target.StartsWith( "https://", StringComparison.Ordinal ) )
    {
        throw new InvalidOperationException( "Unexpected URL sink input." );
    }
    await File.AppendAllTextAsync( Path.Combine( directory, "opened-urls" ), target + "\n" );
    return;
}
var text = await Console.In.ReadToEndAsync( );
await File.WriteAllTextAsync( Path.Combine( directory, "copy-started" ), "" );
while ( File.Exists( Path.Combine( directory, "hold-copy" ) ) )
{
    await Task.Delay( 10 );
}
await File.WriteAllTextAsync( Path.Combine( directory, "clipboard" ), text );
await File.AppendAllTextAsync( Path.Combine( directory, "copied" ), text + "\n" );
// Model wl-copy's persistent owner retaining inherited stderr after its parent exits.
using var owner = Process.Start( new ProcessStartInfo( Environment.ProcessPath! ) { ArgumentList = { "--background" } } );
