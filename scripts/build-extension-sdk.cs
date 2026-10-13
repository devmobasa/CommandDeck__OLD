#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Text.Json;

if ( args.Length != 2 || args[0] != "--output" )
{
    throw new ArgumentException( "Usage: dotnet scripts/build-extension-sdk.cs --output DIRECTORY" );
}
var output = Path.GetFullPath( args[1] );
var repo = Path.GetFullPath( Path.Combine( AppContext.GetData( "EntryPointFileDirectoryPath" )!.ToString( )!, ".." ) );
var temporary = Directory.CreateTempSubdirectory( "commanddeck-sdk-" ).FullName;
try
{
    Directory.CreateDirectory( output );
    File.WriteAllText( Path.Combine( temporary, "global.json" ), JsonSerializer.Serialize( new
    {
        sdk = new
        {
            version = "10.0.400",
            rollForward = "disable"
        }
    } ) );
    File.Copy( Path.Combine( repo, "Directory.Build.props" ), Path.Combine( temporary, "Directory.Build.props" ) );
    File.Copy( Path.Combine( repo, "CodeMetricsConfig.txt" ), Path.Combine( temporary, "CodeMetricsConfig.txt" ) );
    var template = Path.Combine( temporary, "samples", "extensions", "LocalDraft" );
    Directory.CreateDirectory( template );
    foreach ( var name in new[] { "Program.cs", "LocalDraft.csproj", "commanddeck.extension.json" } )
    {
        File.Copy( Path.Combine( repo, "samples", "extensions", "LocalDraft", name ), Path.Combine( template, name ) );
    }
    foreach ( var name in new[] { "CommandDeck.ExtensionProtocol", "CommandDeck.Extensions", "CommandDeck.ExtensionTools", "CommandDeck.Extensions.Testing" } )
    {
        var source = Path.Combine( repo, "src", name );
        var target = Path.Combine( temporary, "src", name );
        foreach ( var file in Directory.EnumerateFiles( source, "*", SearchOption.AllDirectories ) )
        {
            var relative = Path.GetRelativePath( source, file );
            if ( relative.Split( Path.DirectorySeparatorChar ).Any( part => part is "bin" or "obj" ) )
            {
                continue;
            }
            var destination = Path.Combine( target, relative );
            Directory.CreateDirectory( Path.GetDirectoryName( destination )! );
            File.Copy( file, destination );
        }
        var start = new ProcessStartInfo( "dotnet" ) { WorkingDirectory = temporary };
        foreach ( var argument in new[] { "pack", target, "-c", "Release", "-o", output } )
        {
            start.ArgumentList.Add( argument );
        }
        using var process = Process.Start( start )!;
        using var deadline = new CancellationTokenSource( TimeSpan.FromMinutes( 5 ) );
        try
        {
            await process.WaitForExitAsync( deadline.Token );
        }
        catch
        {
            process.Kill( true );
            throw;
        }
        if ( process.ExitCode != 0 )
        {
            throw new InvalidOperationException( $"SDK pack failed: {process.ExitCode}" );
        }
    }
    Console.WriteLine( $"Local preview feed: {output}" );
}
finally
{
    Directory.Delete( temporary, true );
}
