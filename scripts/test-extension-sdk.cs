#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.Json.Nodes;

return await SdkJourney.RunAsync( args );

static class SdkJourney
{
    public static async Task<int> RunAsync( string[] args )
    {
        if ( args.Length != 0 )
        {
            throw new ArgumentException( "Usage: dotnet scripts/test-extension-sdk.cs" );
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
        var artifacts = Path.Combine( repository, "artifacts/extensions/sdk" );
        Directory.CreateDirectory( artifacts );
        var temporary = Directory.CreateTempSubdirectory( "commanddeck-sdk-" ).FullName;
        try
        {
            await File.WriteAllTextAsync( Path.Combine( temporary, "global.json" ), new JsonObject
            {
                ["sdk"] = new JsonObject { ["version"] = "10.0.400", ["rollForward"] = "disable" }
            }.ToJsonString( ) );
            File.Copy( Path.Combine( repository, "Directory.Build.props" ), Path.Combine( temporary, "Directory.Build.props" ) );
            foreach ( var name in new[] { "CommandDeck.ExtensionProtocol", "CommandDeck.Extensions" } )
            {
                CopyDirectory(
                    Path.Combine( repository, "src", name ),
                    Path.Combine( temporary, "src", name ),
                    new HashSet<string>( ["bin", "obj"], StringComparer.Ordinal ) );
                await RunCheckedAsync(
                    "dotnet",
                    ["pack", Path.Combine( temporary, "src", name ), "-c", "Release", "-o", Path.Combine( artifacts, "feed" )],
                    temporary );
            }

            var sample = Path.Combine( temporary, "author" );
            CopyDirectory( Path.Combine( repository, "samples/extensions/UuidTools" ), sample, new HashSet<string>( StringComparer.Ordinal ) );
            await AddWireDefaultsProbeAsync( sample );
            var feed = SecurityElement.Escape( Path.Combine( artifacts, "feed" ) );
            await File.WriteAllTextAsync( Path.Combine( sample, "NuGet.Config" ),
                "<configuration><packageSources><clear/>" +
                $"<add key=\"preview\" value=\"{feed}\"/>" +
                "<add key=\"nuget\" value=\"https://api.nuget.org/v3/index.json\"/>" +
                "</packageSources><packageSourceMapping><packageSource key=\"preview\">" +
                "<package pattern=\"CommandDeck.*\"/></packageSource><packageSource key=\"nuget\">" +
                "<package pattern=\"*\"/></packageSource></packageSourceMapping></configuration>" );

            foreach ( var (profile, publishAot) in new[] { ("managed", "false"), ("aot", "true") } )
            {
                var output = Path.Combine( artifacts, profile );
                await RunCheckedAsync( "dotnet",
                    [
                        "publish", sample, "-c", "Release", "-r", "linux-x64", "--self-contained", "true",
                        $"-p:PublishAot={publishAot}", $"-p:RestorePackagesPath={Path.Combine( temporary, "packages" )}", "-o", output
                    ],
                    temporary );
                await ExchangeAsync( Path.Combine( output, "UuidTools" ) );
                Console.WriteLine( $"PASS .NET 10 public SDK {profile} handwritten item defaults, author handshake, typed view and callback" );
            }

            await File.WriteAllTextAsync( Path.Combine( artifacts, "evidence.json" ), new JsonObject
            {
                ["sdk"] = "10.0.400",
                ["profiles"] = new JsonArray( "managed", "aot" ),
                ["authorProject"] = "standalone public NuGet references",
                ["sandbox"] = false
            }.ToJsonString( new( )
            {
                WriteIndented = true
            } ) + "\n" );
        }
        finally
        {
            Directory.Delete( temporary, recursive: true );
        }

        return 0;
    }

    static async Task AddWireDefaultsProbeAsync( string sample )
    {
        var programPath = Path.Combine( sample, "Program.cs" );
        var program = await File.ReadAllTextAsync( programPath );
        const string entry = "await ExtensionApp.RunAsync( args, new UuidsCommand( ) );";
        if ( !program.Contains( entry, StringComparison.Ordinal ) )
        {
            throw new InvalidOperationException( "The SDK author fixture entry point changed." );
        }

        await File.WriteAllTextAsync( programPath, program.Replace( entry, "HandwrittenWireProbe.Check( );\n" + entry, StringComparison.Ordinal ) );
        await File.WriteAllTextAsync( Path.Combine( sample, "HandwrittenWireProbe.cs" ), """"
            using System.Text.Json;
            using CommandDeck.ExtensionProtocol;
            using CommandDeck.Extensions.Views;

            static class HandwrittenWireProbe
            {
                public static void Check( )
                {
                    var view = JsonSerializer.Deserialize( """{"kind":"list","id":"list","title":"List","items":[{"id":"a","title":"A"}]}""", ExtensionViewJsonContext.Default.ExtensionView );
                    if ( view is null )
                    {
                        throw new InvalidOperationException( "Missing handwritten list view." );
                    }

                    ViewValidation.Validate( view );
                    if ( view is not ListView list || list.Items.Length != 1 || list.Items[0].Actions.Length != 0 )
                    {
                        throw new InvalidOperationException( "Handwritten list item defaults changed." );
                    }
                }
            }
            """" );
    }

    static async Task ExchangeAsync( string executable )
    {
        ProcessStartInfo start = new( executable )
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start( start );
        if ( process is null )
        {
            throw new InvalidOperationException( "The SDK fixture did not start." );
        }

        var stderr = process.StandardError.ReadToEndAsync( );
        async Task SendAsync( JsonObject value )
        {
            await process.StandardInput.WriteLineAsync( value.ToJsonString( ) );
            await process.StandardInput.FlushAsync( );
        }

        async Task<JsonObject> ReceiveAsync( )
        {
            using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
            var line = await process.StandardOutput.ReadLineAsync( deadline.Token );
            if ( line is null || Encoding.UTF8.GetByteCount( line ) + 1 > 262144 )
            {
                throw new InvalidDataException( "The SDK returned an invalid frame." );
            }

            var message = JsonNode.Parse( line ) as JsonObject;
            if ( message is null )
            {
                throw new InvalidDataException( "The SDK returned invalid JSON." );
            }

            return message;
        }

        try
        {
            await SendAsync( new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = "h:1",
                ["method"] = "initialize",
                ["params"] = new JsonObject
                {
                    ["protocol"] = new JsonObject { ["major"] = 1, ["minor"] = 0 },
                    ["sessionId"] = "sdk-fixture",
                    ["executionEpoch"] = 1,
                    ["commandId"] = "uuids",
                    ["purpose"] = "interactive",
                    ["hostFeatures"] = new JsonArray( "commands@1", "views.list@1", "views.pagination@1", "actions.copy-text@1" ),
                    ["limits"] = new JsonObject { ["maximumFrameBytes"] = 262144 }
                }
            } );
            var initialized = await ReceiveAsync( );
            Check(
                initialized["id"]!.GetValue<string>( ) == "h:1" &&
                initialized["result"]!["commands"]!.AsArray( ).Single( )!.GetValue<string>( ) == "uuids",
                "The SDK handshake was invalid." );
            await SendAsync( new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "initialized" } );

            string[]? previous = null;
            for ( var generation = 1; generation <= 2; generation++ )
            {
                JsonObject parameters = new( )
                {
                    ["generation"] = generation,
                    ["invocationId"] = "call-" + generation
                };
                if ( generation == 1 )
                {
                    parameters["inputs"] = new JsonObject( );
                }
                else
                {
                    parameters["viewId"] = "uuids";
                    parameters["viewRevision"] = 1;
                    parameters["event"] = new JsonObject { ["kind"] = "action", ["actionId"] = "regenerate" };
                }

                var method = generation == 1 ? "command.open" : "command.event";
                await SendAsync( new JsonObject { ["jsonrpc"] = "2.0", ["id"] = "h:" + (generation + 1), ["method"] = method, ["params"] = parameters } );
                var view = await ReceiveAsync( );
                Check( view["method"]?.GetValue<string>( ) == "views.replace", "The SDK did not render a view: " + view.ToJsonString( ) );
                var update = view["params"]!;
                Check(
                    update["generation"]!.GetValue<int>( ) == generation &&
                    update["expectedRevision"]!.GetValue<int>( ) == generation - 1,
                    "The SDK view revision was invalid." );
                var rows = update["content"]!["items"]!.AsArray( );
                var values = rows.Select( row => Guid.Parse( row!["id"]!.GetValue<string>( ) ).ToString( ) ).ToArray( );
                Check(
                    values.Distinct( StringComparer.Ordinal ).Count( ) == 8 &&
                    (previous is null || !values.SequenceEqual( previous, StringComparer.Ordinal )),
                    "The SDK returned repeated UUIDs." );
                foreach ( var row in rows )
                {
                    Check(
                        row!["actions"]![0]!["kind"]!.GetValue<string>( ) == "copy-text" &&
                        row["actions"]![0]!["text"]!.GetValue<string>( ) == row["id"]!.GetValue<string>( ),
                        "The SDK copy action was invalid." );
                }

                previous = values;
                await SendAsync( new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = view["id"]!.DeepClone( ),
                    ["result"] = new JsonObject { ["viewId"] = "uuids", ["revision"] = generation }
                } );
                var result = await ReceiveAsync( );
                Check( result["id"]!.GetValue<string>( ) == "h:" + (generation + 1) && result.ContainsKey( "result" ), "The SDK call did not complete." );
            }

            process.StandardInput.Close( );
            await process.WaitForExitAsync( ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Check( process.ExitCode == 0, await stderr );
        }
        finally
        {
            if ( !process.HasExited )
            {
                process.Kill( entireProcessTree: true );
                await process.WaitForExitAsync( );
            }
        }
    }

