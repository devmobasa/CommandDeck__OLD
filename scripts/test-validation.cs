#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

var directory = AppContext.GetData( "EntryPointFileDirectoryPath" ) as string;
if ( directory is null )
{
    throw new InvalidOperationException( "Script directory unavailable." );
}
var script = Path.Combine( directory, "validate.cs" );
var host = Path.GetFullPath( Path.Combine( RuntimeEnvironment.GetRuntimeDirectory( ), "../../..", "dotnet" ) );
var report = Path.Combine( directory, "../build/validation-profile.json" );
var original = File.Exists( report ) ? await File.ReadAllTextAsync( report ) : null;
var fixturePath = Directory.CreateTempSubdirectory( "commanddeck-validation-tools-" ).FullName;
var missingOmarchy = Path.Combine( fixturePath, "missing-omarchy-checkout" );
var omarchyFixture = Path.Combine( fixturePath, "omarchy-checkout" );
Directory.CreateDirectory( Path.Combine( omarchyFixture, "shell/Commons" ) );
await File.WriteAllTextAsync( Path.Combine( omarchyFixture, "shell/Commons/qmldir" ), "module qs.Commons\n" );
try
{
    await CheckAsync( "partial", false, false, 0 );
    await CheckAsync( "desktop", true, true, 1 );
    await RequireCheckAsync( "tool:qs", "failed" );
    await CheckAsync( "partial", true, true, 0, omarchyPath: missingOmarchy );
    await RequireCheckAsync( "tool:qs", "skipped" );
    await RequireCheckAsync( "qml-import:qs.Commons", "skipped" );
    await CheckAsync( "unknown", true, true, 2 );

    // The qml profile makes Quickshell, the Qt 6 tools, and the Omarchy shell
    // module that qmllint resolves mandatory, but not the Omarchy CLI.
    await CheckAsync( "qml", true, true, 1, omarchyPath: missingOmarchy );
    await RequireCheckAsync( "tool:qs", "failed" );
    await RequireCheckAsync( "tool:omarchy", "skipped" );
    await RequireCheckAsync( "qml-import:qs.Commons", "failed" );
    await CheckAsync( "qml", true, true, 1, omarchyPath: omarchyFixture );
    await RequireCheckAsync( "qml-import:qs.Commons", "passed" );
    await CheckAsync( "all", true, true, 1, omarchyPath: missingOmarchy );
    await RequireCheckAsync( "tool:omarchy", "failed" );
    await RequireCheckAsync( "qml-import:qs.Commons", "failed" );
    if ( !OperatingSystem.IsWindows( ) )
    {
        foreach ( var tool in new[] { "qs", "omarchy", "bwrap" } )
        {
            var path = Path.Combine( fixturePath, tool );
            await File.WriteAllTextAsync( path, "not an executable" );
            File.SetUnixFileMode( path, UnixFileMode.UserRead | UnixFileMode.UserWrite );
        }
        await CheckAsync( "extensions", true, false, 1, fixturePath );
        await RequireCheckAsync( "tool:bwrap", "failed" );
        await CheckAsync( "linux", true, false, 1, fixturePath );
        await RequireCheckAsync( "tool:bwrap", "failed" );
        await RequireCheckAsync( "native-rid", OperatingSystem.IsLinux( ) && RuntimeInformation.OSArchitecture == Architecture.X64 ? "passed" : "failed" );
        await CheckAsync( "desktop", true, false, 1, fixturePath );
        await RequireCheckAsync( "tool:qs", "failed" );
        await CheckAsync( "qml", true, false, 1, fixturePath, omarchyFixture );
        await RequireCheckAsync( "tool:qs", "failed" );
    }

    ProcessStartInfo compositionStart = new( host );
    compositionStart.ArgumentList.Add( Path.Combine( directory, "test-validation-composition.cs" ) );
    using var composition = Process.Start( compositionStart );
    if ( composition is null )
    {
        throw new InvalidOperationException( "Could not start validation composition regression." );
    }

    await composition.WaitForExitAsync( );
    if ( composition.ExitCode != 0 )
    {
        throw new InvalidOperationException( "Validation composition regression failed." );
    }

    Console.WriteLine( "PASS validation profiles distinguish required failures from explicit partial skips" );
    return 0;
}
finally
{
    Directory.Delete( fixturePath, recursive: true );
    if ( original is null )
    {
        File.Delete( report );
    }
    else
    {
        await File.WriteAllTextAsync( report, original );
    }
}

async Task CheckAsync( string profile, bool noBuild, bool missingPath, int expected, string? toolPath = null, string? omarchyPath = null )
{
    ProcessStartInfo start = new( host )
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    foreach ( var argument in new[] { "run", "--file", script } )
    {
        start.ArgumentList.Add( argument );
    }
    if ( noBuild )
    {
        start.ArgumentList.Add( "--no-build" );
    }
    start.ArgumentList.Add( "--" );
    start.ArgumentList.Add( profile );
    start.ArgumentList.Add( "--preflight-only" );
    if ( missingPath )
    {
        start.Environment["PATH"] = "/nonexistent-commanddeck-validation-fixture";
    }
    else if ( toolPath is not null )
    {
        start.Environment["PATH"] = toolPath;
    }
    if ( omarchyPath is not null )
    {
        start.Environment["OMARCHY_PATH"] = omarchyPath;
    }

    using var process = Process.Start( start );
    if ( process is null )
    {
        throw new InvalidOperationException( "Could not start validation fixture." );
    }
    var stdout = process.StandardOutput.ReadToEndAsync( );
    var stderr = process.StandardError.ReadToEndAsync( );
    await process.WaitForExitAsync( );
    var output = await stdout + await stderr;
    if ( process.ExitCode != expected )
    {
        throw new InvalidOperationException( $"Expected the {profile} profile to exit {expected}; got {process.ExitCode}: {output}" );
    }
}

async Task RequireCheckAsync( string name, string status )
{
    var profile = JsonNode.Parse( await File.ReadAllTextAsync( report ) );
    if ( profile is null )
    {
        throw new InvalidDataException( "The validation profile report is empty." );
    }

    var matched = profile["checks"]!.AsArray( ).Any( check => ( string? ) check!["name"] == name && ( string? ) check["status"] == status );
    if ( !matched )
    {
        throw new InvalidOperationException( $"The {profile["profile"]} profile report does not record {name} as {status}." );
    }
}
