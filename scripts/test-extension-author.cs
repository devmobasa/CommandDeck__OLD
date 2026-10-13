#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Text.Json.Nodes;

if ( args.Length is not (2 or 3) )
{
    throw new ArgumentException( "Usage: dotnet scripts/test-extension-author.cs HOST FEED [STALE_SDK_FEED]" );
}
var host = Path.GetFullPath( args[0] );
var feed = Path.GetFullPath( args[1] );
var repo = Path.GetFullPath( Path.Combine( AppContext.GetData( "EntryPointFileDirectoryPath" )!.ToString( )!, ".." ) );
var root = Directory.CreateTempSubdirectory( "commanddeck-author-" ).FullName;
var project = Path.Combine( root, "my draft" );
var toolDirectory = Path.Combine( root, "tools" );
var tool = Path.Combine( toolDirectory, "commanddeck-ext" );
var archive = Path.Combine( root, "draft.cdext" );
var config = Path.Combine( root, "config.json" );
var socket = Path.Combine( root, "daemon.sock" );
var log = new System.Text.StringBuilder( );
Process? daemon = null;
Task<string>? daemonOutput = null, daemonError = null;
try
{
    // Isolate both the SDK choice and the package cache from the checkout/user.
    await File.WriteAllTextAsync( Path.Combine( root, "global.json" ), "{\"sdk\":{\"version\":\"10.0.400\",\"rollForward\":\"disable\"}}" );
    await Exec( "dotnet", "tool", "install", "CommandDeck.ExtensionTools", "--version", "0.1.0-preview.6", "--tool-path", toolDirectory, "--add-source", feed );
    await Exec( tool, "new", "--directory", project, "--id", "org.example.author-draft", "--name", "Author draft", "--feed", feed );
    await Reject( tool, "new", "--directory", project, "--id", "org.example.other", "--name", "Other", "--feed", feed );
    await Exec( tool, "check", "--directory", project );
    if ( args.Length == 3 )
    {
        var warmup = Path.Combine( root, "WarmCache.csproj" );
        await File.WriteAllTextAsync( warmup, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"CommandDeck.Extensions\" Version=\"0.1.0-preview.1\" /></ItemGroup></Project>" );
        await Exec( "dotnet", "restore", warmup, "--source", Path.GetFullPath( args[2] ), "--source", "https://api.nuget.org/v3/index.json" );
    }
    // Also cover authors invoking dotnet directly, with the same ambient cache.
    await Exec( "dotnet", "build", Path.Combine( project, "LocalDraft.csproj" ), "-c", "Release" );
    await Exec( tool, "pack", "--directory", project, "--output", archive );
    await Reject( tool, "pack", "--directory", project, "--output", archive );
    await File.WriteAllTextAsync( config, new JsonObject
    {
        ["storage"] = new JsonObject { ["databasePath"] = Path.Combine( root, "catalog.db" ) },
        ["indexing"] = new JsonObject { ["roots"] = new JsonArray( ), ["indexDesktopApplications"] = false, ["indexOmarchyCommands"] = false },
        ["ai"] = new JsonObject { ["enabled"] = false },
        ["agent"] = new JsonObject { ["runtimeDirectory"] = Path.Combine( root, "runtime" ), ["stateDirectory"] = Path.Combine( root, "state" ) }
    }.ToJsonString( ) );
    daemon = Process.Start( Start( host, "daemon", "--config", config, "--socket", socket ) )!;
    daemonOutput = daemon.StandardOutput.ReadToEndAsync( );
    daemonError = daemon.StandardError.ReadToEndAsync( );
    using ( var deadline = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) ) )
    {
        while ( !File.Exists( socket ) )
        {
            if ( daemon.HasExited )
            {
                throw new InvalidOperationException( "Private daemon exited" );
            }
            await Task.Delay( 25, deadline.Token );
        }
    }
    async Task<JsonNode> Inventory( params string[] values ) => JsonNode.Parse( await Exec( host, ["extensions", .. values, "--json", "--socket", socket] ) )!;
    var inspected = await Inventory( "inspect", "--archive", archive );
    var digest = inspected["archiveDigest"]!.GetValue<string>( );
    await Reject( tool, "update", "--host", host, "--archive", archive, "--digest", digest, "--socket", socket );
    await Reject( tool, "update", "--host", host, "--archive", archive, "--digest", new string( '0', 64 ), "--socket", socket, "--yes" );
    await Exec( tool, "update", "--host", host, "--archive", archive, "--digest", digest, "--allow", "storage.read,storage.write", "--socket", socket, "--yes" );
    var installed = await Inventory( "inspect", "org.example.author-draft" );
    AuthorCheck.That( !installed["packages"]![0]!["enabled"]!.GetValue<bool>( ), "Update enabled a new package" );
    var settings = Path.Combine( root, "settings.json" );
    await File.WriteAllTextAsync( settings, "{\"defaultTitle\":\"Author title\"}" );
    var revision = installed["revision"]!.ToString( );
    await Exec( host, "extensions", "configure", "org.example.author-draft", "--settings-file", settings, "--expected-revision", revision, "--socket", socket );
    await Reject( host, "extensions", "configure", "org.example.author-draft", "--settings-file", settings, "--expected-revision", revision, "--socket", socket );
    var manifestPath = Path.Combine( project, "commanddeck.extension.json" );
    var manifest = JsonNode.Parse( await File.ReadAllTextAsync( manifestPath ) )!;
    manifest["version"] = "0.1.1";
    await File.WriteAllTextAsync( manifestPath, manifest.ToJsonString( ) );
    // A source edit must reach the rebuilt binary, not a stale publish directory.
    var source = Path.Combine( project, "Program.cs" );
    await File.WriteAllTextAsync( source, (await File.ReadAllTextAsync( source )).Replace( "Title = \"Local draft\"", "Title = \"Updated local draft\"" ) );
    var updatedArchive = Path.Combine( root, "draft-update.cdext" );
    await Exec( tool, "build", "--directory", project );
    await Exec( tool, "pack", "--directory", project, "--output", updatedArchive );
    var update = await Inventory( "inspect", "--archive", updatedArchive );
    await Exec( tool, "update", "--host", host, "--archive", updatedArchive, "--digest", update["archiveDigest"]!.GetValue<string>( ), "--allow", "storage.read,storage.write", "--socket", socket, "--yes" );
    var replaced = await Inventory( "inspect", "org.example.author-draft" );
    AuthorCheck.That( replaced["packages"]![0]!["version"]!.GetValue<string>( ) == "0.1.1", "Update did not replace the package" );
    AuthorCheck.That( replaced["settings"]!["defaultTitle"]!.GetValue<string>( ) == "Author title", "Update lost settings" );
    await Exec( host, "extensions", "rollback", "org.example.author-draft", "--digest", digest, "--yes", "--socket", socket );
    var rolledBack = await Inventory( "inspect", "org.example.author-draft" );
    AuthorCheck.That( rolledBack["packages"]![0]!["version"]!.GetValue<string>( ) == "0.1.0", "Rollback did not restore the package" );
    AuthorCheck.That( rolledBack["settings"]!["defaultTitle"]!.GetValue<string>( ) == "Author title", "Rollback lost settings" );
    // The external author's final archive also runs through the release sandbox journey.
    await Exec( "dotnet", Path.Combine( repo, "scripts/test-extension-draft.cs" ), host, updatedArchive, "--expected-title", "Updated local draft" );
    var developmentArchive = Path.Combine( root, "development.cdext" );
    await Exec( tool, "dev", "--directory", project, "--output", developmentArchive );
    var development = await Inventory( "inspect", "--archive", developmentArchive );
    await Exec( tool, "attach", "--host", host, "--archive", developmentArchive, "--digest", development["archiveDigest"]!.GetValue<string>( ), "--allow", "storage.read,storage.write", "--socket", socket, "--yes" );
    var developmentId = "org.example.author-draft.development";
    var firstDevelopment = await Inventory( "inspect", developmentId );
    var validSource = await File.ReadAllTextAsync( source );
    var failedArchive = Path.Combine( root, "failed-development.cdext" );
    try
    {
        await File.WriteAllTextAsync( source, validSource + "\nThis is deliberately invalid C#\n" );
        await Reject( tool, "dev", "--directory", project, "--output", failedArchive );
        AuthorCheck.That( !File.Exists( failedArchive ), "Failed development build produced an archive" );
    }
    finally
    {
        await File.WriteAllTextAsync( source, validSource );
    }
    var developmentRevision = firstDevelopment["revision"]!.ToString( );
    await Exec( host, "extensions", "configure", developmentId, "--settings-file", settings, "--expected-revision", developmentRevision, "--socket", socket );
    var reloadedArchive = Path.Combine( root, "development-reload.cdext" );
    await Exec( tool, "dev", "--directory", project, "--output", reloadedArchive );
    var reload = await Inventory( "inspect", "--archive", reloadedArchive );
    await Exec( tool, "reload", "--host", host, "--archive", reloadedArchive, "--digest", reload["archiveDigest"]!.GetValue<string>( ), "--socket", socket, "--yes" );
    var currentDevelopment = await Inventory( "inspect", developmentId );
    AuthorCheck.That( currentDevelopment["settings"]!["defaultTitle"]!.GetValue<string>( ) == "Author title", "Development reload discarded settings" );
    var productionAfterReload = await Inventory( "inspect", "org.example.author-draft" );
    AuthorCheck.That( productionAfterReload["packages"]!.AsArray( ).Single( n => n!["id"]!.GetValue<string>( ) == "org.example.author-draft" )!["digest"]!.GetValue<string>( ) == digest, "Development reload replaced production" );
    await Exec( "dotnet", Path.Combine( repo, "scripts/test-extension-development.cs" ), host, reloadedArchive );
    Console.WriteLine( "PASS author workflow: isolated tool install, template, check, Native AOT build/pack, reviewed install/update, stale CLI settings rejection, preserved settings/rollback, release-sandbox Local Draft, failed development build, attachment/reload and native debugging." );

    ProcessStartInfo Start( string executable, params string[] values )
    {
        var start = new ProcessStartInfo( executable ) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NUGET_PACKAGES"] = Path.Combine( root, "packages" );
        foreach ( var value in values )
        {
            start.ArgumentList.Add( value );
        }
        return start;
    }
    async Task<string> Run( bool success, string executable, string[] values )
    {
        using var child = Process.Start( Start( executable, values ) )!;
        var stdout = child.StandardOutput.ReadToEndAsync( );
        var stderr = child.StandardError.ReadToEndAsync( );
        using var deadline = new CancellationTokenSource( TimeSpan.FromMinutes( 8 ) );
        try
        {
            await child.WaitForExitAsync( deadline.Token );
        }
        catch
        {
            if ( !child.HasExited )
            {
                child.Kill( true );
            }
            throw;
        }
        var output = await stdout;
        var error = await stderr;
        log.AppendLine( executable + " " + string.Join( ' ', values ) ).Append( output ).Append( error );
        AuthorCheck.That( (child.ExitCode == 0) == success, output + error );
        Console.WriteLine( (success ? "PASS " : "PASS rejected ") + Path.GetFileName( executable ) + " " + values[0] );
        return output;
    }
    Task<string> Exec( string executable, params string[] values ) => Run( true, executable, values );
    Task<string> Reject( string executable, params string[] values ) => Run( false, executable, values );
}
finally
{
    if ( daemon is not null )
    {
        if ( !daemon.HasExited )
        {
            daemon.Kill( true );
            await daemon.WaitForExitAsync( );
        }
        log.Append( await daemonOutput! ).Append( await daemonError! );
        daemon.Dispose( );
    }
    var logPath = Path.Combine( repo, "artifacts/extensions/author-tools/journey.log" );
    Directory.CreateDirectory( Path.GetDirectoryName( logPath )! );
    await File.WriteAllTextAsync( logPath, log.ToString( ) );
    Directory.Delete( root, true );
}

static class AuthorCheck
{
    public static void That( bool value, string message )
    {
        if ( !value )
        {
            throw new InvalidOperationException( message );
        }
    }
}
