using System.Diagnostics;
using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task DaemonAndBridgeRestartSoakAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-recovery-soak-{Guid.NewGuid( ):N}" );
        var documents = Path.Combine( root, "documents" );
        var database = Path.Combine( root, "catalog.db" );
        var config = Path.Combine( root, "config.json" );
        var socket = SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" );
        Directory.CreateDirectory( documents );
        await File.WriteAllTextAsync(
            Path.Combine( documents, "recovery-note.txt" ),
            "restart recovery marker" );
        var options = CreateIncrementalIndexOptions( database, documents );
        await File.WriteAllTextAsync(
            config,
            JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );

        List<Process> processes = [];
        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 20 ) );
        try
        {
            string? stableId = null;
            for ( var cycle = 0; cycle < 4; cycle++ )
            {
                var daemon = StartCommandDeckProcess(
                    processes,
                    "daemon", "--config", config, "--socket", socket );
                var client = await ConnectWithRetryAsync( socket );
                var requestBase = cycle * 100L;
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = requestBase + 1,
                    Operation = ProtocolOperations.Ping
                }, CancellationToken.None );
                var pong = await ReadMessageAsync(
                    client,
                    requestBase + 1,
                    ProtocolMessageTypes.Pong,
                    timeout.Token );
                ArgumentNullException.ThrowIfNull( pong );

                var result = await SearchForCustomizableResultAsync(
                    client,
                    requestBase + 2,
                    timeout.Token );
                stableId ??= result.Id;
                Equal( stableId, result.Id );

                var favorite = cycle % 2 == 0;
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = requestBase + 3,
                    Operation = ProtocolOperations.FavoriteSet,
                    ResultId = stableId,
                    Enabled = favorite
                }, CancellationToken.None );
                var customization = await ReadMessageAsync(
                    client,
                    requestBase + 3,
                    ProtocolMessageTypes.CustomizationResult,
                    timeout.Token );
                Equal( favorite, customization?.Customization?.IsFavorite );

                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = requestBase + 4,
                    Operation = ProtocolOperations.Search,
                    Query = new string( 'r', 2_000 )
                }, CancellationToken.None );
                StopProcess( daemon );
                try
                {
                    await client.DisposeAsync( );
                }
                catch ( IOException )
                {
                    // Expected when the daemon is killed with a request in flight.
                }
            }

            var finalDaemon = StartCommandDeckProcess(
                processes,
                "daemon", "--config", config, "--socket", socket );
            await using ( var client = await ConnectWithRetryAsync( socket ) )
            {
                for ( var cycle = 0; cycle < 3; cycle++ )
                {
                    var bridge = StartCommandDeckProcess(
                        processes,
                        "bridge", "--socket", socket );
                    await bridge.StandardInput.WriteLineAsync( ProtocolJson.SerializeRequest(
                        new ProtocolRequest
                        {
                            RequestId = cycle + 1,
                            Operation = ProtocolOperations.Ping
                        } ) );
                    var first = await bridge.StandardOutput.ReadLineAsync( )
                        .WaitAsync( TimeSpan.FromSeconds( 2 ) );
                    var second = await bridge.StandardOutput.ReadLineAsync( )
                        .WaitAsync( TimeSpan.FromSeconds( 2 ) );
                    var firstMessage = DeserializeMessage( first ?? string.Empty );
                    var secondMessage = DeserializeMessage( second ?? string.Empty );
                    True( firstMessage?.Type == ProtocolMessageTypes.Pong ||
                        secondMessage?.Type == ProtocolMessageTypes.Pong );
                    True( firstMessage?.Type == ProtocolMessageTypes.Done ||
                        secondMessage?.Type == ProtocolMessageTypes.Done );
                    StopProcess( bridge );
                }

                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 99,
                    Operation = ProtocolOperations.PreferencesGet
                }, CancellationToken.None );
                var snapshot = await ReadMessageAsync(
                    client,
                    99,
                    ProtocolMessageTypes.PreferencesResult,
                    timeout.Token );
                ArgumentNullException.ThrowIfNull( snapshot?.Preferences );
                True( snapshot.Preferences.Customizations.All( value =>
                    !string.Equals( value.StableId, stableId, StringComparison.Ordinal ) ||
                    !value.IsFavorite ) );
            }

            StopProcess( finalDaemon );
            True( processes.All( static process => process.HasExited ) );
        }
        finally
        {
            foreach ( var process in processes )
            {
                StopProcess( process );
                process.Dispose( );
            }

            if ( Directory.Exists( root ) )
            {
                Directory.Delete( root, recursive: true );
            }
        }
    }

    private static async Task<SearchResult> SearchForCustomizableResultAsync(
        UnixProtocolClient client,
        long firstRequestId,
        CancellationToken cancellationToken )
    {
        for ( var attempt = 0; attempt < 20; attempt++ )
        {
            var requestId = firstRequestId + attempt;
            await client.SendAsync( new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.Search,
                Query = "restart recovery"
            }, CancellationToken.None );

            await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ) )
            {
                var message = DeserializeMessage( line );
                if ( message?.RequestId == requestId &&
                    message.Type == ProtocolMessageTypes.Result &&
                    message.Result?.CanCustomize == true )
                {
                    return message.Result;
                }

                if ( message?.RequestId == requestId &&
                    message.Type == ProtocolMessageTypes.Error )
                {
                    throw new InvalidOperationException(
                        $"Recovery search failed: {message.Error ?? "No error detail."}" );
                }

                if ( message?.RequestId == requestId &&
                    message.Type == ProtocolMessageTypes.Done )
                {
                    break;
                }
            }

            await Task.Delay( 25, cancellationToken );
        }

        throw new InvalidOperationException(
            "Recovery search did not produce a customizable result after indexing settled." );
    }

    private static Process StartCommandDeckProcess(
        List<Process> processes,
        params string[] arguments )
    {
        ProcessStartInfo start = new( )
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add( typeof( CommandDeck.Program ).Assembly.Location );
        foreach ( var argument in arguments )
        {
            start.ArgumentList.Add( argument );
        }

        Process process = new( )
        {
            StartInfo = start
        };
        True( process.Start( ) );
        processes.Add( process );
        return process;
    }

    private static void StopProcess( Process process )
    {
        if ( process.HasExited )
        {
            return;
        }

        process.Kill( entireProcessTree: true );
        process.WaitForExit( 2_000 );
    }
}
