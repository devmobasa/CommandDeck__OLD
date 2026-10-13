#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using static DevCheck;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

if ( args.Length != 2 )
{
    throw new ArgumentException( "Usage: dotnet scripts/test-extension-development.cs HOST DEVELOPMENT_PACKAGE" );
}
var host = Path.GetFullPath( args[0] );
var archive = Path.GetFullPath( args[1] );
var root = Directory.CreateTempSubdirectory( "commanddeck-debug-test-" ).FullName;
var config = Path.Combine( root, "config.json" );
var socket = Path.Combine( root, "daemon.sock" );
Process? daemon = null, debugger = null;
Task<string>? daemonOut = null, daemonErr = null, debuggerOut = null, debuggerErr = null;
var log = new StringBuilder( );
try
{
    await File.WriteAllTextAsync( config, new JsonObject
    {
        ["storage"] = new JsonObject { ["databasePath"] = Path.Combine( root, "catalog.db" ) },
        ["indexing"] = new JsonObject { ["roots"] = new JsonArray( ), ["indexDesktopApplications"] = false, ["indexOmarchyCommands"] = false },
        ["ai"] = new JsonObject { ["enabled"] = false },
        ["agent"] = new JsonObject { ["runtimeDirectory"] = Path.Combine( root, "runtime" ), ["stateDirectory"] = Path.Combine( root, "state" ) }
    }.ToJsonString( ) );
    daemon = Process.Start( Info( host, "daemon", "--config", config, "--socket", socket ) )!;
    daemonOut = daemon.StandardOutput.ReadToEndAsync( );
    daemonErr = daemon.StandardError.ReadToEndAsync( );
    await WaitFile( socket );
    using var manager = await DevClient.Connect( socket );
    async Task<JsonNode> Inventory( JsonObject change ) => (await manager.Call( "extensions.manage", new( )
    {
        ["extension"] = change,
        ["limit"] = 32
    } )).Single( n => n["type"]!.GetValue<string>( ) == "extensions-result" )["extensions"]!;
    async Task<JsonNode> Change( JsonObject change )
    {
        change["expectedRevision"] = (await Inventory( new( )
        {
            ["action"] = "list"
        } ))["revision"]!.GetValue<long>( );
        return await Inventory( change );
    }
    var review = await Inventory( new( )
    {
        ["action"] = "inspect",
        ["archivePath"] = archive
    } );
    var digest = review["archiveDigest"]!.GetValue<string>( );
    var installed = await Change( new( )
    {
        ["action"] = "attach",
        ["archivePath"] = archive,
        ["digest"] = digest,
        ["confirmed"] = true,
        ["grants"] = new JsonArray( "storage.read", "storage.write" )
    } );
    var key = installed["packages"]![0]!["key"]!.GetValue<string>( );
    await Change( new( )
    {
        ["action"] = "enable",
        ["key"] = key
    } );
    await Change( new( )
    {
        ["action"] = "enable"
    } );
    JsonObject Open( ) => new( )
    {
        ["resultId"] = "extension:" + key + ":edit",
        ["extensionInput"] = new JsonObject { ["features"] = new JsonArray( "commands@1", "views.form@1", "settings@1", "storage@1", "development.debug@1" ) }
    };
    async Task<JsonNode> Arm( int seconds ) => (await Change( new( )
    {
        ["action"] = "debug",
        ["key"] = key,
        ["commandId"] = "edit",
        ["digest"] = digest,
        ["debugSeconds"] = seconds,
        ["confirmed"] = true
    } ))["debug"]!;
    var lease = await Arm( 90 );
    var endpoint = lease["socketPath"]!.GetValue<string>( );
    using var client = await DevClient.Connect( socket );
    var olderRenderer = Open( );
    olderRenderer["extensionInput"]!["features"] = new JsonArray( "commands@1", "views.form@1", "settings@1", "storage@1" );
    await client.Call( "extension.open", olderRenderer, true );
    var opening = client.Call( "extension.open", Open( ) );
    await WaitFile( endpoint );
    var debuggerLog = Path.Combine( root, "gdb.log" );
    var start = Info( "/usr/bin/gdb", "--nx", "--quiet", "-ex", "set pagination off", "-ex", "set confirm off", "-ex", "set logging file " + debuggerLog, "-ex", "set logging enabled on", "-ex", "target remote | " + Quote( host ) + " extension-debug-connect --socket " + Quote( endpoint ) );
    start.RedirectStandardInput = true;
    debugger = Process.Start( start )!;
    debuggerOut = debugger.StandardOutput.ReadToEndAsync( );
    debuggerErr = debugger.StandardError.ReadToEndAsync( );
    var privateFile = Path.Combine( root, "host-private.txt" );
    var copiedFile = Path.Combine( root, "debugger-copy.txt" );
    await File.WriteAllTextAsync( privateFile, "HOST_PRIVATE_SENTINEL" );
    await debugger.StandardInput.WriteLineAsync( "remote get " + privateFile + " " + copiedFile );
    await debugger.StandardInput.FlushAsync( );
    // Deliberately remain stopped beyond the normal 8-second open deadline.
    await Task.Delay( TimeSpan.FromSeconds( 10 ) );
    DevCheck.That( !opening.IsCompleted, "Debugged startup did not suspend its foreground deadline" );
    await debugger.StandardInput.WriteLineAsync( "info registers rip\ncontinue" );
    await debugger.StandardInput.FlushAsync( );
    var messages = await opening.WaitAsync( TimeSpan.FromSeconds( 15 ) );
    var view = messages.Last( n => n["extensionSession"]?["status"]?.GetValue<string>( ) == "view" )["extensionSession"]!;
    DevCheck.That( view["debug"]?["consumed"]?.GetValue<bool>( ) == true, "The view has no consumed debug lease" );
    await ExercisePausedCallback( debugger, debuggerLog, client, view );
    // Revocation must kill the entire namespace even while the debugger is attached.
    await Change( new( )
    {
        ["action"] = "debug-stop",
        ["key"] = key
    } );
    await debugger.StandardInput.WriteLineAsync( "quit" );
    await debugger.StandardInput.FlushAsync( );
    await debugger.WaitForExitAsync( ).WaitAsync( TimeSpan.FromSeconds( 10 ) );
    var gdbOutput = await debuggerOut + await debuggerErr;
    log.Append( gdbOutput );
    DevCheck.That( gdbOutput.Contains( "rip", StringComparison.Ordinal ), "GDB did not read the stopped child's registers" );
    DevCheck.That( !File.Exists( copiedFile ) || !File.ReadAllText( copiedFile ).Contains( "HOST_PRIVATE_SENTINEL", StringComparison.Ordinal ), "Debugger server escaped the child filesystem" );
    debugger.Dispose( );
    debugger = null;
    // Closing/expiry removes the private endpoint and allows the ordinary release path again.
    DevCheck.That( !Directory.Exists( Path.GetDirectoryName( endpoint )! ), "Debug endpoint survived revocation" );
    var reopened = await client.Call( "extension.open", Open( ) );
    DevCheck.That( reopened.Any( n => n["extensionSession"]?["status"]?.GetValue<string>( ) == "view" && n["extensionSession"]?["debug"] is null ), "Ordinary reopen inherited debugging" );
    await client.Call( "extension.close", new( )
    {
        ["sessionId"] = reopened.Last( n => n["extensionSession"]?["status"]?.GetValue<string>( ) == "view" )["extensionSession"]!["sessionId"]!.GetValue<string>( )
    } );
    var shortLease = await Arm( 3 );
    await client.Call( "extension.open", Open( ), true );
    DevCheck.That( !Directory.Exists( Path.GetDirectoryName( shortLease["socketPath"]!.GetValue<string>( ) )! ), "Expired debug endpoint survived" );
    var logs = await Inventory( new( )
    {
        ["action"] = "logs",
        ["key"] = key
    } );
    DevCheck.That( logs["diagnostics"]!.AsArray( ).Count >= 3, "Session diagnostics were not retained" );
    DevCheck.That( logs["diagnostics"]!.AsArray( ).All( n => n!["stderr"] is null ), "Child stderr leaked without explicit opt-in" );
    Console.WriteLine( "PASS development debugging: reviewed attachment, real GDB registers, suspended open and callback deadlines, visible lease, revoke/expiry cleanup, release reopen, bounded metadata-only diagnostics." );
}
finally
{
    if ( debugger is not null )
    {
        if ( !debugger.HasExited )
        {
            debugger.Kill( true );
        }
        await debugger.WaitForExitAsync( );
        log.Append( await debuggerOut! ).Append( await debuggerErr! );
        debugger.Dispose( );
    }
    if ( daemon is not null )
    {
        if ( !daemon.HasExited )
        {
            daemon.Kill( true );
        }
        await daemon.WaitForExitAsync( );
        log.Append( await daemonOut! ).Append( await daemonErr! );
        daemon.Dispose( );
    }
    var repo = Path.GetFullPath( Path.Combine( AppContext.GetData( "EntryPointFileDirectoryPath" )!.ToString( )!, ".." ) );
    await File.WriteAllTextAsync( Path.Combine( repo, "artifacts/extensions/development-debug.log" ), log.ToString( ) );
    Directory.Delete( root, true );
}
static class DevCheck
{
    public static void That( bool condition, string message )
    {
        if ( !condition )
        {
            throw new InvalidOperationException( message );
        }
    }
    public static async Task ExercisePausedCallback( Process debugger, string debuggerLog, DevClient client, JsonNode view )
    {
        using ( var interrupt = Process.Start( Info( "/usr/bin/kill", "-INT", debugger.Id.ToString( ) ) )! )
        {
            await interrupt.WaitForExitAsync( );
        }
        await debugger.StandardInput.WriteLineAsync( "echo STOPPED_FOR_CALLBACK\\n" );
        await debugger.StandardInput.FlushAsync( );
        using ( var stopped = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) ) )
        {
            while ( !File.Exists( debuggerLog ) || !(await File.ReadAllTextAsync( debuggerLog )).Contains( "STOPPED_FOR_CALLBACK", StringComparison.Ordinal ) )
            {
                await Task.Delay( 25, stopped.Token );
            }
        }
        var sessionId = view["sessionId"]!.GetValue<string>( );
        var revision = view["revision"]!.GetValue<long>( );
        await client.Call( "extension.present", new( )
        {
            ["sessionId"] = sessionId,
            ["extensionInput"] = new JsonObject { ["sequence"] = 1, ["revision"] = revision }
        } );
        var saving = client.Call( "extension.action", new( )
        {
            ["sessionId"] = sessionId,
            ["extensionInput"] = new JsonObject
            {
                ["sequence"] = 2,
                ["revision"] = revision,
                ["handle"] = view["content"]!["submit"]!["handle"]!.GetValue<string>( ),
                ["formRevision"] = 1,
                ["values"] = new JsonObject { ["title"] = "Debug save", ["body"] = "Paused callback", ["category"] = "note", ["done"] = false }
            }
        } );
        await Task.Delay( TimeSpan.FromSeconds( 31 ) );
        DevCheck.That( !saving.IsCompleted, "Debugged callback did not suspend its foreground deadline" );
        await debugger.StandardInput.WriteLineAsync( "continue" );
        await debugger.StandardInput.FlushAsync( );
        DevCheck.That( (await saving).Any( n => n["extensionSession"]?["content"]?["title"]?.GetValue<string>( ) == "Draft saved" ), "Paused callback did not complete after continuing" );
    }
    public static ProcessStartInfo Info( string executable, params string[] values )
    {
        var info = new ProcessStartInfo( executable ) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach ( var value in values )
        {
            info.ArgumentList.Add( value );
        }
        return info;
    }
    public static string Quote( string value ) => "'" + value.Replace( "'", "'\\''", StringComparison.Ordinal ) + "'";
    public static async Task WaitFile( string path )
    {
        using var deadline = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
        while ( !File.Exists( path ) )
        {
            await Task.Delay( 25, deadline.Token );
        }
    }

}

