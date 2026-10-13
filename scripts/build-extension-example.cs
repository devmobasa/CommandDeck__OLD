#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:project ../src/CommandDeck.ExtensionTools/CommandDeck.ExtensionTools.csproj

using System.Diagnostics;
using System.Xml.Linq;
using CommandDeck.ExtensionTools;

if ( args.Length != 3 )
{
    throw new ArgumentException( "Usage: dotnet scripts/build-extension-example.cs SAMPLE_DIRECTORY FEED_DIRECTORY OUTPUT.cdext" );
}
var source = Path.GetFullPath( args[0] );
var feed = Path.GetFullPath( args[1] );
var output = Path.GetFullPath( args[2] );
if ( File.Exists( output ) )
{
    throw new IOException( "The output archive already exists." );
}
var root = Directory.CreateTempSubdirectory( "commanddeck-example-" ).FullName;
try
{
    foreach ( var file in Directory.EnumerateFiles( source, "*", SearchOption.AllDirectories ) )
    {
        var relative = Path.GetRelativePath( source, file );
        if ( relative.Split( Path.DirectorySeparatorChar ).Any( part => part is "bin" or "obj" or ".commanddeck" ) || relative == "packages.lock.json" )
        {
            continue;
        }
        var target = Path.Combine( root, relative );
        Directory.CreateDirectory( Path.GetDirectoryName( target )! );
        File.Copy( file, target );
    }
    await File.WriteAllTextAsync( Path.Combine( root, "global.json" ), "{\"sdk\":{\"version\":\"10.0.400\",\"rollForward\":\"disable\"}}" );
    new XDocument( new XElement( "configuration", new XElement( "packageSources", new XElement( "clear" ),
        new XElement( "add", new XAttribute( "key", "preview" ), new XAttribute( "value", feed ) ),
        new XElement( "add", new XAttribute( "key", "nuget" ), new XAttribute( "value", "https://api.nuget.org/v3/index.json" ) ) ),
        new XElement( "config", new XElement( "add", new XAttribute( "key", "globalPackagesFolder" ), new XAttribute( "value", Path.Combine( root, "packages" ) ) ) ) ) ).Save( Path.Combine( root, "NuGet.Config" ) );
    var publish = Path.Combine( root, "published" );
    var start = new ProcessStartInfo( "dotnet" ) { WorkingDirectory = root };
    foreach ( var value in new[] { "publish", AuthorProject.ProjectPath( root ), "-c", "Release", "-r", "linux-x64", "--self-contained", "true", "-p:PublishAot=true", "-o", publish } )
    {
        start.ArgumentList.Add( value );
    }
    using var process = Process.Start( start )!;
    try
    {
        await process.WaitForExitAsync( ).WaitAsync( TimeSpan.FromMinutes( 5 ) );
    }
    catch
    {
        process.Kill( true );
        await process.WaitForExitAsync( );
        throw;
    }
    if ( process.ExitCode != 0 )
    {
        throw new InvalidOperationException( "Native example build failed." );
    }
    ExtensionPackageBuilder.Pack( Path.Combine( root, "commanddeck.extension.json" ), publish, output );
    Console.WriteLine( "PASS .NET 10 external example build and package: " + output );
}
finally
{
    Directory.Delete( root, true );
}
