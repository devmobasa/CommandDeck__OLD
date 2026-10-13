#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
// Exercise a real daemon with isolated desktop fixtures; no user clipboard or apps are accessed.
// Usage: dotnet tests/workflows-smoke.cs /absolute/path/to/commanddeck

using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if ( args.Length >= 2 && args[0] == "--fixture" )
{
    await WorkflowSmoke.RunFixtureAsync( args[1], args[2..] );
    return;
}
if ( args.Length != 1 )
{
    throw new ArgumentException( "Usage: dotnet tests/workflows-smoke.cs /path/to/commanddeck" );
}

var binary = Path.GetFullPath( args[0] );
var root = Path.Combine( Path.GetTempPath( ), $"cd-workflows-{Guid.NewGuid( ):N}" );
Directory.CreateDirectory( root );
try
{
    await WorkflowSmoke.SmokeAsync( binary, root );
}
finally
{
    Directory.Delete( root, recursive: true );
}

static class WorkflowSmoke
{
    private static readonly string[] EchoArguments = ["%s", "{value}"];

    public static async Task SmokeAsync( string binary, string root )
    {
        var tools = Path.Combine( root, "bin" );
        Directory.CreateDirectory( tools );
        var clipboard = Path.Combine( root, "copied" );
        var opened = Path.Combine( root, "opened" );
        Dictionary<string, string> environment = new( )
        {
            ["PATH"] = tools + Path.PathSeparator + Environment.GetEnvironmentVariable( "PATH" ),
            ["XDG_CONFIG_HOME"] = Path.Combine( root, "config" ),
            ["XDG_STATE_HOME"] = Path.Combine( root, "state" ),
            ["XDG_CACHE_HOME"] = Path.Combine( root, "cache" ),
            ["XDG_RUNTIME_DIR"] = Path.Combine( root, "runtime" ),
            ["COMMANDDECK_SMOKE_ROOT"] = root,
            ["COMMANDDECK_HEY"] = "off"
        };
        Directory.CreateDirectory( environment["XDG_RUNTIME_DIR"] );
        foreach ( var name in new[] { "wl-copy", "xdg-open", "uwsm-app", "qs" } )
        {
            WriteFixtureLauncher( Path.Combine( tools, name ), name );
        }

        var project = Path.Combine( root, "project checkout" );
        Directory.CreateDirectory( project );
        var git = await RunProcessAsync( "git", ["init", "-b", "workflow-test", project], environment );
        Require( git.ExitCode == 0, git.Error );
        var first = Path.Combine( project, "one file.txt" );
        var second = Path.Combine( project, "two 'quoted'.txt" );
        await File.WriteAllTextAsync( first, "first" );
        await File.WriteAllTextAsync( second, "second" );
        var scripts = Path.Combine( root, "scripts" );
        Directory.CreateDirectory( scripts );
        await File.WriteAllTextAsync( Path.Combine( scripts, "echo.commanddeck.json" ), JsonSerializer.Serialize( new
        {
            title = "Echo smoke argument", description = "Isolated argv fixture",
            executable = "/usr/bin/printf", arguments = EchoArguments,
            inputs = new[] { new { name = "value" } }
        } ) );
        var config = Path.Combine( root, "config.json" );
        await File.WriteAllTextAsync( config, JsonSerializer.Serialize( new
        {
            storage = new { databasePath = Path.Combine( root, "catalog.db" ) },
            indexing = new { roots = Array.Empty<string>( ), indexDesktopApplications = false, indexOmarchyCommands = false },
            privacy = new { storeUsageHistory = false, storeQueryHistory = false, neverIndex = Array.Empty<string>( ) },
            ai = new { enabled = false, providers = Array.Empty<string>( ) },
            agent = new { enabled = false },
            projects = new[] { new { title = "Workflow project", path = project } },
            snippets = new[] { new { id = "hello", title = "Hello smoke", text = "Hello {name}" } },
            scriptDirectories = new[] { scripts }
        } ) );

        await using var wayland = await StartWaylandFixtureAsync( environment );
        var socketPath = Path.Combine( root, "daemon.sock" );
        using var daemon = StartProcess( binary, ["daemon", "--config", config, "--socket", socketPath], environment );
        var output = daemon.StandardOutput.ReadToEndAsync( );
        var error = daemon.StandardError.ReadToEndAsync( );
        try
        {
            var deadline = Stopwatch.StartNew( );
            while ( !File.Exists( socketPath ) )
            {
                Require( !daemon.HasExited, daemon.HasExited ? await error : "Daemon exited" );
                Require( deadline.Elapsed < TimeSpan.FromSeconds( 15 ), "Daemon did not create its socket" );
                await Task.Delay( 30 );
            }
            using Socket socket = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
            await socket.ConnectAsync( new UnixDomainSocketEndPoint( socketPath ) );
            using NetworkStream stream = new( socket );
            using StreamReader reader = new( stream, Encoding.UTF8, leaveOpen: true );
            await using StreamWriter writer = new( stream, new UTF8Encoding( false ), leaveOpen: true ) { AutoFlush = true };
            SmokeClient client = new( reader, writer );
            await CheckWorkflowsAsync( client, wayland, binary, environment, project, first, second, clipboard, opened );
        }
        finally
        {
            if ( !daemon.HasExited )
            {
                using var signal = Process.Start( new ProcessStartInfo( "/usr/bin/kill" )
                {
                    ArgumentList = { "-TERM", daemon.Id.ToString( ) }, UseShellExecute = false
                } );
                using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 5 ) );
                try
                {
                    await daemon.WaitForExitAsync( timeout.Token );
                }
                catch ( OperationCanceledException )
                {
                    daemon.Kill( entireProcessTree: true );
                    await daemon.WaitForExitAsync( );
                }
            }
            var log = await output + await error;
            Require( daemon.ExitCode == 0, "Daemon did not shut down gracefully: " + log );
        }
    }

    static bool Succeeded( JsonObject outcome ) => ( bool? ) outcome["succeeded"] == true;
    static string Describe( IEnumerable<JsonObject> messages ) => string.Join( '\n', messages.Select( message => message.ToJsonString( ) ) );

    static void Require( bool condition, string message )
    {
        if ( !condition )
        {
            throw new InvalidOperationException( message );
        }
    }

    static Process StartProcess( string binary, IEnumerable<string> arguments, Dictionary<string, string> environment )
    {
        ProcessStartInfo start = new( binary )
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach ( var argument in arguments )
        {
            start.ArgumentList.Add( argument );
        }
        foreach ( var (name, value) in environment )
        {
            start.Environment[name] = value;
        }
        var process = Process.Start( start );
        if ( process is null )
        {
            throw new InvalidOperationException( "Could not start " + binary );
        }

        return process;
    }

    static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string binary, IEnumerable<string> arguments, Dictionary<string, string> environment, string? input = null, int timeoutSeconds = 10 )
    {
        using var process = StartProcess( binary, arguments, environment );
        var output = process.StandardOutput.ReadToEndAsync( );
        var error = process.StandardError.ReadToEndAsync( );
        if ( input is not null )
        {
            await process.StandardInput.WriteAsync( input );
        }
        process.StandardInput.Close( );
        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( timeoutSeconds ) );
        try
        {
            await process.WaitForExitAsync( timeout.Token );
        }
        catch
        {
            if ( !process.HasExited )
            {
                process.Kill( entireProcessTree: true );
                await process.WaitForExitAsync( );
            }
            throw;
        }

        return (process.ExitCode, await output, await error);
    }

    static void WriteFixtureLauncher( string path, string name )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new PlatformNotSupportedException( "Workflow smoke fixtures require Linux." );
        }

        var executable = Environment.ProcessPath;
        if ( executable is null )
        {
            throw new InvalidOperationException( "The smoke fixture executable is unavailable" );
        }

        static string Quote( string value ) => "'" + value.Replace( "'", "'\"'\"'", StringComparison.Ordinal ) + "'";
        var command = Quote( executable );
        if ( Path.GetFileNameWithoutExtension( executable ) == "dotnet" )
        {
            command += " " + Quote( Assembly.GetExecutingAssembly( ).Location );
        }
        File.WriteAllText( path, $"#!/bin/sh\nexec {command} --fixture {Quote( name )} \"$@\"\n" );
        File.SetUnixFileMode( path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
    }

    public static async Task RunFixtureAsync( string name, string[] arguments )
    {
        var root = Environment.GetEnvironmentVariable( "COMMANDDECK_SMOKE_ROOT" );
        if ( root is null )
        {
            throw new InvalidOperationException( "The smoke fixture directory is unavailable" );
        }

        switch ( name )
        {
            case "wl-copy":
                await using ( var file = File.Create( Path.Combine( root, "copied" ) ) )
                {
                    await Console.OpenStandardInput( ).CopyToAsync( file );
                }
                break;
            case "xdg-open":
                await File.AppendAllTextAsync( Path.Combine( root, "opened" ), JsonSerializer.Serialize( arguments ) + "\n" );
                break;
            case "qs":
                var pickerOutput = Environment.GetEnvironmentVariable( "COMMANDDECK_PICKER_OUTPUT" );
                if ( pickerOutput is null )
                {
                    throw new InvalidOperationException( "The picker fixture output is unavailable" );
                }

                await File.WriteAllTextAsync( pickerOutput, "1" );
                Console.WriteLine( "ignored UI log" );
                break;
            case "uwsm-app":
                var separator = Array.IndexOf( arguments, "--" );
                Require( separator >= 0 && separator + 1 < arguments.Length, "Invalid uwsm-app fixture arguments" );
                ProcessStartInfo start = new( arguments[separator + 1] ) { UseShellExecute = false };
                foreach ( var argument in arguments[(separator + 2)..] )
                {
                    start.ArgumentList.Add( argument );
                }
                using ( var child = Process.Start( start ) )
                {
                    if ( child is null )
                    {
                        throw new InvalidOperationException( "Could not start the desktop fixture" );
                    }

                    await child.WaitForExitAsync( );
                    Environment.ExitCode = child.ExitCode;
                }
                break;
            default:
                throw new ArgumentException( "Unknown fixture: " + name );
        }
    }

    private static async Task CheckWorkflowsAsync( SmokeClient client, WaylandFixture wayland, string binary,
        Dictionary<string, string> environment, string project, string first, string second, string clipboard, string opened )
    {
        var deadline = Stopwatch.StartNew( );
        var row = (await client.SearchAsync( "proj: Workflow" ))[0];
        Require( ( string? ) row["subtitle"] == project && (( string? ) row["detailMarkdown"])!.Contains( "workflow-test", StringComparison.Ordinal ), "Project checkout and Git status" );
        Console.WriteLine( "PASS project checkout and Git status" );

        row = (await client.SearchAsync( "snip: Hello" ))[0];
        var outcome = await client.InvokeAsync( row, "snippet-preview", new JsonObject { ["inputValues"] = new JsonObject { ["name"] = "Ada {date}" } } );
        Require( Succeeded( outcome ) && ( string? ) outcome["detailMarkdown"] == "Hello Ada {date}", "Snippet preview" );
        Require( Succeeded( await client.InvokeAsync( row, "snippet-copy", new JsonObject { ["inputValues"] = new JsonObject { ["name"] = "Ada" } } ) ), "Snippet copy" );
        Require( await File.ReadAllTextAsync( clipboard ) == "Hello Ada", "Snippet clipboard contents" );
        Console.WriteLine( "PASS snippet inputs, preview, and copy" );

        row = (await client.SearchAsync( "script: Echo" ))[0];
        Require( !Succeeded( await client.InvokeAsync( row, "run", new JsonObject { ["inputValues"] = new JsonObject { ["value"] = "blocked" } } ) ), "Script requires confirmation" );
        outcome = await client.InvokeAsync( row, "run", new JsonObject { ["confirmed"] = true, ["inputValues"] = new JsonObject { ["value"] = "literal $(not-a-command)" } } );
        Require( Succeeded( outcome ) && ( string? ) outcome["detailMarkdown"] == "literal $(not-a-command)", "Literal script output" );
        Console.WriteLine( "PASS confirmed scripts and literal output" );

        var controls = (await client.SearchAsync( "clip:" )).First( value => ( string? ) value["id"] == "clipboard:controls" );
        Require( (( string? ) controls["title"])!.Contains( "off", StringComparison.Ordinal ), "Clipboard starts off" );
        Require( Succeeded( await client.InvokeAsync( controls, "enable", new JsonObject { ["confirmed"] = true } ) ), "Enable clipboard capture" );
        deadline.Restart( );
        do
        {
            controls = (await client.SearchAsync( "clip:" )).First( value => ( string? ) value["id"] == "clipboard:controls" );
            Require( deadline.Elapsed < TimeSpan.FromSeconds( 5 ), "Native clipboard capture did not start: " + controls.ToJsonString( ) );
            await Task.Delay( 50 );
        } while ( ( string? ) controls["subtitle"] != "Capturing text" );
        Require( await wayland.CommandAsync( "raw" ) == "sent", "Synthetic clipboard selection" );

        deadline.Restart( );
        List<JsonObject> entries;
        do
        {
            entries = (await client.SearchAsync( "clip: synthetic" )).Where( value => ( string? ) value["id"] != "clipboard:controls" ).ToList( );
            Require( deadline.Elapsed < TimeSpan.FromSeconds( 5 ), "Clipboard capture timed out" );
            if ( entries.Count == 0 )
            {
                await Task.Delay( 100 );
            }
        } while ( entries.Count == 0 );
        var entry = entries[0];
        Require( Succeeded( await client.InvokeAsync( entry, "pin" ) ), "Pin clipboard entry" );
        Require( Succeeded( await client.InvokeAsync( entry, "copy" ) ), "Copy clipboard entry" );
        Require( await File.ReadAllTextAsync( clipboard ) == "synthetic\r\n😀é\n", "Native clipboard capture and copy contents" );
        var clipboardRows = await client.SearchAsync( "clip:" );
        controls = clipboardRows.First( value => ( string? ) value["id"] == "clipboard:controls" );
        entry = clipboardRows.First( value => ( string? ) value["id"] == ( string? ) entry["id"] );
        Require( Succeeded( await client.InvokeAsync( controls, "disable" ) ), "Pause clipboard capture" );
        outcome = await client.InvokeAsync( entry, "delete" );
        Require( Succeeded( outcome ), "Delete clipboard entry: " + outcome.ToJsonString( ) );
        Require( !Succeeded( await client.InvokeAsync( entry, "copy" ) ), "Deleted clipboard entry is unavailable" );
        Console.WriteLine( "PASS opt-in clipboard capture, pin, copy, pause, and deletion" );

        var rows = await client.SearchAsync( "browse:", new JsonObject { ["browseDirectory"] = project } );
        var files = rows.Where( value => new[] { first, second }.Contains( ( string? ) value["metadata"]?["path"] ) ).ToList( );
        Require( files.Count == 2, "Folder browsing returns both files" );
        List<JsonObject> selected = [];
        foreach ( var file in files )
        {
            selected = await client.RequestAsync( "files.select", new JsonObject { ["resultId"] = ( string? ) file["id"], ["enabled"] = true } );
            Require( selected.Any( message => ( string? ) message["type"] == "file-selection" ), "Select file" );
        }
        Require( selected[0]["fileSelection"]!.AsArray( ).Count == 2, "Retain both selections" );
        Require( Succeeded( (await client.RequestAsync( "files.batch", new JsonObject { ["actionId"] = "copy-paths" } ))[0]["actionResult"]!.AsObject( ) ), "Copy selected paths" );
        Require( new HashSet<string>( await File.ReadAllLinesAsync( clipboard ) ).SetEquals( [first, second] ), "Literal selected paths" );
        Require( Succeeded( (await client.RequestAsync( "files.batch", new JsonObject { ["actionId"] = "open" } ))[0]["actionResult"]!.AsObject( ) ), "Open selected files" );
        Require( (await File.ReadAllLinesAsync( opened )).Select( line => ( string ) JsonNode.Parse( line )![0]! ).ToHashSet( ).SetEquals( [first, second] ), "Opened file arguments" );
        await client.RequestAsync( "files.clear" );
        Require( !Succeeded( (await client.RequestAsync( "files.batch", new JsonObject { ["actionId"] = "open" } ))[0]["actionResult"]!.AsObject( ) ), "Cleared selection cannot open files" );
        Require( ( string? ) (await client.RequestAsync( "files.select", new JsonObject { ["resultId"] = "forged", ["enabled"] = true } ))[0]["type"] == "error", "Reject forged selection" );
        Console.WriteLine( "PASS folder browsing and retained multi-file actions over the socket" );

        var picker = await RunProcessAsync( binary, ["pick", "--label-tab"], environment, "a\tAlpha\nb value\tBeta\n" );
        Require( picker.ExitCode == 0 && picker.Output == "b value\n", $"Picker failed: {picker.ExitCode}, {picker.Output}, {picker.Error}" );
        Console.WriteLine( "PASS pipe picker stdout contract and packaged assets" );
    }

    private static async Task<WaylandFixture> StartWaylandFixtureAsync(
        Dictionary<string, string> environment, [CallerFilePath] string source = "" )
    {
        var project = Path.Combine( Path.GetDirectoryName( source )!, "CommandDeck.Specs", "CommandDeck.Specs.csproj" );
        var build = await RunProcessAsync( "dotnet", ["build", project, "-c", "Release", "--nologo", "--verbosity", "quiet"],
            environment, timeoutSeconds: 120 );
        Require( build.ExitCode == 0, "Synthetic Wayland fixture build failed: " + build.Output + build.Error );
        var binary = Path.Combine( Path.GetDirectoryName( project )!, "bin", "Release", "net11.0", "linux-x64", "CommandDeck.Specs" );
        WaylandFixture fixture = new( StartProcess( binary, ["--clipboard-wayland-server"], environment ) );
        try
        {
            var name = await fixture.ReadAsync( );
            Require( name.StartsWith( "wayland-", StringComparison.Ordinal ), "Synthetic Wayland server did not start: " + name );
            environment["WAYLAND_DISPLAY"] = Path.Combine( environment["XDG_RUNTIME_DIR"], name );
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync( );
            throw;
        }
    }

    private sealed class WaylandFixture( Process process ) : IAsyncDisposable
    {
        private readonly Task<string> _error = process.StandardError.ReadToEndAsync( );

        public async Task<string> ReadAsync( )
        {
            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 5 ) );
            var line = await process.StandardOutput.ReadLineAsync( timeout.Token );
            if ( line is null )
            {
                throw new IOException( "Synthetic Wayland server disconnected: " + await _error );
            }

            return line;
        }

        public async Task<string> CommandAsync( string command )
        {
            await process.StandardInput.WriteLineAsync( command );
            return await ReadAsync( );
        }

        public async ValueTask DisposeAsync( )
        {
            process.StandardInput.Close( );
            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 5 ) );
            try
            {
                await process.WaitForExitAsync( timeout.Token );
            }
            catch ( OperationCanceledException )
            {
                process.Kill( entireProcessTree: true );
                await process.WaitForExitAsync( );
            }
            var exitCode = process.ExitCode;
            var error = await _error;
            process.Dispose( );
            Require( exitCode == 0, "Synthetic Wayland server failed: " + error );
        }
    }

    private sealed class SmokeClient( StreamReader reader, StreamWriter writer )
    {
        private int _requestId;

        public async Task<List<JsonObject>> RequestAsync( string operation, JsonObject? values = null )
        {
            var request = values ?? new JsonObject( );
            request["version"] = 3;
            request["requestId"] = ++_requestId;
            request["operation"] = operation;
            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 10 ) );
            await writer.WriteLineAsync( request.ToJsonString( ).AsMemory( ), timeout.Token );
            List<JsonObject> messages = [];
            while ( true )
            {
                var line = await reader.ReadLineAsync( timeout.Token );
                if ( line is null )
                {
                    throw new IOException( "Daemon disconnected" );
                }

                var message = JsonNode.Parse( line ) as JsonObject;
                if ( message is null )
                {
                    throw new IOException( "Daemon returned an invalid message" );
                }

                if ( ( int? ) message["requestId"] != _requestId )
                {
                    continue;
                }
                if ( ( string? ) message["type"] == "done" )
                {
                    return messages;
                }
                messages.Add( message );
            }
        }

        public async Task<List<JsonObject>> SearchAsync( string query, JsonObject? values = null )
        {
            var request = values ?? new JsonObject( );
            request["query"] = query;
            var messages = await RequestAsync( "search", request );
            Require( messages.All( message => ( string? ) message["type"] != "error" ), Describe( messages ) );
            return messages.Where( message => ( string? ) message["type"] == "result" )
                .Select( message => ( JsonObject ) message["result"]! ).ToList( );
        }

        public async Task<JsonObject> InvokeAsync( JsonObject result, string action, JsonObject? values = null )
        {
            var reference = ( string? ) result["invocationReference"];
            Require( !string.IsNullOrWhiteSpace( reference ), "Search result has no invocation reference" );
            var request = values ?? new JsonObject( );
            request["resultId"] = ( string? ) result["id"];
            request["actionId"] = action;
            request["invocationReference"] = reference;
            var messages = await RequestAsync( "invoke", request );
            var response = messages.FirstOrDefault( message => ( string? ) message["type"] == "action-result" );
            if ( response is null )
            {
                throw new InvalidOperationException( $"Invocation {action} failed: {Describe( messages )}" );
            }

            return ( JsonObject ) response["actionResult"]!;
        }
    }
}
