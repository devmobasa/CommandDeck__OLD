#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

if ( args.Length != 2 && !(args.Length == 4 && args[2] == "--expected-title") )
{
    throw new ArgumentException( "Usage: dotnet scripts/test-extension-draft.cs HOST PACKAGE [--expected-title TITLE]" );
}
var host = Path.GetFullPath( args[0] );
var archive = Path.GetFullPath( args[1] );
var root = Directory.CreateTempSubdirectory( "commanddeck-draft-" ).FullName;
var config = Path.Combine( root, "config.json" );
var socket = Path.Combine( root, "daemon.sock" );
Process? daemon = null;
Task<string>? output = null, errors = null;
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
    async Task Start( )
    {
        var info = new ProcessStartInfo( host ) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach ( var value in new[] { "daemon", "--config", config, "--socket", socket } )
        {
            info.ArgumentList.Add( value );
        }
        daemon = Process.Start( info )!;
        output = daemon.StandardOutput.ReadToEndAsync( );
        errors = daemon.StandardError.ReadToEndAsync( );
        using var deadline = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
        while ( !File.Exists( socket ) )
        {
            await Task.Delay( 25, deadline.Token );
        }
    }
    async Task Stop( )
    {
        if ( daemon is null )
        {
            return;
        }
        if ( !daemon.HasExited )
        {
            await DraftCheck.Exec( "kill", "-TERM", daemon.Id.ToString( ) );
            await daemon.WaitForExitAsync( ).WaitAsync( TimeSpan.FromSeconds( 10 ) );
        }
        log.Append( await output! ).Append( await errors! );
        daemon.Dispose( );
        daemon = null;
    }
    await Start( );
    string key;
    async Task<JsonNode> Inventory( DraftClient client, JsonObject request ) =>
        (await client.Call( "extensions.manage", new JsonObject { ["extension"] = request, ["limit"] = 32 } )).Single( n => n["type"]!.GetValue<string>( ) == "extensions-result" )["extensions"]!;
    async Task<JsonNode> Change( DraftClient client, JsonObject change )
    {
        var current = await Inventory( client, new JsonObject { ["action"] = "list" } );
        change["expectedRevision"] = current["revision"]!.GetValue<long>( );
        return await Inventory( client, change );
    }
    JsonObject Open( ) => new( )
    {
        ["resultId"] = "extension:" + key + ":edit",
        ["extensionInput"] = new JsonObject { ["features"] = new JsonArray( "commands@1", "views.form@1", "settings@1", "storage@1" ) }
    };
    async Task<JsonNode> Save( DraftClient client, JsonNode view, string title, bool invalid = false )
    {
        await client.Call( "extension.present", DraftCheck.Input( view, 1 ) );
        var input = DraftCheck.Input( view, 2 );
        input["extensionInput"]!["handle"] = view["content"]!["submit"]!["handle"]!.GetValue<string>( );
        input["extensionInput"]!["formRevision"] = 1;
        input["extensionInput"]!["values"] = new JsonObject { ["title"] = title, ["body"] = "Saved across restart", ["category"] = "task", ["done"] = true };
        var result = await client.Call( "extension.action", input, invalid );
        return invalid ? view : DraftCheck.View( result );
    }
    using ( var client = await DraftClient.Connect( socket ) )
    {
        var review = await Inventory( client, new JsonObject { ["action"] = "inspect", ["archivePath"] = archive } );
        var installed = await Change( client, new JsonObject { ["action"] = "install", ["archivePath"] = archive, ["digest"] = review["archiveDigest"]!.GetValue<string>( ), ["confirmed"] = true } );
        key = installed["packages"]![0]!["key"]!.GetValue<string>( );
        await Change( client, new JsonObject { ["action"] = "enable", ["key"] = key } );
        await Change( client, new JsonObject { ["action"] = "enable" } );
        await client.Call( "extension.open", Open( ), true ); // No storage grant.
        await Change( client, new JsonObject { ["action"] = "grants", ["key"] = key, ["grants"] = new JsonArray( "storage.read", "storage.write" ), ["confirmed"] = true } );
        await Change( client, new JsonObject { ["action"] = "configure", ["key"] = key, ["settings"] = new JsonObject { ["defaultTitle"] = "Configured title" } } );
        var opened = DraftCheck.View( await client.Call( "extension.open", Open( ) ) );
        DraftCheck.That( opened["content"]!["fields"]![0]!["defaultValue"]!.GetValue<string>( ) == "Configured title", "Configured default missing" );
        if ( args.Length == 4 )
        {
            DraftCheck.That( opened["content"]!["title"]!.GetValue<string>( ) == args[3], "The rebuilt source edit did not reach the running extension" );
        }
        await Save( client, opened, "", true );
        await client.Call( "extension.close", new JsonObject { ["sessionId"] = opened["sessionId"]!.GetValue<string>( ) } );
        opened = DraftCheck.View( await client.Call( "extension.open", Open( ) ) );
        var rejected = await Save( client, opened, "Untitled" );
        DraftCheck.That( rejected["content"]!["fieldErrors"]!["title"] is not null, "Custom inline validation missing" );
        // The replacement has fresh handles and accepts a new submission of the edited form.
        await client.Call( "extension.present", DraftCheck.Input( rejected, 3 ) );
        var submit = DraftCheck.Input( rejected, 4 );
        submit["extensionInput"]!["handle"] = rejected["content"]!["submit"]!["handle"]!.GetValue<string>( );
        submit["extensionInput"]!["formRevision"] = 2;
        submit["extensionInput"]!["values"] = new JsonObject { ["title"] = "Saved title", ["body"] = "Saved across restart", ["category"] = "task", ["done"] = true };
        DraftCheck.That( DraftCheck.View( await client.Call( "extension.action", submit ) )["content"]!["title"]!.GetValue<string>( ) == "Draft saved", "Save did not complete" );
        await client.Call( "extension.action", submit, true ); // Same ingress sequence cannot save twice.
        await client.Call( "extension.close", new JsonObject { ["sessionId"] = opened["sessionId"]!.GetValue<string>( ) } );
    }
    await Stop( );
    await Start( );
    using ( var first = await DraftClient.Connect( socket ) )
    {
        using ( var second = await DraftClient.Connect( socket ) )
        {
            var one = DraftCheck.View( await first.Call( "extension.open", Open( ) ) );
            DraftCheck.That( one["content"]!["fields"]![0]!["defaultValue"]!.GetValue<string>( ) == "Saved title", "Draft lost across daemon restart" );
            var two = DraftCheck.View( await second.Call( "extension.open", Open( ) ) );
            await Save( first, one, "First writer" );
            var stale = await Save( second, two, "Stale writer" );
            DraftCheck.That( stale["content"]!["error"] is not null, "Stale writer overwrote another session" );
            await Change( first, new JsonObject { ["action"] = "disable", ["key"] = key } );
            await first.Call( "extension.action", DraftCheck.Input( one, 3 ), true );
            await first.Call( "extension.open", Open( ), true );
            await Change( first, new JsonObject { ["action"] = "enable", ["key"] = key } );
            var reopened = DraftCheck.View( await first.Call( "extension.open", Open( ) ) );
            DraftCheck.That( reopened["content"]!["fields"]![0]!["defaultValue"]!.GetValue<string>( ) == "First writer", "Disable discarded data or stale write succeeded" );
            await Change( first, new JsonObject { ["action"] = "remove", ["key"] = key, ["confirmed"] = true } );
        }
    }
    await Stop( );
    Console.WriteLine( "PASS Local Draft: install/configure, denied grants, native form validation, save/replay, restart persistence, concurrent stale writes, disable/reopen and removal through production IPC and sandbox." );
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
        log.Append( await output! ).Append( await errors! );
        daemon.Dispose( );
    }
    var repo = Path.GetFullPath( Path.Combine( AppContext.GetData( "EntryPointFileDirectoryPath" )!.ToString( )!, ".." ) );
    Directory.CreateDirectory( Path.Combine( repo, "artifacts/extensions" ) );
    await File.WriteAllTextAsync( Path.Combine( repo, "artifacts/extensions/draft-daemon.log" ), log.ToString( ) );
    Directory.Delete( root, true );
}

