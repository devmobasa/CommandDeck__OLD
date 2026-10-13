#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

return await PackageAotJourney.RunAsync( args );

static class PackageAotJourney
{
    public static async Task<int> RunAsync( string[] args )
    {
        if ( args.Length != 1 )
        {
            throw new ArgumentException( "Usage: dotnet scripts/test-extension-package-aot.cs /path/to/published/commanddeck" );
        }

        var entryPoint = AppContext.GetData( "EntryPointFilePath" ) as string;
        if ( entryPoint is null )
        {
            throw new InvalidOperationException( "The script path is unavailable." );
        }

        var scriptDirectory = Path.GetDirectoryName( entryPoint );
        if ( scriptDirectory is null )
        {
            throw new InvalidOperationException( "The script directory is unavailable." );
        }

        var repository = Path.GetFullPath( Path.Combine( scriptDirectory, ".." ) );
        var host = Path.GetFullPath( args[0] );
        var version = await RunAsync( host, ["version"], repository, TimeSpan.FromSeconds( 5 ) );
        Check( version.ExitCode == 0, version.StandardError );
        Check( version.StandardOutput.Contains( "runtime: Native AOT", StringComparison.Ordinal ), "Supply a published Native AOT host" );

        var root = Directory.CreateTempSubdirectory( "commanddeck-aot-extension-" ).FullName;
        Process? daemon = null;
        Task<string>? daemonError = null;
        try
        {
            var socketPath = Path.Combine( root, "daemon.sock" );
            var config = Path.Combine( root, "config.json" );
            var package = Path.Combine( root, "uuid.cdext" );
            await File.WriteAllTextAsync( config, new JsonObject
            {
                ["storage"] = new JsonObject { ["databasePath"] = Path.Combine( root, "catalog.db" ) },
                ["indexing"] = new JsonObject { ["roots"] = new JsonArray( ), ["indexDesktopApplications"] = false, ["indexOmarchyCommands"] = false },
                ["ai"] = new JsonObject { ["enabled"] = false },
                ["agent"] = new JsonObject { ["runtimeDirectory"] = Path.Combine( root, "runtime" ), ["stateDirectory"] = Path.Combine( root, "state" ) }
            }.ToJsonString( ) );

            using ( var archive = ZipFile.Open( package, ZipArchiveMode.Create ) )
            {
                archive.CreateEntryFromFile(
                    Path.Combine( repository, "samples/extensions/UuidTools/commanddeck.extension.json" ),
                    "commanddeck.extension.json",
                    CompressionLevel.Optimal );
                archive.CreateEntryFromFile(
                    Path.Combine( repository, "artifacts/extensions/sdk/aot/UuidTools" ),
                    "bin/linux-x64/UuidTools",
                    CompressionLevel.Optimal );
            }

            ProcessStartInfo start = new( host )
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach ( var argument in new[] { "daemon", "--config", config, "--socket", socketPath } )
            {
                start.ArgumentList.Add( argument );
            }

            daemon = Process.Start( start );
            if ( daemon is null )
            {
                throw new InvalidOperationException( "The AOT daemon did not start." );
            }

            _ = daemon.StandardOutput.ReadToEndAsync( );
            daemonError = daemon.StandardError.ReadToEndAsync( );
            using ( CancellationTokenSource startup = new( TimeSpan.FromSeconds( 10 ) ) )
            {
                while ( !File.Exists( socketPath ) )
                {
                    if ( daemon.HasExited )
                    {
                        throw new InvalidOperationException( await daemonError );
                    }

                    await Task.Delay( 20, startup.Token );
                }
            }

            async Task<JsonObject> CliAsync( params string[] arguments )
            {
                var result = await RunAsync( host, ["extensions", .. arguments, "--socket", socketPath, "--json"], repository, TimeSpan.FromSeconds( 15 ) );
                Check( result.ExitCode == 0, result.StandardError );
                var parsed = JsonNode.Parse( result.StandardOutput ) as JsonObject;
                if ( parsed is null )
                {
                    throw new InvalidDataException( "The extension CLI returned invalid JSON." );
                }

                return parsed;
            }

            var review = await CliAsync( "inspect", "--archive", package );
            var installed = await CliAsync( "install", "--archive", package, "--digest", review["archiveDigest"]!.GetValue<string>( ), "--yes" );
            Check( installed["total"]!.GetValue<int>( ) == 1, "The package was not installed." );
            Check(
                !installed["enabled"]!.GetValue<bool>( ) && !installed["packages"]![0]!["enabled"]!.GetValue<bool>( ),
                "The package was enabled before review." );
            await CliAsync( "enable", "org.commanddeck.uuid-tools" );
            await CliAsync( "enable" );

            string? source = null;
            using ( Socket socket = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified ) )
            using ( CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) ) )
            {
                await socket.ConnectAsync( new UnixDomainSocketEndPoint( socketPath ), deadline.Token );
                using NetworkStream stream = new( socket, ownsSocket: false );
                using StreamWriter writer = new( stream, new UTF8Encoding( false ), leaveOpen: true )
                {
                    AutoFlush = true
                };
                using StreamReader reader = new( stream, Encoding.UTF8, leaveOpen: true );
                var bootstrap = new JsonObject { ["version"] = 3, ["requestId"] = 1, ["operation"] = "bootstrap" };
                await writer.WriteLineAsync( bootstrap.ToJsonString( ).AsMemory( ), deadline.Token );
                while ( true )
                {
                    var line = await reader.ReadLineAsync( deadline.Token );
                    if ( line is null )
                    {
                        throw new EndOfStreamException( "The daemon closed during bootstrap." );
                    }

                    var message = JsonNode.Parse( line )!;
                    Check( message["type"]!.GetValue<string>( ) != "error", line );
                    if ( message["type"]!.GetValue<string>( ) == "bootstrap-result" )
                    {
                        source = message["bootstrap"]!["sources"]!.AsArray( )
                            .Select( value => value!["id"]!.GetValue<string>( ) )
                            .Single( value => value.StartsWith( "extension:", StringComparison.Ordinal ) );
                    }

                    if ( message["type"]!.GetValue<string>( ) == "done" )
                    {
                        break;
                    }
                }
            }

            Check( source is not null, "The extension source was absent from bootstrap." );
            var request = new JsonObject
            {
                ["version"] = 3,
                ["requestId"] = 2,
                ["operation"] = "search",
                ["expression"] = new JsonObject
                {
                    ["includeFilterIds"] = new JsonArray( source ),
                    ["excludeFilterIds"] = new JsonArray( ),
                    ["clauses"] = new JsonArray( new JsonObject { ["text"] = "UUIDs" } )
                }
            };
            var bridge = await RunAsync( host, ["bridge", "--socket", socketPath], repository, TimeSpan.FromSeconds( 10 ), request.ToJsonString( ) + "\n" );
            Check( bridge.ExitCode == 0, bridge.StandardError );
            var responses = bridge.StandardOutput.Split( '\n', StringSplitOptions.RemoveEmptyEntries ).Select( line => JsonNode.Parse( line ) ).ToArray( );
            Check( responses.All( message => message!["type"]!.GetValue<string>( ) != "error" ), bridge.StandardOutput );
            Check( responses.Any( message => message!["result"]?["kind"]?.GetValue<string>( ) == "extension-command" ), bridge.StandardOutput );
            await CliAsync( "remove", "org.commanddeck.uuid-tools", "--yes" );
            Console.WriteLine( "PASS Native AOT daemon: sample archive inspection/install, registry bootstrap, typed bridge search and removal" );
        }
        finally
        {
            if ( daemon is not null )
            {
                if ( !daemon.HasExited )
                {
                    _ = await RunAsync( "/usr/bin/kill", ["-TERM", daemon.Id.ToString( )], repository, TimeSpan.FromSeconds( 5 ) );
                    try
                    {
                        await daemon.WaitForExitAsync( ).WaitAsync( TimeSpan.FromSeconds( 10 ) );
                    }
                    catch ( TimeoutException )
                    {
                        daemon.Kill( entireProcessTree: true );
                        await daemon.WaitForExitAsync( );
                    }
                }

                daemon.Dispose( );
            }

            Directory.Delete( root, recursive: true );
        }

        return 0;
    }

    static void Check( bool condition, string message )
    {
        if ( !condition )
        {
            throw new InvalidOperationException( message );
        }
    }

    static async Task<CommandResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        string? standardInput = null )
    {
        ProcessStartInfo start = new( executable )
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false
        };
        foreach ( var argument in arguments )
        {
            start.ArgumentList.Add( argument );
        }

        using var process = Process.Start( start );
        if ( process is null )
        {
            throw new InvalidOperationException( $"Could not start {executable}." );
        }

        var stdout = process.StandardOutput.ReadToEndAsync( );
        var stderr = process.StandardError.ReadToEndAsync( );
        if ( standardInput is not null )
        {
            await process.StandardInput.WriteAsync( standardInput );
            process.StandardInput.Close( );
        }

        try
        {
            await process.WaitForExitAsync( ).WaitAsync( timeout );
        }
        catch ( TimeoutException )
        {
            process.Kill( entireProcessTree: true );
            await process.WaitForExitAsync( );
            throw;
        }

        return new( process.ExitCode, await stdout, await stderr );
    }
}

sealed record CommandResult( int ExitCode, string StandardOutput, string StandardError );
