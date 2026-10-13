#!/usr/bin/env dotnet
#:property PublishAot=true
#:property RestorePackagesWithLockFile=false
#:property InvariantGlobalization=true
#:property AssemblyName=extension-hostile

using System.Diagnostics;

if ( args.Length > 0 )
{
    await Task.Delay( Timeout.Infinite );
    return;
}
// Each fixture leaves a descendant for the production cleanup watchdog to retire.
using var child = Process.Start( new ProcessStartInfo( Environment.ProcessPath! ) { ArgumentList = { "child" } } );
switch ( File.ReadAllText( "/extension/behavior" ) )
{
    case "crash":
        Environment.Exit( 42 );
        break;
    case "hang":
        await Task.Delay( Timeout.Infinite );
        break;
    case "malformed":
        Console.WriteLine( "not a protocol frame" );
        await Task.Delay( Timeout.Infinite );
        break;
    case "flood":
        var output = Console.OpenStandardOutput( );
        var bytes = new byte[65536];
        Array.Fill( bytes, ( byte ) 'x' );
        while ( true )
        {
            await output.WriteAsync( bytes );
        }
}
