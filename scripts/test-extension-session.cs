#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:property EnableNETAnalyzers=false
#:include ./test-extension-session.clients.cs

using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

if ( args.Length < 2 )
{
    throw new ArgumentException( "Usage: dotnet scripts/test-extension-session.cs HOST PACKAGE [--wayland]" );
}
await Journey.RunAsync( Path.GetFullPath( args[0] ), Path.GetFullPath( args[1] ), args.Contains( "--wayland", StringComparer.Ordinal ) );

static class Journey
{
    public static readonly string Repo = Path.GetFullPath( Path.Combine( AppContext.GetData( "EntryPointFileDirectoryPath" )!.ToString( )!, ".." ) );
    public static readonly string[] Features = ["commands@1", "views.list@1", "views.detail@1", "views.stack@1", "views.pagination@1", "actions.copy-text@1"];
    public static void Check( bool condition, string message )
    {
        if ( !condition )
        {
            throw new InvalidOperationException( message );
        }
    }
    public static async Task<string> Exec( string executable, params string[] arguments )
    {
        var start = new ProcessStartInfo( executable ) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach ( var argument in arguments )
        {
            start.ArgumentList.Add( argument );
        }
        using var process = Process.Start( start )!;
        var output = process.StandardOutput.ReadToEndAsync( );
        var error = process.StandardError.ReadToEndAsync( );
        using var timeout = new CancellationTokenSource( TimeSpan.FromMinutes( 3 ) );
        try
        {
            await process.WaitForExitAsync( timeout.Token );
        }
        catch
        {
            process.Kill( true );
            throw;
        }
        Check( process.ExitCode == 0, $"{executable} {string.Join( ' ', arguments )}: {await error} {await output}" );
        return await output;
    }
    public static async Task Wait( Func<Task<bool>> predicate, string label, int seconds = 8 )
    {
        var deadline = DateTime.UtcNow.AddSeconds( seconds );
        while ( !await predicate( ) )
        {
            Check( DateTime.UtcNow < deadline, label );
            await Task.Delay( 25 );
        }
    }
    static async Task<HashSet<string>> Units( ) => (await Exec( "systemctl", "--user", "list-units", "--all", "--plain", "--no-legend", "commanddeck-extension-*.service" ))
        .Split( '\n', StringSplitOptions.RemoveEmptyEntries ).Select( line => line.Split( ' ', StringSplitOptions.RemoveEmptyEntries )[0] ).ToHashSet( StringComparer.Ordinal );
    public static JsonObject Input( string session, long sequence, long revision, string? handle = null, string? item = null ) => new( )
    {
        ["sessionId"] = session,
        ["extensionInput"] = new JsonObject { ["sequence"] = sequence, ["revision"] = revision, ["handle"] = handle, ["itemId"] = item }
    };
    static async Task ControllerLifecycle( string host, string address, string root, string command )
    {
        var directory = Path.Combine( root, "controller-lifecycle" );
        Directory.CreateDirectory( directory );
        var fixture = Path.Combine( directory, "shell.qml" );
        File.Copy( Path.Combine( Repo, "tests/CommandDeck.ExtensionJourney/lifecycle.qml" ), fixture );
        await Exec( "cp", "-r", Path.Combine( Repo, "qml", "common" ), Path.Combine( directory, "plugin" ) );
        var start = new ProcessStartInfo( "qs" ) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach ( var argument in new[] { "--no-color", "-p", fixture } )
        {
            start.ArgumentList.Add( argument );
        }
        start.Environment["COMMANDDECK_TEST_HOST"] = host;
        start.Environment["COMMANDDECK_TEST_SOCKET"] = address;
        start.Environment["COMMANDDECK_TEST_COMMAND"] = command;
        using var process = Process.Start( start )!;
        var output = process.StandardOutput.ReadToEndAsync( );
        var errors = process.StandardError.ReadToEndAsync( );
        using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 70 ) );
        try
        {
            await process.WaitForExitAsync( timeout.Token );
        }
        catch
        {
            process.Kill( true );
            await process.WaitForExitAsync( );
            throw;
        }
        var log = await output + await errors;
        Check( process.ExitCode == 0 && log.Contains( "PASS connected controller", StringComparison.Ordinal ), log );
        Console.WriteLine( "PASS connected controller: 20 immediate close/reopen cycles" );
    }
    public static async Task RunAsync( string host, string package, bool wayland )
    {
        var baseline = await Units( );
        var root = Directory.CreateTempSubdirectory( "commanddeck-session-" ).FullName;
        var effects = Path.Combine( root, "effects" );
        Directory.CreateDirectory( effects );
        var clipboard = Path.Combine( effects, "clipboard" );
        var copied = Path.Combine( effects, "copied" );
        var hold = Path.Combine( effects, "hold-copy" );
        var started = Path.Combine( effects, "copy-started" );
        var bin = Path.Combine( root, "bin" );
        await Exec( "dotnet", "publish", Path.Combine( Repo, "scripts/extension-clipboard-sink.cs" ), "-o", bin );
        var address = Path.Combine( root, "daemon.sock" );
        var config = Path.Combine( root, "config.json" );
        await File.WriteAllTextAsync( config, new JsonObject
        {
            ["storage"] = new JsonObject { ["databasePath"] = Path.Combine( root, "catalog.db" ) },
            ["indexing"] = new JsonObject { ["roots"] = new JsonArray( ), ["indexDesktopApplications"] = false, ["indexOmarchyCommands"] = false },
            ["ai"] = new JsonObject { ["enabled"] = false },
            ["agent"] = new JsonObject { ["runtimeDirectory"] = Path.Combine( root, "runtime" ), ["stateDirectory"] = Path.Combine( root, "state" ) }
        }.ToJsonString( ) );
        var start = new ProcessStartInfo( host ) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach ( var argument in new[] { "daemon", "--config", config, "--socket", address } )
        {
            start.ArgumentList.Add( argument );
        }
        start.Environment["PATH"] = bin + ":" + Environment.GetEnvironmentVariable( "PATH" );
        start.Environment["COMMANDDECK_TEST_EFFECTS"] = effects;
        using var daemon = Process.Start( start )!;
        var stdout = daemon.StandardOutput.ReadToEndAsync( );
        var stderr = daemon.StandardError.ReadToEndAsync( );
        var clients = new List<Client>( );
        Client Connect( )
        {
            var connection = new Client( address );
            clients.Add( connection );
            return connection;
        }
        async Task<JsonNode> Cli( params string[] arguments ) => JsonNode.Parse( await Exec( host, ["extensions", .. arguments, "--socket", address, "--json"] ) )!;
        async Task Clean( string label ) => await Wait( async ( ) => (await Units( )).SetEquals( baseline ), label );
        try
        {
            await Wait( ( ) => Task.FromResult( File.Exists( address ) || daemon.HasExited ), "Daemon startup" );
            Check( !daemon.HasExited, "Daemon failed to start" );
            var review = await Cli( "inspect", "--archive", package );
            var inventory = await Cli( "install", "--archive", package, "--digest", review["archiveDigest"]!.GetValue<string>( ), "--yes" );
            var packageId = inventory["packages"]![0]!["id"]!.GetValue<string>( );
            var packageKey = inventory["packages"]![0]!["key"]!.GetValue<string>( );
            await Cli( "enable", packageId );
            await Cli( "enable" );
            var client = Connect( );
            var rows = await client.Call( "search", new( )
            {
                ["query"] = "UUID"
            } );
            var result = rows.Select( message => message["result"] ).First( row => row?["kind"]?.GetValue<string>( ) == "extension-command" )!;
            Check( result["actions"]![0]!["kind"]!.GetValue<string>( ) == "extension-open", "Missing launcher activation" );
            await Clean( "Discovery started a child" );
            JsonObject OpenInput( string[] features ) => new( )
            {
                ["resultId"] = result["id"]!.GetValue<string>( ),
                ["extensionInput"] = new JsonObject { ["features"] = new JsonArray( features.Select( value => ( JsonNode? ) JsonValue.Create( value ) ).ToArray( ) ) }
            };
            await client.Call( "extension.open", OpenInput( ["commands@1"] ), true );
            await Clean( "Unsupported renderer started child" );
            async Task<JsonNode> Open( Client connection )
            {
                var messages = await connection.Call( "extension.open", OpenInput( Features ) );
                var view = messages.Select( message => message["extensionSession"] ).First( state => state?["status"]?.GetValue<string>( ) == "view" )!;
                Check( view["content"]!["items"]!.AsArray( ).Count == 8, "UUID row count" );
                await connection.Call( "extension.present", Input( view["sessionId"]!.GetValue<string>( ), 1, view["revision"]!.GetValue<long>( ) ) );
                return view;
            }
            var view = await Open( client );
            var session = view["sessionId"]!.GetValue<string>( );
            Check( !(await Units( )).SetEquals( baseline ), "No isolated session" );
            JsonObject Copy( JsonNode snapshot, long sequence ) => Input( snapshot["sessionId"]!.GetValue<string>( ), sequence, snapshot["revision"]!.GetValue<long>( ),
                snapshot["content"]!["items"]![0]!["actions"]![0]!["handle"]!.GetValue<string>( ), snapshot["content"]!["items"]![0]!["id"]!.GetValue<string>( ) );
            await client.Call( "extension.action", Copy( view, 2 ), true );
            Check( !File.Exists( clipboard ), "Copy without grant" );
            var foreign = Connect( );
            await foreign.Call( "extension.action", Copy( view, 2 ), true );
            await foreign.Call( "extension.close", Input( session, 0, 0 ), true );
            await Cli( "grants", packageId, "--allow", "clipboard.write", "--yes" );
            await Clean( "Grant cleanup" );
            await client.Call( "extension.action", Copy( view, 3 ), true );
            await ControllerLifecycle( host, address, root, result["id"]!.GetValue<string>( ) );
            await Clean( "Controller rapid reopen cleanup" );
            if ( wayland )
            {
                await Wayland.Run( host, address, clipboard, root );
                await Clean( "Wayland cleanup" );
                File.Delete( copied );
            }
            view = await Open( client );
            session = view["sessionId"]!.GetValue<string>( );
            var copyClock = Stopwatch.StartNew( );
            await client.Call( "extension.action", Copy( view, 2 ) );
            Check( copyClock.Elapsed < TimeSpan.FromSeconds( 2 ), "Copy waited for the background clipboard owner's inherited stderr" );
            Check( await File.ReadAllTextAsync( clipboard ) == view["content"]!["items"]![0]!["title"]!.GetValue<string>( ), "Exact displayed copy" );
            await client.Call( "extension.action", Copy( view, 2 ), true );
            Check( File.ReadAllLines( copied ).Length == 1, "Replayed effect" );
            var messages = await client.Call( "extension.action", Input( session, 3, 1, view["content"]!["actions"]![0]!["handle"]!.GetValue<string>( ) ) );
            var replacement = messages.Select( message => message["extensionSession"] ).First( state => state?["status"]?.GetValue<string>( ) == "view" )!;
            Check( !JsonNode.DeepEquals( view["content"]!["items"], replacement["content"]!["items"] ), "Callback did not regenerate" );
            await client.Call( "extension.action", Copy( view, 4 ) );
            Check( await File.ReadAllTextAsync( clipboard ) == view["content"]!["items"]![0]!["title"]!.GetValue<string>( ), "Redraw retargeted old click" );
            await client.Call( "extension.present", Input( session, 5, 2 ) );
            await client.Call( "extension.action", Copy( view, 6 ), true );
            File.WriteAllText( hold, "" );
            File.Delete( started );
            var copyRequest = await client.Send( "extension.action", Copy( replacement, 7 ) );
            await Wait( ( ) => Task.FromResult( File.Exists( started ) ), "Effect never started" );
            inventory = await Cli( "list" );
            var disableRequest = await foreign.Send( "extensions.manage", new( )
            {
                ["extension"] = new JsonObject { ["action"] = "disable", ["key"] = packageKey, ["expectedRevision"] = inventory["revision"]!.DeepClone( ) }
            } );
            await Task.Delay( 100 );
            Check( !foreign.HasData, "Disable committed while started effect held lease" );
            File.Delete( hold );
            await client.Done( copyRequest );
            Client.NoErrors( await foreign.Done( disableRequest ) );
            await Clean( "Disable cleanup" );
            Check( await File.ReadAllTextAsync( clipboard ) == replacement["content"]!["items"]![0]!["title"]!.GetValue<string>( ), "Race effect payload" );
            await client.Call( "extension.open", OpenInput( Features ), true );
            await Cli( "enable", packageId );
            var reopened = await Open( client );
            Check( reopened["sessionId"]!.GetValue<string>( ) != session, "Reused authority" );
            await client.Call( "extension.action", Copy( view, 8 ), true );
            await client.Call( "extension.close", Input( reopened["sessionId"]!.GetValue<string>( ), 0, 0 ) );
            await Clean( "Close cleanup" );
            view = await Open( client );
            session = view["sessionId"]!.GetValue<string>( );
            static JsonNode ChangedView( List<JsonNode> response ) => response.Select( message => message["extensionSession"] )
                .First( state => state?["status"]?.GetValue<string>( ) == "view" )!;
            var detail = ChangedView( await client.Call( "extension.action", Input( session, 2, view["revision"]!.GetValue<long>( ),
                view["content"]!["items"]![0]!["actions"]![1]!["handle"]!.GetValue<string>( ), view["content"]!["items"]![0]!["id"]!.GetValue<string>( ) ) ) );
            Check( detail["content"]!["kind"]!.GetValue<string>( ) == "detail", "Details did not push" );
            Check( detail["content"]!["depth"]!.GetValue<int>( ) == 2, "Missing stack depth" );
            await client.Call( "extension.present", Input( session, 3, detail["revision"]!.GetValue<long>( ) ) );
            await client.Call( "extension.action", Input( session, 4, detail["revision"]!.GetValue<long>( ), detail["content"]!["actions"]![0]!["handle"]!.GetValue<string>( ) ) );
            Check( await File.ReadAllTextAsync( clipboard ) == view["content"]!["items"]![0]!["title"]!.GetValue<string>( ), "Detail copy payload" );
            var parent = ChangedView( await client.Call( "extension.action", Input( session, 5, detail["revision"]!.GetValue<long>( ), detail["content"]!["backHandle"]!.GetValue<string>( ) ) ) );
            Check( JsonNode.DeepEquals( parent["content"]!["items"]![0]!["id"], view["content"]!["items"]![0]!["id"] ), "Back lost parent content" );
            await client.Call( "extension.present", Input( session, 6, parent["revision"]!.GetValue<long>( ) ) );
            await client.Call( "extension.action", Input( session, 7, detail["revision"]!.GetValue<long>( ), detail["content"]!["actions"]![0]!["handle"]!.GetValue<string>( ) ), true );
            var page = ChangedView( await client.Call( "extension.action", Input( session, 8, parent["revision"]!.GetValue<long>( ), parent["content"]!["nextPage"]!.GetValue<string>( ) ) ) );
            Check( !JsonNode.DeepEquals( page["content"]!["items"]![0]!["id"], parent["content"]!["items"]![0]!["id"] ), "Next page did not replace items" );
            Check( page["content"]!["items"]!.AsArray( ).Count == 8, "Pages were appended" );
            await client.Call( "extension.present", Input( session, 9, page["revision"]!.GetValue<long>( ) ) );
            await client.Call( "extension.action", Input( session, 10, parent["revision"]!.GetValue<long>( ), parent["content"]!["nextPage"]!.GetValue<string>( ) ), true );
            var previous = ChangedView( await client.Call( "extension.action", Input( session, 11, page["revision"]!.GetValue<long>( ), page["content"]!["previousPage"]!.GetValue<string>( ) ) ) );
            Check( JsonNode.DeepEquals( previous["content"]!["items"]![0]!["id"], parent["content"]!["items"]![0]!["id"] ), "Previous page lost items" );
            await client.Call( "extension.present", Input( session, 12, previous["revision"]!.GetValue<long>( ) ) );
            await client.Call( "extension.action", Input( session, 13, previous["revision"]!.GetValue<long>( ), previous["content"]!["actions"]![0]!["handle"]!.GetValue<string>( ) ) );
            await client.Call( "extension.close", Input( session, 0, 0 ) );
            await Clean( "Navigation cleanup" );
            Console.WriteLine( "PASS connected navigation: push Detail, copy, Back, Next/Previous pages, retired detail/page handles, callback after restored parent." );
            await Open( client );
            client.Dispose( );
            clients.Remove( client );
            await Clean( "Disconnect cleanup" );
            client = Connect( );
            await Open( client );
            await Cli( "disable" );
            await Clean( "Master disable cleanup" );
            await Cli( "enable" );
            // Updating one package must leave a different package's foreground command usable.
            var siblingArchive = Variant( package, Path.Combine( root, "sibling.cdext" ), "org.commanddeck.uuid-sibling", "0.1.0" );
            var siblingReview = await Cli( "inspect", "--archive", siblingArchive );
            await Cli( "install", "--archive", siblingArchive, "--digest", siblingReview["archiveDigest"]!.GetValue<string>( ), "--yes" );
            await Cli( "enable", "org.commanddeck.uuid-sibling" );
            var siblingResult = (await foreign.Call( "search", new( )
            {
                ["query"] = "UUID"
            } )).Select( message => message["result"] )
                .First( row => row?["kind"]?.GetValue<string>( ) == "extension-command" && row["id"]!.GetValue<string>( ) != result["id"]!.GetValue<string>( ) )!;
            var siblingInput = OpenInput( Features );
            siblingInput["resultId"] = siblingResult["id"]!.GetValue<string>( );
            var siblingMessages = await foreign.Call( "extension.open", siblingInput );
            var siblingView = siblingMessages.Select( message => message["extensionSession"] ).First( state => state?["status"]?.GetValue<string>( ) == "view" )!;
            var siblingSession = siblingView["sessionId"]!.GetValue<string>( );
            await foreign.Call( "extension.present", Input( siblingSession, 1, 1 ) );
            var siblingUnits = await Units( );
            view = await Open( client );
            var updatedVersion = review["manifest"]!["version"]!.GetValue<string>( ) == "0.1.1" ? "0.1.2" : "0.1.1";
            var updatedArchive = Variant( package, Path.Combine( root, "updated.cdext" ), packageId, updatedVersion );
            var updatedReview = await Cli( "inspect", "--archive", updatedArchive );
            await Cli( "install", "--archive", updatedArchive, "--digest", updatedReview["archiveDigest"]!.GetValue<string>( ), "--yes" );
            await Wait( async ( ) => (await Units( )).SetEquals( siblingUnits ), "Update stopped sibling or leaked old session" );
            await client.Call( "extension.action", Copy( view, 2 ), true );
            await Open( client );
            inventory = await Cli( "list" );
            await client.Call( "extensions.manage", new( )
            {
                ["extension"] = new JsonObject
                {
                    ["action"] = "rollback",
                    ["key"] = packageKey,
                    ["confirmed"] = true,
                    ["digest"] = review["archiveDigest"]!.DeepClone( ),
                    ["expectedRevision"] = inventory["revision"]!.DeepClone( )
                }
            } );
            await Wait( async ( ) => (await Units( )).SetEquals( siblingUnits ), "Rollback cleanup" );
            await foreign.Call( "extension.action", Input( siblingSession, 2, 1, siblingView["content"]!["actions"]![0]!["handle"]!.GetValue<string>( ) ) );
            await foreign.Call( "extension.close", Input( siblingSession, 0, 0 ) );
            await Clean( "Sibling cleanup" );
            await Open( client );
            await Cli( "remove", packageId, "--yes" );
            await Clean( "Remove cleanup" );
            await client.Call( "ping", new( ) );
            var hostileOutput = Path.Combine( root, "hostile" );
            await Exec( "dotnet", "publish", Path.Combine( Repo, "scripts/extension-hostile.cs" ), "-r", "linux-x64", "-o", hostileOutput );
            foreach ( var behavior in new[] { "crash", "hang", "malformed", "flood" } )
            {
                var hostileArchive = Variant( package, Path.Combine( root, behavior + ".cdext" ), "org.commanddeck.hostile-" + behavior, "0.1.0", Path.Combine( hostileOutput, "extension-hostile" ), behavior );
                var inspected = await Cli( "inspect", "--archive", hostileArchive );
                await Cli( "install", "--archive", hostileArchive, "--digest", inspected["archiveDigest"]!.GetValue<string>( ), "--yes" );
                await Cli( "enable", "org.commanddeck.hostile-" + behavior );
                var search = await client.Call( "search", new( )
                {
                    ["query"] = behavior
                } );
                var hostileResult = search.Select( message => message["result"] ).First( row => row?["kind"]?.GetValue<string>( ) == "extension-command" && row["title"]!.GetValue<string>( ) == behavior )!;
                var hostileInput = OpenInput( Features );
                hostileInput["resultId"] = hostileResult["id"]!.GetValue<string>( );
                var failed = await client.Call( "extension.open", hostileInput, true );
                Check( failed.Any( message => message["extensionSession"]?["status"]?.GetValue<string>( ) == "failed" ), behavior + " did not publish recovery state" );
                await Clean( behavior + " descendants survived" );
                await client.Call( "ping", new( ) );
            }
            if ( wayland )
            {
                await Wayland.Run( host, address, clipboard, root, true );
                await Clean( "Wayland failure recovery cleanup" );
            }
            await OpenSiblingAndStopDaemon( );
            async Task OpenSiblingAndStopDaemon( )
            {
                await foreign.Call( "extension.open", siblingInput );
                using var signal = Process.Start( "kill", $"-TERM {daemon.Id}" )!;
                await signal.WaitForExitAsync( );
                await Wait( ( ) => Task.FromResult( daemon.HasExited ), "Daemon shutdown", 10 );
                await Clean( "Daemon shutdown cleanup" );
            }
            var evidence = new JsonObject
            {
                ["hostSha256"] = Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( host ) ) ),
                ["packageSha256"] = Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( package ) ) ),
                ["commit"] = (await Exec( "git", "rev-parse", "HEAD" )).Trim( ),
                ["recordedUtc"] = DateTime.UtcNow.ToString( "O" ),
                ["runtimeProfile"] = "linux-glibc-invariant-v1",
                ["clipboard"] = "private sink",
                ["rendererVersion"] = (await Exec( "qs", "--version" )).Trim( ),
                ["rendererSha256"] = RendererDigest( ),
                ["repositoryWaylandJourney"] = wayland,
                ["installedAcceptance"] = "pending",
                ["environment"] = RuntimeInformation.OSDescription
            };
            var evidencePath = Path.Combine( Repo, "artifacts/extensions/session-evidence.json" );
            Directory.CreateDirectory( Path.GetDirectoryName( evidencePath )! );
            await File.WriteAllTextAsync( evidencePath, evidence.ToJsonString( new( )
            {
                WriteIndented = true
            } ) + "\n" );
            Console.WriteLine( "PASS real daemon/sandbox: discovery, negotiation, ownership, copy grants, replay, regeneration, presentation, revocation race, close/reopen, disconnect, master disable, update/rollback/sibling, remove, crash/hang/malformed/flood, daemon shutdown." );
        }
        finally
        {
            File.Delete( hold );
            foreach ( var client in clients )
            {
                client.Dispose( );
            }
            if ( !daemon.HasExited )
            {
                using var signal = Process.Start( "kill", $"-TERM {daemon.Id}" )!;
                await signal.WaitForExitAsync( );
            }
            using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
            try
            {
                await daemon.WaitForExitAsync( timeout.Token );
            }
            catch ( OperationCanceledException )
            {
                daemon.Kill( true );
                await daemon.WaitForExitAsync( );
            }
            Directory.CreateDirectory( Path.Combine( Repo, "artifacts/extensions" ) );
            await File.WriteAllTextAsync( Path.Combine( Repo, "artifacts/extensions/session-daemon.log" ), await stdout + await stderr );
            Directory.Delete( root, true );
        }
    }
    static string RendererDigest( )
    {
        using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
        var root = Path.Combine( Repo, "qml", "common" );
        foreach ( var file in Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) )
        {
            hash.AppendData( Encoding.UTF8.GetBytes( Path.GetRelativePath( root, file ) + "\0" ) );
            hash.AppendData( File.ReadAllBytes( file ) );
        }
        return Convert.ToHexStringLower( hash.GetHashAndReset( ) );
    }
    static string Variant( string source, string destination, string id, string version, string? executable = null, string? behavior = null )
    {
        using var input = ZipFile.OpenRead( source );
        using var output = ZipFile.Open( destination, ZipArchiveMode.Create );
        foreach ( var original in input.Entries )
        {
            var entry = output.CreateEntry( original.FullName );
            entry.ExternalAttributes = original.ExternalAttributes;
            using var target = entry.Open( );
            using var content = original.Open( );
            if ( entry.FullName == "commanddeck.extension.json" )
            {
                var manifest = JsonNode.Parse( content )!;
                manifest["id"] = id;
                manifest["version"] = version;
                if ( behavior is not null )
                {
                    manifest["commands"]![0]!["title"] = behavior;
                }
                target.Write( Encoding.UTF8.GetBytes( manifest.ToJsonString( ) ) );
            }
            else if ( executable is not null && entry.FullName == "bin/linux-x64/UuidTools" )
            {
                using var binary = File.OpenRead( executable );
                binary.CopyTo( target );
            }
            else
            {
                content.CopyTo( target );
            }
        }
        if ( behavior is not null )
        {
            var entry = output.CreateEntry( "behavior" );
            entry.ExternalAttributes = 0x81a4 << 16;
            using var target = new StreamWriter( entry.Open( ) );
            target.Write( behavior );
        }
        return destination;
    }
}