sealed class DevClient : IDisposable
{
    private readonly Socket _socket;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private int _sequence;
    private DevClient( Socket socket )
    {
        _socket = socket;
        var stream = new NetworkStream( socket, false );
        _reader = new StreamReader( stream );
        _writer = new StreamWriter( stream, new UTF8Encoding( false ) ) { AutoFlush = true };
    }
    public static async Task<DevClient> Connect( string path )
    {
        var socket = new Socket( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        await socket.ConnectAsync( new UnixDomainSocketEndPoint( path ) );
        return new DevClient( socket );
    }
    public async Task<List<JsonNode>> Call( string operation, JsonObject fields, bool error = false )
    {
        fields = ( JsonObject ) fields.DeepClone( );
        var id = ++_sequence;
        if ( operation.StartsWith( "extension.", StringComparison.Ordinal ) && fields["extensionInput"] is null )
        {
            fields["extensionInput"] = new JsonObject( );
        }
        fields["version"] = 3;
        fields["requestId"] = id;
        fields["operation"] = operation;
        await _writer.WriteLineAsync( fields.ToJsonString( ) );
        List<JsonNode> messages = [];
        using var deadline = new CancellationTokenSource( TimeSpan.FromSeconds( 45 ) );
        while ( true )
        {
            var messageText = await _reader.ReadLineAsync( deadline.Token );
            if ( messageText is null )
            {
                throw new EndOfStreamException( );
            }

            var message = JsonNode.Parse( messageText )!;
            messages.Add( message );
            if ( message["requestId"]!.GetValue<int>( ) == id && message["type"]!.GetValue<string>( ) == "done" )
            {
                break;
            }
        }
        DevCheck.That( messages.Any( n => n["requestId"]!.GetValue<int>( ) == id && n["type"]!.GetValue<string>( ) == "error" ) == error, operation + ": " + string.Join( "\n", messages ) );
        return messages;
    }
    public void Dispose( )
    {
        _writer.Dispose( );
        _reader.Dispose( );
        _socket.Dispose( );
    }
}
