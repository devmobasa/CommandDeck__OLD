#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

await BrokerJourney.Run( args );

static class BrokerJourney
{
    public static async Task Run( string[] args )
    {
        if ( args.Length != 2 )
        {
            throw new ArgumentException( "Usage: dotnet scripts/test-extension-brokers.cs HOST CONNECTED_TOOLS.cdext" );
        }
        var root = Directory.CreateTempSubdirectory( "commanddeck-brokers-" ).FullName;
        var socket = Path.Combine( root, "daemon.sock" );
        Process? daemon = null;
        Task<string>? output = null, errors = null;
        try
        {
            var config = Path.Combine( root, "config.json" );
            await File.WriteAllTextAsync( config, new JsonObject
            {
                ["storage"] = new JsonObject { ["databasePath"] = Path.Combine( root, "catalog.db" ) },
                ["indexing"] = new JsonObject { ["roots"] = new JsonArray( ), ["indexDesktopApplications"] = false, ["indexOmarchyCommands"] = false },
                ["ai"] = new JsonObject { ["enabled"] = false },
                ["agent"] = new JsonObject { ["runtimeDirectory"] = Path.Combine( root, "runtime" ), ["stateDirectory"] = Path.Combine( root, "state" ) }
            }.ToJsonString( ) );
            var start = new ProcessStartInfo( Path.GetFullPath( args[0] ) ) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach ( var value in new[] { "daemon", "--config", config, "--socket", socket } )
            {
                start.ArgumentList.Add( value );
            }
            daemon = Process.Start( start )!;
            output = daemon.StandardOutput.ReadToEndAsync( );
            errors = daemon.StandardError.ReadToEndAsync( );
            using var deadline = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
            while ( !File.Exists( socket ) )
            {
                await Task.Delay( 25, deadline.Token );
            }
            using var client = await BrokerClient.Connect( socket );
            var inspected = await Change( client, new JsonObject { ["action"] = "inspect", ["archivePath"] = Path.GetFullPath( args[1] ) } );
            var installed = await Change( client, new JsonObject
            {
                ["action"] = "install",
                ["archivePath"] = Path.GetFullPath( args[1] ),
                ["digest"] = inspected["archiveDigest"]!.DeepClone( ),
                ["confirmed"] = true,
                ["grants"] = new JsonArray( "files.read", "http.request", "secrets.use", "storage.read", "storage.write" )
            } );
            var key = installed["packages"]![0]!["key"]!.GetValue<string>( );
            await Change( client, new JsonObject { ["action"] = "enable", ["key"] = key } );
            await Change( client, new JsonObject { ["action"] = "enable" } );
            await Notes( client, root, key );
            await Tasks( client, key );
            await Change( client, new JsonObject { ["action"] = "disable", ["key"] = key } );
            var connections = await Change( client, new JsonObject { ["action"] = "connections", ["key"] = key } );
            That( connections["connections"]!.AsArray( ).Count == 0, "Disable retained broker authority" );
            Console.WriteLine( "PASS connected brokers: published daemon and native sandbox, selected note read/revocation, HTTP adapter, credential attachment, cancelled and confirmed writes, replay denial and package revocation." );
        }
        finally
        {
            if ( daemon is not null )
            {
                if ( !daemon.HasExited )
                {
                    var stop = new ProcessStartInfo( "/usr/bin/kill" );
                    stop.ArgumentList.Add( "-TERM" );
                    stop.ArgumentList.Add( daemon.Id.ToString( ) );
                    using var signal = Process.Start( stop )!;
                    await signal.WaitForExitAsync( );
                    try
                    {
                        await daemon.WaitForExitAsync( ).WaitAsync( TimeSpan.FromSeconds( 10 ) );
                    }
                    catch ( TimeoutException )
                    {
                        daemon.Kill( true );
                        await daemon.WaitForExitAsync( );
                    }
                }
                var log = await output! + await errors!;
                Directory.CreateDirectory( "artifacts/extensions" );
                await File.WriteAllTextAsync( "artifacts/extensions/broker-journey.log", log );
                That( !log.Contains( "private-bearer", StringComparison.Ordinal ), "Credential leaked to daemon log" );
                daemon.Dispose( );
            }
            Directory.Delete( root, true );
        }
    }
    private static async Task Notes( BrokerClient client, string root, string key )
    {
        var notes = Path.Combine( root, "notes" );
        Directory.CreateDirectory( notes );
        await File.WriteAllTextAsync( Path.Combine( notes, "meeting.txt" ), "Chosen note\r\ncontent" );
        await File.WriteAllTextAsync( Path.Combine( notes, ".env" ), "protected" );
        await Change( client, new JsonObject
        {
            ["action"] = "connection-set",
            ["key"] = key,
            ["confirmed"] = true,
            ["connection"] = new JsonObject { ["name"] = "notes", ["kind"] = "files", ["path"] = notes }
        } );
        var list = View( await client.Call( "extension.open", Open( key, "notes" ) ) );
        That( list["content"]!["items"]!.AsArray( ).Count == 1, "Protected file was listed" );
        var item = list["content"]!["items"]![0]!;
        var detail = View( await Action( client, list, item["actions"]![0]!["handle"]!.GetValue<string>( ), item["id"]!.GetValue<string>( ) ) );
        That( detail["content"]!["body"]!.GetValue<string>( ) == "Chosen note\ncontent", "Native note read failed" );
        await client.Call( "extension.close", new JsonObject { ["sessionId"] = detail["sessionId"]!.DeepClone( ) } );
        await Change( client, new JsonObject { ["action"] = "connection-remove", ["key"] = key, ["connectionName"] = "notes", ["confirmed"] = true } );
        var revoked = View( await client.Call( "extension.open", Open( key, "notes" ) ) );
        That( revoked["content"]!["title"]!.GetValue<string>( ).Contains( "Select a notes root", StringComparison.Ordinal ), "Revoked note root remained readable" );
        await client.Call( "extension.close", new JsonObject { ["sessionId"] = revoked["sessionId"]!.DeepClone( ) } );
    }
    private static async Task Tasks( BrokerClient client, string key )
    {
        await using var service = new MockTasks( );
        await Change( client, new JsonObject
        {
            ["action"] = "connection-set",
            ["key"] = key,
            ["confirmed"] = true,
            ["connection"] = new JsonObject
            {
                ["name"] = "tasks",
                ["kind"] = "http",
                ["origin"] = service.Origin,
                ["route"] = "/tasks",
                ["methods"] = new JsonArray( "GET", "POST" ),
                ["privateNetwork"] = true,
                ["credentialKind"] = "session-bearer",
                ["credential"] = "private-bearer"
            }
        } );
        var list = View( await client.Call( "extension.open", Open( key, "tasks" ) ) );
        That( list["content"]!["items"]!.AsArray( ).Count == 1, "HTTP adapter did not list tasks" );
        var form = View( await Action( client, list, list["content"]!["actions"]![1]!["handle"]!.GetValue<string>( ) ) );
        var cancelled = View( await Save( client, form, false ) );
        That( service.Writes == 0, "Cancellation sent a remote write" );
        var saved = View( await Save( client, cancelled, true ) );
        That( saved["content"]!["title"]!.GetValue<string>( ) == "Task created" && service.Writes == 1, "Confirmed task was not created exactly once" );
        That( service.Authorization == "Bearer private-bearer", "Host did not attach the scoped credential" );
        var replay = Input( saved );
        replay["extensionInput"]!["confirmationId"] = client.LastConfirmation;
        replay["extensionInput"]!["confirmed"] = true;
        await client.Call( "extension.action", replay, error: true );
        That( service.Writes == 1, "Replayed confirmation sent another write" );
        await client.Call( "extension.close", new JsonObject { ["sessionId"] = saved["sessionId"]!.DeepClone( ) } );
    }
    private static async Task<List<JsonNode>> Save( BrokerClient client, JsonNode form, bool approve )
    {
        await client.Call( "extension.present", Input( form ) );
        var input = Input( form );
        input["extensionInput"]!["handle"] = form["content"]!["submit"]!["handle"]!.DeepClone( );
        input["extensionInput"]!["formRevision"] = ++client.FormRevision;
        input["extensionInput"]!["values"] = new JsonObject { ["title"] = "Created by broker journey" };
        return await client.Call( "extension.action", input, approve: approve );
    }
    private static async Task<List<JsonNode>> Action( BrokerClient client, JsonNode view, string handle, string? item = null )
    {
        await client.Call( "extension.present", Input( view ) );
        var input = Input( view );
        input["extensionInput"]!["handle"] = handle;
        input["extensionInput"]!["itemId"] = item;
        return await client.Call( "extension.action", input );
    }
    private static JsonObject Input( JsonNode view ) => new( ) { ["sessionId"] = view["sessionId"]!.DeepClone( ), ["extensionInput"] = new JsonObject { ["revision"] = view["revision"]!.DeepClone( ) } };
    private static JsonObject Open( string key, string command ) => new( )
    {
        ["resultId"] = "extension:" + key + ":" + command,
        ["extensionInput"] = new JsonObject
        {
            ["features"] = new JsonArray( "commands@1", "brokers@1", "views.list@1", "views.detail@1", "views.stack@1", "views.pagination@1", "views.form@1", "storage@1" )
        }
    };
    private static async Task<JsonNode> Change( BrokerClient client, JsonObject change )
    {
        var current = (await client.Call( "extensions.manage", new JsonObject { ["extension"] = new JsonObject( ), ["limit"] = 32 } )).Single( n => n["extensions"] is not null )["extensions"]!;
        change["expectedRevision"] = current["revision"]!.DeepClone( );
        return (await client.Call( "extensions.manage", new JsonObject { ["extension"] = change, ["limit"] = 32 } )).Single( n => n["extensions"] is not null )["extensions"]!;
    }
    private static JsonNode View( List<JsonNode> messages ) => messages.Last( n => n["extensionSession"]?["content"] is not null )["extensionSession"]!;
    public static void That( bool condition, string message )
    {
        if ( !condition )
        {
            throw new InvalidOperationException( message );
        }
    }
}

