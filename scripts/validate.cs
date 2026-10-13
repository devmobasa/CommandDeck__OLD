#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

return await new Validation( ).RunAsync( args );

sealed class Validation
{
    private readonly JsonArray _checks = [];
    private readonly JsonObject _report = new( );
    private bool _failed;
    private string _profile = "partial";
    private bool Desktop => _profile is "desktop" or "all";
    private bool Qml => _profile is "qml" || Desktop;
    private bool Extensions => _profile is "extensions" or "linux" or "all";
    private bool Aot => _profile is "aot" or "linux" or "all";

    public async Task<int> RunAsync( string[] arguments )
    {
        _profile = arguments.Length > 0 ? arguments[0] : "partial";
        if ( _profile is not ("partial" or "qml" or "desktop" or "extensions" or "aot" or "linux" or "all") || arguments.Length > 2 ||
            (arguments.Length == 2 && arguments[1] != "--preflight-only") )
        {
            Console.Error.WriteLine( "Usage: dotnet scripts/validate.cs [partial|qml|desktop|extensions|aot|linux|all] [--preflight-only]" );
            return 2;
        }

        var entry = AppContext.GetData( "EntryPointFileDirectoryPath" ) as string;
        if ( entry is null )
        {
            throw new InvalidOperationException( "Validation script directory unavailable." );
        }

        Directory.SetCurrentDirectory( Path.GetFullPath( Path.Combine( entry, ".." ) ) );
        Directory.CreateDirectory( "build" );
        _report["profile"] = _profile;
        _report["startedUtc"] = DateTimeOffset.UtcNow.ToString( "O" );
        _report["checks"] = _checks;
        Preflight( );
        if ( _failed || arguments.Contains( "--preflight-only", StringComparer.Ordinal ) )
        {
            return _failed ? 1 : 0;
        }

        await RunCheckAsync( "validation-profile-regression", "dotnet", "scripts/test-validation.cs" );
        if ( _failed )
        {
            return 1;
        }

        // The qml profile is the focused QML lane. The repository gate runs the
        // same QML script whenever Quickshell is present.
        if ( _profile == "qml" )
        {
            await RunCheckAsync( "qml-lint-and-specs", "bash", "scripts/test-qml-components.sh" );
            Record( "repository", "skipped", "not selected; the qml profile runs only QML lint, component, and event specs" );
        }
        else
        {
            await RunCheckAsync( "repository", "bash", "scripts/validate-repository.sh" );
        }

        if ( _failed )
        {
            return 1;
        }

        if ( Extensions )
        {
            await RunCheckAsync( "author-test-kit", "dotnet", "scripts/test-extension-testing.cs" );
            await RunCheckAsync( "independent-sdk-consumer", "dotnet", "scripts/test-extension-sdk.cs" );
            await RunCheckAsync( "host-child-isolation", "dotnet", "scripts/test-extension-sandbox.cs" );
        }
        else
        {
            Record( "extensions", "skipped", "not selected; required for extension wire, SDK, or host changes" );
        }

        if ( Aot )
        {
            await RunCheckAsync( "native-aot-publish-and-smoke", "bash", "scripts/publish-aot.sh", "linux-x64" );
            if ( !_failed )
            {
                await RunCheckAsync( "native-aot-index-watchers", "dotnet", "run",
                    "--project", "tests/CommandDeck.Specs", "--configuration", "Release", "--no-build", "--",
                    "--native-index-aot", "artifacts/publish/linux-x64/commanddeck" );
            }
            if ( !_failed )
            {
                await RunCheckAsync( "native-aot-clipboard-idle-and-lifecycle", "dotnet", "run",
                    "--project", "tests/CommandDeck.Specs", "--configuration", "Release", "--no-build", "--",
                    "--clipboard-native-aot", "artifacts/publish/linux-x64/commanddeck" );
            }
        }
        else
        {
            Record( "native-aot", "skipped", "not selected; required for AOT-sensitive changes" );
        }

        Record( "installed-desktop-and-helper", "skipped", "Installed Wayland, Polkit, upgrade, and rollback remain separate acceptance environments." );
        return _failed ? 1 : 0;
    }

