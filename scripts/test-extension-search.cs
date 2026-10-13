#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

if ( args.Length is < 2 or > 3 )
{
    throw new ArgumentException( "Usage: dotnet scripts/test-extension-search.cs HOST NOTEBOOK.cdext [HOSTILE.cdext]" );
}
var root = Directory.CreateTempSubdirectory( "commanddeck-search-" ).FullName;
Process? daemon = null;
Task<string>? stdout = null, stderr = null;
try
{
    var config = Path.Combine( root, "config.json" );
    var socket = Path.Combine( root, "daemon.sock" );
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
    stdout = daemon.StandardOutput.ReadToEndAsync( );
    stderr = daemon.StandardError.ReadToEndAsync( );
    using var startup = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
    while ( !File.Exists( socket ) )
    {
        await Task.Delay( 25, startup.Token );
    }
    using var client = await SearchClient.Connect( socket );
    var baseline = await Measure( client, "Meeting", 12 );
    daemon.Refresh( );
    var baselineRss = daemon.WorkingSet64;
    var inspected = await Change( client, new JsonObject { ["action"] = "inspect", ["archivePath"] = Path.GetFullPath( args[1] ) } );
    var installed = await Change( client, new JsonObject
    {
        ["action"] = "install",
        ["archivePath"] = Path.GetFullPath( args[1] ),
        ["digest"] = inspected["archiveDigest"]!.DeepClone( ),
        ["confirmed"] = true,
        ["grants"] = new JsonArray( "storage.read", "storage.write" )
    } );
    var key = installed["packages"]![0]!["key"]!.GetValue<string>( );
    await Change( client, new JsonObject { ["action"] = "enable", ["key"] = key } );
    await Change( client, new JsonObject { ["action"] = "enable" } );
    var form = View( await client.Call( "extension.open", Open( "extension:" + key + ":note" ) ) );
    await client.Call( "extension.present", Input( form ) );
    var submit = Input( form );
    submit["extensionInput"]!["handle"] = form["content"]!["submit"]!["handle"]!.DeepClone( );
    submit["extensionInput"]!["formRevision"] = 1;
    submit["extensionInput"]!["values"] = new JsonObject { ["title"] = "Meeting notebook", ["body"] = "Saved searchable note" };
    var detail = View( await client.Call( "extension.action", submit ) );
    That( detail["content"]!["body"]!.GetValue<string>( ) == "Saved searchable note", "Notebook save failed" );
    await client.Call( "extension.close", new JsonObject { ["sessionId"] = detail["sessionId"]!.DeepClone( ) } );
    var providerId = "extension-search:" + key + ":notes";
    await client.Call( "filter-alias.set", new JsonObject { ["alias"] = "notes", ["targetFilterId"] = providerId } );
    await client.Call( "filter-alias.set", new JsonObject { ["alias"] = "app", ["targetFilterId"] = providerId }, error: true );
    That( (await Search( client, "Meeting" )).All( row => !Dynamic( row ) ), "Root query reached a provider without consent" );
    var launcherResults = await client.Call( "search", new JsonObject
    {
        ["query"] = "Meeting",
        ["limit"] = 50,
        ["expression"] = new JsonObject
        {
            ["includeFilterIds"] = new JsonArray( providerId ),
            ["excludeFilterIds"] = new JsonArray( ),
            ["clauses"] = new JsonArray( new JsonObject { ["field"] = null, ["text"] = "Meeting", ["excluded"] = false } )
        }
    } );
    That( launcherResults.Count( message => message["result"] is JsonNode result && Dynamic( result ) ) == 1, "Launcher scoped expression did not find the saved note" );
    var cold = Stopwatch.StartNew( );
    var row = (await Search( client, "notes: Meeting" )).Single( Dynamic );
    cold.Stop( );
    using ( var foreign = await SearchClient.Connect( socket ) )
    {
        await foreign.Call( "extension.open", Open( row["id"]!.GetValue<string>( ), row ), error: true );
    }
    detail = View( await client.Call( "extension.open", Open( row["id"]!.GetValue<string>( ), row ) ) );
    That( detail["content"]!["title"]!.GetValue<string>( ) == "Meeting notebook", "Result did not open the declared command with its item context" );
    await client.Call( "extension.close", new JsonObject { ["sessionId"] = detail["sessionId"]!.DeepClone( ) } );
    await client.Call( "extension.open", Open( row["id"]!.GetValue<string>( ), row ), error: true );
    row = (await Search( client, "notes: Meeting" )).Single( Dynamic );
    await Search( client, "notes: absent" );
    await client.Call( "extension.open", Open( row["id"]!.GetValue<string>( ), row ), error: true );
    await Change( client, new JsonObject { ["action"] = "global-search", ["key"] = key, ["globalSearchEnabled"] = true, ["confirmed"] = true } );
    row = await SearchJourney.FindGlobalRow( ( ) => Search( client, "Meeting" ), Dynamic );
    var global = await Measure( client, "Meeting", 12 );
    var burst = await client.Burst( "notes: " );
    That( burst.Any( Dynamic ), "The final query in a typing burst lost its search results" );
    row = await SearchJourney.FindGlobalRow( ( ) => Search( client, "Meeting" ), Dynamic );
    await Change( client, new JsonObject { ["action"] = "global-search", ["key"] = key, ["globalSearchEnabled"] = false, ["confirmed"] = true } );
    await client.Call( "extension.open", Open( row["id"]!.GetValue<string>( ), row ), error: true );
    That( (await Search( client, "Meeting" )).All( result => !Dynamic( result ) ), "Global search remained enabled" );
    row = (await Search( client, "notes: Meeting" )).Single( Dynamic );
    await Change( client, new JsonObject { ["action"] = "disable", ["key"] = key } );
    await client.Call( "extension.open", Open( row["id"]!.GetValue<string>( ), row ), error: true );
    await client.Call( "search", new JsonObject { ["query"] = "notes: Meeting", ["limit"] = 50 }, error: true );
    That( (await Search( client, "Meeting" )).All( result => !Dynamic( result ) ), "Disabled provider still returned results" );
    Console.WriteLine( $"PASS native search journey: save/cache, scoped prefix, query consent, process-independent activation, foreign/replayed/stale leases and revocation. Cold scoped={cold.Elapsed.TotalMilliseconds:F1}ms" );
    daemon.Refresh( );
    Console.WriteLine( $"Baseline ms {Percentiles( baseline )}; opt-in ms {Percentiles( global )} (12 serial samples, not a production benchmark). Daemon idle RSS baseline={baselineRss / 1024}KiB, after search={daemon.WorkingSet64 / 1024}KiB; 12-query typing burst passed." );
    if ( args.Length == 3 )
    {
        await Isolation( client, socket, Path.GetFullPath( args[2] ), daemon );
    }
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
        Directory.CreateDirectory( "artifacts/extensions" );
        var log = await stdout! + await stderr!;
        await File.WriteAllTextAsync( "artifacts/extensions/search-journey.log", log );
        That( !log.Contains( "QUERY-MUST-NOT-ESCAPE", StringComparison.Ordinal ), "Search stderr escaped into host logs" );
        daemon.Dispose( );
    }
    Directory.Delete( root, true );
}
static void That( bool value, string message )
{
    if ( !value )
    {
        throw new InvalidOperationException( message );
    }
}
static bool Dynamic( JsonNode row ) => row["id"]!.GetValue<string>( ).StartsWith( "extension-item:", StringComparison.Ordinal );
static JsonNode View( List<JsonNode> messages ) => messages.Last( message => message["extensionSession"]?["content"] is not null )["extensionSession"]!;
static async Task<List<JsonNode>> Search( SearchClient client, string query ) => (await client.Call( "search", new JsonObject { ["query"] = query, ["limit"] = 50 } )).Where( message => message["result"] is not null ).Select( message => message["result"]! ).ToList( );
static JsonObject Open( string id, JsonNode? row = null ) => new( )
{
    ["resultId"] = id,
    ["extensionInput"] = new JsonObject { ["features"] = new JsonArray( "commands@1", "views.form@1", "views.detail@1", "storage@1" ), ["searchLease"] = row?["metadata"]?["extensionSearchLease"]?.DeepClone( ) }
};
static JsonObject Input( JsonNode view ) => new( ) { ["sessionId"] = view["sessionId"]!.DeepClone( ), ["extensionInput"] = new JsonObject { ["revision"] = view["revision"]!.DeepClone( ) } };
static async Task<JsonNode> Change( SearchClient client, JsonObject input )
{
    await client.Call( "extensions.manage", new JsonObject { ["extension"] = new JsonObject { ["action"] = "list" }, ["limit"] = 128 } );
    return (await client.Call( "extensions.manage", new JsonObject { ["extension"] = input, ["limit"] = 128 } )).Last( message => message["extensions"] is not null )["extensions"]!;
}
static async Task<double[]> Measure( SearchClient client, string query, int count )
{
    List<double> values = [];
    for ( var index = 0; index < count; index++ )
    {
        var timer = Stopwatch.StartNew( );
        await Search( client, query );
        values.Add( timer.Elapsed.TotalMilliseconds );
    }
    return values.Order( ).ToArray( );
}
static string Percentiles( double[] values ) => $"p50={values[( int ) (values.Length * .5)]:F1}, p95={values[( int ) (values.Length * .95)]:F1}, p99={values[( int ) (values.Length * .99)]:F1}";

