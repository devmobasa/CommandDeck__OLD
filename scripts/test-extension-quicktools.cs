#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

if ( args.Length != 2 )
{
    throw new ArgumentException( "Usage: dotnet scripts/test-extension-quicktools.cs HOST PACKAGE" );
}
var host = Path.GetFullPath( args[0] );
var package = Path.GetFullPath( args[1] );
var repo = Path.GetFullPath( Path.Combine( AppContext.GetData( "EntryPointFileDirectoryPath" )!.ToString( )!, ".." ) );
var root = Directory.CreateTempSubdirectory( "commanddeck-quicktools-" ).FullName;
var config = Path.Combine( root, "config.json" );
var socket = Path.Combine( root, "daemon.sock" );
var bin = Path.Combine( root, "bin" );
Process? daemon = null;
Task<string>? stdout = null;
Task<string>? stderr = null;
try
{
    await Exec( "dotnet", "publish", Path.Combine( repo, "scripts/extension-clipboard-sink.cs" ), "-o", bin );
    File.CreateSymbolicLink( Path.Combine( bin, "uwsm-app" ), Path.Combine( bin, "wl-copy" ) );
    File.CreateSymbolicLink( Path.Combine( bin, "xdg-open" ), Path.Combine( bin, "wl-copy" ) );
    await File.WriteAllTextAsync( config, new JsonObject
    {
        ["storage"] = new JsonObject { ["databasePath"] = Path.Combine( root, "catalog.db" ) },
        ["indexing"] = new JsonObject { ["roots"] = new JsonArray( ), ["indexDesktopApplications"] = false, ["indexOmarchyCommands"] = false },
        ["ai"] = new JsonObject { ["enabled"] = false },
        ["agent"] = new JsonObject { ["runtimeDirectory"] = Path.Combine( root, "runtime" ), ["stateDirectory"] = Path.Combine( root, "state" ) }
    }.ToJsonString( ) );
    var start = new ProcessStartInfo( host ) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach ( var arg in new[] { "daemon", "--config", config, "--socket", socket } )
    {
        start.ArgumentList.Add( arg );
    }
    start.Environment["PATH"] = bin + ":" + Environment.GetEnvironmentVariable( "PATH" );
    start.Environment["COMMANDDECK_TEST_EFFECTS"] = root;
    daemon = Process.Start( start )!;
    stdout = daemon.StandardOutput.ReadToEndAsync( );
    stderr = daemon.StandardError.ReadToEndAsync( );
    await Wait( ( ) => File.Exists( socket ), "daemon startup" );
    async Task<JsonNode> Cli( params string[] values ) => JsonNode.Parse( await Exec( host, ["extensions", .. values, "--socket", socket, "--json"] ) )!;
    var review = await Cli( "inspect", "--archive", package );
    var inventory = await Cli( "install", "--archive", package, "--digest", review["archiveDigest"]!.GetValue<string>( ), "--yes" );
    var key = inventory["packages"]![0]!["key"]!.GetValue<string>( );
    await Cli( "enable", "org.commanddeck.quick-tools" );
    await Cli( "enable" );
    using var connection = new Socket( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
    await connection.ConnectAsync( new UnixDomainSocketEndPoint( socket ) );
    using var stream = new NetworkStream( connection, false );
    using var reader = new StreamReader( stream );
    using var writer = new StreamWriter( stream, new UTF8Encoding( false ) ) { AutoFlush = true };
    var sequence = 0;
    async Task<List<JsonNode>> Call( string operation, JsonObject fields, bool error = false )
    {
        var id = ++sequence;
        fields["version"] = 3;
        fields["requestId"] = id;
        fields["operation"] = operation;
        await writer.WriteLineAsync( fields.ToJsonString( ) );
        List<JsonNode> messages = [];
        using var deadline = new CancellationTokenSource( TimeSpan.FromSeconds( 20 ) );
        while ( true )
        {
            var messageText = await reader.ReadLineAsync( deadline.Token );
            if ( messageText is null )
            {
                throw new IOException( "Daemon disconnected" );
            }

            var message = JsonNode.Parse( messageText )!;
            messages.Add( message );
            if ( message["requestId"]!.GetValue<int>( ) == id && message["type"]!.GetValue<string>( ) == "done" )
            {
                break;
            }
        }
        Check( messages.Any( m => m["type"]!.GetValue<string>( ) == "error" ) == error, string.Join( '\n', messages ) );
        return messages;
    }
    JsonObject Open( string command ) => new( )
    {
        ["resultId"] = "extension:" + key + ":" + command,
        ["extensionInput"] = new JsonObject { ["features"] = new JsonArray( "commands@1", "commands.no-view@1", "effects.copy-text@1", "assets.png@1", "views.list@1", "views.detail@1", "views.stack@1", "actions.open-url@1" ) }
    };
    JsonObject Input( JsonNode view, int inputSequence, string? handle = null, string? item = null ) => new( )
    {
        ["sessionId"] = view["sessionId"]!.GetValue<string>( ),
        ["extensionInput"] = new JsonObject
        {
            ["sequence"] = inputSequence,
            ["revision"] = view["revision"]!.GetValue<long>( ),
            ["handle"] = handle,
            ["itemId"] = item
        }
    };
    JsonNode View( List<JsonNode> messages ) => messages.Select( m => m["extensionSession"] ).First( s => s?["status"]?.GetValue<string>( ) == "view" )!;
    var clipboard = Path.Combine( root, "clipboard" );
    var urls = Path.Combine( root, "opened-urls" );
    await Call( "search", new( )
    {
        ["query"] = "UUID"
    } );
    Check( !File.Exists( clipboard ), "Discovery copied text" );
    Check( !File.Exists( urls ), "Discovery opened a URL" );
    await Call( "extension.open", Open( "copy" ), true );
    Check( !File.Exists( clipboard ), "No-view copy bypassed its grant" );
    await Cli( "grants", "org.commanddeck.quick-tools", "--allow", "clipboard.write", "--yes" );
    for ( var iteration = 0; iteration < 3; iteration++ )
    {
        var completed = await Call( "extension.open", Open( "copy" ) );
        Check( completed.Any( m => m["extensionSession"]?["status"]?.GetValue<string>( ) == "completed" ), "No terminal completion" );
        Check( !completed.Any( m => m["extensionSession"]?["status"]?.GetValue<string>( ) == "view" ), "No-view command published a view" );
        Check( Guid.TryParse( await File.ReadAllTextAsync( clipboard ), out _ ), "No-view clipboard was not a UUID" );
    }
    Check( File.ReadAllLines( Path.Combine( root, "copied" ) ).Length == 3, "No-view effects replayed" );
    var list = View( await Call( "extension.open", Open( "reference" ) ) );
    var row = list["content"]!["items"]![0]!;
    Check( row["imageSource"]!.GetValue<string>( ).StartsWith( "data:image/png;base64,", StringComparison.Ordinal ), "List image data missing" );
    Check( row["asset"] is null, "List asset escaped as a path" );
    await Call( "extension.present", Input( list, 1 ) );
    var detail = View( await Call( "extension.action", Input( list, 2, row["actions"]![0]!["handle"]!.GetValue<string>( ), "uuid" ) ) );
    Check( detail["content"]!["imageSource"]!.GetValue<string>( ) == row["imageSource"]!.GetValue<string>( ), "Detail asset mismatch" );
    await Call( "extension.present", Input( detail, 3 ) );
    var handle = detail["content"]!["actions"]![0]!["handle"]!.GetValue<string>( );
    await Call( "extension.action", Input( detail, 4, handle ), true );
    Check( !File.Exists( urls ), "URL bypassed its grant" );
    await Cli( "grants", "org.commanddeck.quick-tools", "--allow", "clipboard.write,desktop.open-url", "--yes" );
    await Call( "extension.action", Input( detail, 5, handle ), true );
    list = View( await Call( "extension.open", Open( "reference" ) ) );
    await Call( "extension.present", Input( list, 1 ) );
    detail = View( await Call( "extension.action", Input( list, 2, list["content"]!["items"]![0]!["actions"]![0]!["handle"]!.GetValue<string>( ), "uuid" ) ) );
    await Call( "extension.present", Input( detail, 3 ) );
    handle = detail["content"]!["actions"]![0]!["handle"]!.GetValue<string>( );
    await Call( "extension.action", Input( detail, 4, handle ) );
    Check( (await File.ReadAllTextAsync( urls )).Trim( ) == "https://www.rfc-editor.org/rfc/rfc9562.html", "URL target changed" );
    await Call( "extension.action", Input( detail, 4, handle ), true );
    Check( File.ReadAllLines( urls ).Length == 1, "URL replayed" );
    await Call( "extension.close", Input( detail, 0 ) );
    // The host owns command modes, even when a child SDK disagrees with its manifest.
    foreach ( var (command, mode) in new[] { ("reference", "no-view"), ("copy", "view") } )
    {
        var id = "org.commanddeck.quick-tools-" + command;
        var variant = QuickToolsFixture.Variant( package, Path.Combine( root, command + ".cdext" ), id, command, mode );
        var inspected = await Cli( "inspect", "--archive", variant );
        var installed = await Cli( "install", "--archive", variant, "--digest", inspected["archiveDigest"]!.GetValue<string>( ), "--allow", "clipboard.write,desktop.open-url", "--yes" );
        await Cli( "enable", id );
        var variantKey = installed["packages"]!.AsArray( ).Single( p => p!["id"]!.GetValue<string>( ) == id )!["key"]!.GetValue<string>( );
        var request = Open( command );
        request["resultId"] = "extension:" + variantKey + ":" + command;
        await Call( "extension.open", request, true );
    }
    Check( File.ReadAllLines( Path.Combine( root, "copied" ) ).Length == 3, "A view command used invocation-only effects" );
    Console.WriteLine( "PASS Quick Tools: inert discovery, denied grants, three no-view completions/reopens, exact clipboard, List/Detail assets, exact URL, revocation and replay rejection." );
}
finally
{
    if ( daemon is not null )
    {
        if ( !daemon.HasExited )
        {
            await Exec( "kill", "-TERM", daemon.Id.ToString( ) );
            await daemon.WaitForExitAsync( );
        }
        Directory.CreateDirectory( Path.Combine( repo, "artifacts/extensions" ) );
        await File.WriteAllTextAsync( Path.Combine( repo, "artifacts/extensions/quicktools-daemon.log" ), await stdout! + await stderr! );
        daemon.Dispose( );
    }
    Directory.Delete( root, true );
}

static void Check( bool condition, string message )
{
    if ( !condition )
    {
        throw new InvalidOperationException( message );
    }
}
static async Task Wait( Func<bool> condition, string message )
{
    var until = DateTime.UtcNow.AddSeconds( 8 );
    while ( !condition( ) )
    {
        Check( DateTime.UtcNow < until, message );
        await Task.Delay( 25 );
    }
}
static async Task<string> Exec( string executable, params string[] arguments )
{
    var start = new ProcessStartInfo( executable ) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach ( var arg in arguments )
    {
        start.ArgumentList.Add( arg );
    }
    using var process = Process.Start( start )!;
    var output = process.StandardOutput.ReadToEndAsync( );
    var error = process.StandardError.ReadToEndAsync( );
    using var deadline = new CancellationTokenSource( TimeSpan.FromMinutes( 2 ) );
    try
    {
        await process.WaitForExitAsync( deadline.Token );
    }
    catch
    {
        process.Kill( true );
        throw;
    }
    Check( process.ExitCode == 0, executable + ": " + await error + await output );
    return await output;
}

static class QuickToolsFixture
{
    public static string Variant( string source, string destination, string id, string command, string mode )
    {
        using var input = ZipFile.OpenRead( source );
        using var output = ZipFile.Open( destination, ZipArchiveMode.Create );
        foreach ( var entry in input.Entries )
        {
            var copy = output.CreateEntry( entry.FullName );
            copy.ExternalAttributes = entry.ExternalAttributes;
            using var content = entry.Open( );
            using var target = copy.Open( );
            if ( entry.FullName == "commanddeck.extension.json" )
            {
                var manifest = JsonNode.Parse( content )!;
                manifest["id"] = id;
                manifest["commands"]!.AsArray( ).Single( c => c!["id"]!.GetValue<string>( ) == command )!["mode"] = mode;
                target.Write( Encoding.UTF8.GetBytes( manifest.ToJsonString( ) ) );
            }
            else
            {
                content.CopyTo( target );
            }
        }
        return destination;
    }
}