sealed class BrokerClient : IDisposable
{
    private readonly Socket _socket;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private int _sequence, _ingress;
    private string? _package;
    public int FormRevision { get; set; }
    public string? LastConfirmation { get; private set; }
    private BrokerClient( Socket socket )
    {
        _socket = socket;
        var stream = new NetworkStream( socket, false );
        _reader = new StreamReader( stream );
        _writer = new StreamWriter( stream, new UTF8Encoding( false ) ) { AutoFlush = true };
    }
    public static async Task<BrokerClient> Connect( string path )
    {
        var socket = new Socket( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        await socket.ConnectAsync( new UnixDomainSocketEndPoint( path ) );
        return new( socket );
    }
    private async Task<int> Send( string operation, JsonObject fields )
    {
        fields = ( JsonObject ) fields.DeepClone( );
        var id = ++_sequence;
        if ( operation == "extension.open" )
        {
            _ingress = 0;
        }
        if ( operation.StartsWith( "extension.", StringComparison.Ordinal ) )
        {
            fields["extensionInput"] ??= new JsonObject( );
        }
        if ( operation is "extension.present" or "extension.action" )
        {
            fields["extensionInput"]!["sequence"] = ++_ingress;
        }
        fields["version"] = 3;
        fields["requestId"] = id;
        fields["operation"] = operation;
        await _writer.WriteLineAsync( fields.ToJsonString( ) );
        return id;
    }
    public async Task<List<JsonNode>> Call( string operation, JsonObject fields, bool error = false, bool approve = false )
    {
        var id = await Send( operation, fields );
        List<JsonNode> messages = [];
        using var deadline = new CancellationTokenSource( TimeSpan.FromSeconds( 40 ) );
        while ( true )
        {
            var messageText = await _reader.ReadLineAsync( deadline.Token );
            if ( messageText is null )
            {
                throw new EndOfStreamException( );
            }

            var message = JsonNode.Parse( messageText )!;
            messages.Add( message );
            if ( message["extensionSession"]?["packageKey"] is { } package )
            {
                _package = package.GetValue<string>( );
            }
            if ( message["extensionSession"]?["confirmation"] is { } confirmation )
            {
                LastConfirmation = confirmation["id"]!.GetValue<string>( );
                await Send( "extension.action", new JsonObject
                {
                    ["sessionId"] = message["extensionSession"]!["sessionId"]!.DeepClone( ),
                    ["extensionInput"] = new JsonObject { ["revision"] = message["extensionSession"]!["revision"]!.DeepClone( ), ["confirmationId"] = LastConfirmation, ["confirmed"] = approve }
                } );
            }
            if ( message["requestId"]!.GetValue<int>( ) == id && message["type"]!.GetValue<string>( ) == "done" )
            {
                break;
            }
        }
        var failed = messages.Any( n => n["requestId"]!.GetValue<int>( ) == id && n["type"]!.GetValue<string>( ) == "error" );
        if ( failed && !error && operation != "extensions.manage" && _package is not null )
        {
            var diagnostics = await Call( "extensions.manage", new JsonObject { ["extension"] = new JsonObject { ["action"] = "logs", ["key"] = _package, ["includeStderr"] = true }, ["limit"] = 32 } );
            BrokerJourney.That( !string.Join( "\n", diagnostics ).Contains( "private-bearer", StringComparison.Ordinal ), "Credential leaked to diagnostics" );
            messages.AddRange( diagnostics );
        }
        BrokerJourney.That( failed == error, operation + " failed: " + string.Join( "\n", messages ) );
        return messages;
    }
    public void Dispose( )
    {
        _writer.Dispose( );
        _reader.Dispose( );
        _socket.Dispose( );
    }
}

sealed class MockTasks : IAsyncDisposable
{
    private readonly TcpListener _listener = new( IPAddress.Loopback, 0 );
    private readonly CancellationTokenSource _lifetime = new( );
    private readonly Task _run;
    public string Origin { get; }
    public string? Authorization { get; private set; }
    public int Writes { get; private set; }
    public MockTasks( )
    {
        _listener.Start( );
        Origin = "http://127.0.0.1:" + (( IPEndPoint ) _listener.LocalEndpoint).Port;
        _run = Run( );
    }
    private async Task Run( )
    {
        try
        {
            while ( !_lifetime.IsCancellationRequested )
            {
                using var client = await _listener.AcceptTcpClientAsync( _lifetime.Token );
                await using var stream = client.GetStream( );
                using var reader = new StreamReader( stream, Encoding.ASCII, false, 1024, true );
                var first = await reader.ReadLineAsync( _lifetime.Token ) ?? "";
                string? line;
                var length = 0;
                while ( !string.IsNullOrEmpty( line = await reader.ReadLineAsync( _lifetime.Token ) ) )
                {
                    if ( line.StartsWith( "Authorization: ", StringComparison.Ordinal ) )
                    {
                        Authorization = line[15..];
                    }
                    if ( line.StartsWith( "Content-Length: ", StringComparison.Ordinal ) )
                    {
                        length = int.Parse( line[16..] );
                    }
                }
                if ( length > 0 )
                {
                    var body = new char[length];
                    await reader.ReadBlockAsync( body, _lifetime.Token );
                }
                if ( first.StartsWith( "POST", StringComparison.Ordinal ) )
                {
                    Writes++;
                }
                const string json = "[{\"title\":\"Existing task\"}]";
                await stream.WriteAsync( Encoding.ASCII.GetBytes( "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + json.Length + "\r\n\r\n" + json ), _lifetime.Token );
            }
        }
        catch ( Exception exception ) when ( exception is OperationCanceledException or SocketException or IOException ) { }
    }
    public async ValueTask DisposeAsync( )
    {
        _lifetime.Cancel( );
        _listener.Stop( );
        await _run;
        _lifetime.Dispose( );
    }
}