static async Task Isolation( SearchClient client, string socket, string archive, Process daemon )
{
    var inspected = await Change( client, new JsonObject { ["action"] = "inspect", ["archivePath"] = archive } );
    var installed = await Change( client, new JsonObject { ["action"] = "install", ["archivePath"] = archive, ["digest"] = inspected["archiveDigest"]!.DeepClone( ), ["confirmed"] = true, ["grants"] = new JsonArray( ) } );
    var key = installed["packages"]!.AsArray( ).Single( package => package!["id"]!.GetValue<string>( ) == "org.commanddeck.search-isolation-test" )!["key"]!.GetValue<string>( );
    await Change( client, new JsonObject { ["action"] = "enable", ["key"] = key } );
    await Change( client, new JsonObject { ["action"] = "global-search", ["key"] = key, ["globalSearchEnabled"] = true, ["confirmed"] = true } );
    var sibling = View( await client.Call( "extension.open", Open( "extension:" + key + ":idle" ) ) );
    using var worker = await SearchClient.Connect( socket );
    var isolationRows = await Search( worker, "ext" + key[..10] + "-probe: isolation" );
    That( isolationRows.Any( Dynamic ), "Isolation probe returned no rows: " + string.Join( "\n", isolationRows ) );
    var isolation = isolationRows.Single( Dynamic );
    That( isolation["title"]!.GetValue<string>( ) == "Isolation passed", "Search escaped filesystem or network isolation" );
    foreach ( var method in new[] { "command.data", "command.broker", "effects.copy-text", "views.replace" } )
    {
        That( (await Search( worker, "probe " + method )).All( row => !Dynamic( row ) ), "A hostile search called a host method" );
    }
    var identity = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( Encoding.UTF8.GetBytes( "search:" + key ) ) )[..24].ToLowerInvariant( );
    var info = new ProcessStartInfo( "/usr/bin/systemctl" ) { RedirectStandardOutput = true };
    foreach ( var value in new[] { "--user", "show", "--value", "-p", "ControlGroup", "commanddeck-extensions-p" + identity + ".slice" } )
    {
        info.ArgumentList.Add( value );
    }
    using var lookup = Process.Start( info )!;
    var group = (await lookup.StandardOutput.ReadToEndAsync( )).Trim( );
    await lookup.WaitForExitAsync( );
    That( group.StartsWith( "/user.slice/", StringComparison.Ordinal ), "Missing search accounting group" );
    var cgroup = "/sys/fs/cgroup" + group;
    var timer = Stopwatch.StartNew( );
    var hung = Search( worker, "ext" + key[..10] + "-probe: hang" );
    long peak = 0;
    var observedDescendants = false;
    while ( !hung.IsCompleted )
    {
        foreach ( var file in Directory.EnumerateFiles( cgroup, "cgroup.procs", SearchOption.AllDirectories ) )
        {
            try
            {
                observedDescendants |= File.ReadAllLines( file ).Any( pid => File.ReadAllText( "/proc/" + pid + "/cmdline" ).Split( '\0' ).Contains( "descendant" ) );
            }
            catch ( IOException ) { }
        }
        peak = Math.Max( peak, long.Parse( await File.ReadAllTextAsync( cgroup + "/memory.current" ) ) );
        await Task.Delay( 15 );
    }
    var hungRows = await hung;
    That( hungRows.Count == 0 && timer.Elapsed < TimeSpan.FromSeconds( 3 ),
        $"Noncooperative search exceeded its deadline: {hungRows.Count} rows in {timer.Elapsed.TotalMilliseconds:F1} ms" );
    timer.Stop( );
    That( observedDescendants, "Hostile fixture did not spawn its descendant" );
    foreach ( var file in Directory.EnumerateFiles( cgroup, "cgroup.procs", SearchOption.AllDirectories ) )
    {
        That( string.IsNullOrWhiteSpace( await File.ReadAllTextAsync( file ) ), "Search descendant survived cancellation" );
    }
    await client.Call( "extension.present", Input( sibling ) );
    var ping = Input( sibling );
    ping["extensionInput"]!["handle"] = sibling["content"]!["actions"]![0]!["handle"]!.DeepClone( );
    sibling = View( await client.Call( "extension.action", ping ) );
    That( sibling["content"]!["title"]!.GetValue<string>( ) == "Sibling alive", "Search killed the foreground sibling" );
    var logs = await Change( client, new JsonObject { ["action"] = "logs", ["key"] = key, ["includeStderr"] = true } );
    That( !logs.ToJsonString( ).Contains( "QUERY-MUST-NOT-ESCAPE", StringComparison.Ordinal ), "Search diagnostics were shared with the foreground command" );
    daemon.Refresh( );
    Console.WriteLine( $"PASS hostile search: denied RPC, read-only artifact, no network/home, noncooperative descendant killed in {timer.Elapsed.TotalMilliseconds:F1}ms, sibling alive. Search memory peak sampled={peak / 1024}KiB; idle search processes=0; daemon RSS={daemon.WorkingSet64 / 1024}KiB." );
    await client.Call( "extension.close", new JsonObject { ["sessionId"] = sibling["sessionId"]!.DeepClone( ) } );
}