    static async Task RunCheckedAsync( string executable, IEnumerable<string> arguments, string workingDirectory )
    {
        ProcessStartInfo start = new( executable )
        {
            WorkingDirectory = workingDirectory,
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

        try
        {
            await process.WaitForExitAsync( ).WaitAsync( TimeSpan.FromMinutes( 5 ) );
        }
        catch ( TimeoutException )
        {
            process.Kill( entireProcessTree: true );
            await process.WaitForExitAsync( );
            throw;
        }

        Check( process.ExitCode == 0, $"{executable} exited with code {process.ExitCode}." );
    }

    static void CopyDirectory( string source, string destination, IReadOnlySet<string> excludedDirectories )
    {
        Directory.CreateDirectory( destination );
        foreach ( var file in Directory.EnumerateFiles( source ) )
        {
            File.Copy( file, Path.Combine( destination, Path.GetFileName( file ) ) );
        }

        foreach ( var directory in Directory.EnumerateDirectories( source ) )
        {
            if ( excludedDirectories.Contains( Path.GetFileName( directory ) ) )
            {
                continue;
            }

            CopyDirectory( directory, Path.Combine( destination, Path.GetFileName( directory ) ), excludedDirectories );
        }
    }

    static void Check( bool condition, string message )
    {
        if ( !condition )
        {
            throw new InvalidOperationException( message );
        }
    }
}