static class DraftCheck
{
    public static void That( bool value, string message )
    {
        if ( !value )
        {
            throw new InvalidOperationException( message );
        }
    }
    public static JsonNode View( List<JsonNode> messages ) => messages.Last( n => n["extensionSession"]?["status"]?.GetValue<string>( ) == "view" )["extensionSession"]!;
    public static JsonObject Input( JsonNode view, int sequence ) => new( ) { ["sessionId"] = view["sessionId"]!.GetValue<string>( ), ["extensionInput"] = new JsonObject { ["sequence"] = sequence, ["revision"] = view["revision"]!.GetValue<long>( ) } };
    public static async Task Exec( string executable, params string[] args )
    {
        var info = new ProcessStartInfo( executable );
        foreach ( var value in args )
        {
            info.ArgumentList.Add( value );
        }
        using var process = Process.Start( info )!;
        await process.WaitForExitAsync( ).WaitAsync( TimeSpan.FromSeconds( 10 ) );
        That( process.ExitCode == 0, executable );
    }
}

sealed class DraftClient : IDisposable
{
    private readonly Socket _socket;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private int _sequence;
    private DraftClient( Socket socket )
    {
        _socket = socket;
        var stream = new NetworkStream( socket, false );
        _reader = new StreamReader( stream );
        _writer = new StreamWriter( stream, new UTF8Encoding( false ) ) { AutoFlush = true };
    }
    public static async Task<DraftClient> Connect( string path )
    {
        var socket = new Socket( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        await socket.ConnectAsync( new UnixDomainSocketEndPoint( path ) );
        return new DraftClient( socket );
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
        using var deadline = new CancellationTokenSource( TimeSpan.FromSeconds( 15 ) );
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
        DraftCheck.That( messages.Any( n => n["requestId"]!.GetValue<int>( ) == id && n["type"]!.GetValue<string>( ) == "error" ) == error, operation + ": " + string.Join( "\n", messages ) );
        return messages;
    }
    public void Dispose( )
    {
        _writer.Dispose( );
        _reader.Dispose( );
        _socket.Dispose( );
    }
}