static class SearchJourney
{
    public static async Task<JsonNode> FindGlobalRow( Func<Task<List<JsonNode>>> search, Func<JsonNode, bool> dynamic )
    {
        // Individual global providers may miss their 300 ms budget. Lease setup is
        // a functional check; latency samples and the final typing query stay separate.
        for ( var attempt = 0; attempt < 5; attempt++ )
        {
            var rows = (await search( )).Where( dynamic ).ToArray( );
            if ( rows.Length > 0 )
            {
                if ( rows.Length != 1 )
                {
                    throw new InvalidOperationException( "Global search returned duplicate extension rows" );
                }

                return rows[0];
            }
        }

        throw new InvalidOperationException( "Global search returned no extension row across five independent queries" );
    }
}

sealed class SearchClient : IDisposable
{
    private readonly Socket _socket;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private int _request, _sequence;
    private long _revision;
    private SearchClient( Socket socket )
    {
        _socket = socket;
        var stream = new NetworkStream( socket, false );
        _reader = new StreamReader( stream );
        _writer = new StreamWriter( stream, new UTF8Encoding( false ) ) { AutoFlush = true };
    }
    public static async Task<SearchClient> Connect( string path )
    {
        var socket = new Socket( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        await socket.ConnectAsync( new UnixDomainSocketEndPoint( path ) );
        return new( socket );
    }
    public async Task<List<JsonNode>> Burst( string prefix )
    {
        HashSet<int> pending = [];
        for ( var index = 0; index < 12; index++ )
        {
            var id = ++_request;
            pending.Add( id );
            await _writer.WriteLineAsync( new JsonObject { ["version"] = 3, ["requestId"] = id, ["operation"] = "search", ["query"] = prefix + (index == 11 ? "Meeting" : "Meet" + index), ["limit"] = 50 }.ToJsonString( ) );
            await Task.Delay( 25 );
        }
        using var deadline = new CancellationTokenSource( TimeSpan.FromSeconds( 5 ) );
        List<JsonNode> latest = [];
        while ( pending.Count > 0 )
        {
            var messageText = await _reader.ReadLineAsync( deadline.Token );
            if ( messageText is null )
            {
                throw new EndOfStreamException( );
            }

            var message = JsonNode.Parse( messageText )!;
            var id = message["requestId"]!.GetValue<int>( );
            if ( id == _request && message["type"]?.GetValue<string>( ) == "error" )
            {
                throw new InvalidOperationException( "Final typing query was rejected: " + message );
            }
            if ( id == _request && message["result"] is { } result )
            {
                latest.Add( result );
            }
            if ( message["type"]?.GetValue<string>( ) == "done" )
            {
                pending.Remove( id );
            }
        }
        return latest;
    }
    public async Task<List<JsonNode>> Call( string operation, JsonObject fields, bool error = false )
    {
        var id = ++_request;
        fields["version"] = 3;
        fields["requestId"] = id;
        fields["operation"] = operation;
        if ( operation == "extension.open" )
        {
            _sequence = 0;
        }
        if ( operation.StartsWith( "extension.", StringComparison.Ordinal ) )
        {
            fields["extensionInput"] ??= new JsonObject( );
        }
        if ( operation is "extension.present" or "extension.action" )
        {
            fields["extensionInput"]!["sequence"] = ++_sequence;
        }
        if ( operation == "extensions.manage" )
        {
            fields["extension"]!["expectedRevision"] = _revision;
        }
        await _writer.WriteLineAsync( fields.ToJsonString( ) );
        using var deadline = new CancellationTokenSource( TimeSpan.FromSeconds( 15 ) );
        List<JsonNode> messages = [];
        while ( true )
        {
            var messageText = await _reader.ReadLineAsync( deadline.Token );
            if ( messageText is null )
            {
                throw new EndOfStreamException( );
            }

            var message = JsonNode.Parse( messageText )!;
            messages.Add( message );
            if ( message["extensions"]?["revision"] is { } revision )
            {
                _revision = revision.GetValue<long>( );
            }
            if ( message["requestId"]?.GetValue<int>( ) == id && message["type"]?.GetValue<string>( ) == "done" )
            {
                break;
            }
        }
        var failed = messages.Any( message => message["type"]?.GetValue<string>( ) == "error" || message["extensionSession"]?["status"]?.GetValue<string>( ) == "failed" );
        if ( failed != error )
        {
            throw new InvalidOperationException( operation + ": " + string.Join( "\n", messages ) );
        }
        return messages;
    }
    public void Dispose( )
    {
        _writer.Dispose( );
        _reader.Dispose( );
        _socket.Dispose( );
    }
}