    private void Preflight( )
    {
        // The QML suites and qmllint need Quickshell and the Qt 6 tools; only
        // plugin validation in the desktop profile needs the Omarchy CLI.
        (string Tool, bool Required)[] tools =
        [
            ("qs", Qml), ("omarchy", Desktop), ("/usr/lib/qt6/bin/qmllint", Qml), ("/usr/lib/qt6/bin/qmltestrunner", Qml)
        ];
        foreach ( var (tool, required) in tools )
        {
            RecordTool( "tool:" + tool, Available( tool ), required, null );
        }

        // qmllint resolves the plugin's qs.Commons import from the Omarchy shell.
        var omarchy = Environment.GetEnvironmentVariable( "OMARCHY_PATH" ) is { Length: > 0 } path ? path : "/usr/share/omarchy";
        RecordTool( "qml-import:qs.Commons", File.Exists( Path.Combine( omarchy, "shell/Commons/qmldir" ) ), Qml,
            $"looked in {omarchy}/shell; set OMARCHY_PATH to an Omarchy checkout" );

        if ( Extensions )
        {
            Record( "tool:bwrap", Available( "bwrap" ) ? "passed" : "failed", "required for host/child isolation" );
        }
        if ( Aot )
        {
            Record( "native-rid", OperatingSystem.IsLinux( ) && RuntimeInformation.OSArchitecture == Architecture.X64 ? "passed" : "failed",
                "This profile publishes and smokes linux-x64 on a matching native host." );
        }
    }

    private void RecordTool( string name, bool available, bool required, string? hint )
    {
        if ( available )
        {
            Record( name, "passed", "available" );
            return;
        }

        var suffix = hint is null ? "" : $" ({hint})";
        Record( name, required ? "failed" : "skipped", required
            ? $"required by the {_profile} profile but missing{suffix}"
            : $"desktop check unavailable in this {_profile} profile{suffix}" );
    }

    private static bool Available( string command ) => command.Contains( '/' ) ? IsExecutable( command ) :
        (Environment.GetEnvironmentVariable( "PATH" ) ?? "").Split( Path.PathSeparator )
            .Any( directory => IsExecutable( Path.Combine( directory, command ) ) );

    private static bool IsExecutable( string path )
    {
        if ( !File.Exists( path ) )
        {
            return false;
        }

        try
        {
            return OperatingSystem.IsWindows( ) || (File.GetUnixFileMode( path ) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException )
        {
            return false;
        }
    }

    private void Record( string name, string status, string reason )
    {
        _checks.Add( new JsonObject { ["name"] = name, ["status"] = status, ["reason"] = reason } );
        Console.WriteLine( $"{status.ToUpperInvariant( )} {name}: {reason}" );
        _failed |= status == "failed";
        File.WriteAllText( "build/validation-profile.json", _report.ToJsonString( new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
    }

    private async Task RunCheckAsync( string name, string executable, params string[] arguments )
    {
        ProcessStartInfo start = new( executable );
        foreach ( var argument in arguments )
        {
            start.ArgumentList.Add( argument );
        }

        if ( name == "repository" && Desktop )
        {
            start.Environment["COMMANDDECK_REQUIRE_NO_SKIPS"] = "1";
        }

        try
        {
            using var process = Process.Start( start );
            if ( process is null )
            {
                throw new InvalidOperationException( "Process did not start." );
            }

            await process.WaitForExitAsync( );
            Record( name, process.ExitCode == 0 ? "passed" : "failed", $"exit code {process.ExitCode}" );
        }
        catch ( Exception error )
        {
            Record( name, "failed", error.Message );
        }
    }
}
