using System.Net.Sockets;
using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static bool IsIndexRootsCommand( CliArguments arguments )
    {
        var values = arguments.PositionalsAfterCommand( );
        return values.Count > 0 && values[0] is "roots" or "add" or "remove" or "disable" or "enable" or "import";
    }

    private static async Task<int> RunIndexRootsCliAsync( CliArguments arguments, CancellationToken cancellationToken )
    {
        var values = arguments.PositionalsAfterCommand( );
        var operation = values[0] switch
        {
            "roots" => ProtocolOperations.IndexRoots,
            "add" => ProtocolOperations.IndexAdd,
            "remove" => ProtocolOperations.IndexRemove,
            "disable" => ProtocolOperations.IndexDisable,
            "enable" => ProtocolOperations.IndexEnable,
            "import" => ProtocolOperations.IndexRootsImport,
            _ => throw new InvalidOperationException( "Unknown index roots command." )
        };
        if ( values.Count != (operation == ProtocolOperations.IndexRoots ? 1 : 2) )
        {
            Console.Error.WriteLine( "Use index roots, add <path>, remove/disable/enable <root-id>, or import <export-file>." );
            return 2;
        }

        IndexRootPreferences? import = null;
        if ( operation == ProtocolOperations.IndexRootsImport )
        {
            import = await ReadIndexRootsImportAsync( values[1], cancellationToken ).ConfigureAwait( false );
        }

        var request = new ProtocolRequest
        {
            RequestId = 1,
            Operation = operation,
            Path = operation == ProtocolOperations.IndexAdd ? IndexRootPaths.Normalize( values[1] ) : null,
            RootId = operation is ProtocolOperations.IndexRemove or ProtocolOperations.IndexDisable or ProtocolOperations.IndexEnable ? values[1] : null,
            IndexRoots = import
        };
        var client = await ConnectPreferenceDaemonAsync( arguments, cancellationToken ).ConfigureAwait( false );
        IndexRootsResult? result = null;
        if ( client is not null )
        {
            await using ( client )
            {
                await client.SendAsync( request, cancellationToken ).ConfigureAwait( false );
                await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ).ConfigureAwait( false ) )
                {
                    var message = JsonSerializer.Deserialize( line, CommandDeckJsonContext.Default.ProtocolMessage );
                    if ( message?.Type == ProtocolMessageTypes.Error )
                    {
                        throw new InvalidOperationException( message.Error );
                    }
                    result ??= message?.IndexRoots;
                    if ( message?.Type == ProtocolMessageTypes.Done )
                    {
                        break;
                    }
                }
            }
        }
        else
        {
            await using var runtime = await CommandDeckRuntime.CreateAsync( arguments.GetOption( "--config" ),
                new RuntimeConstructionOptions( ), includeClipboardHistory: false, cancellationToken ).ConfigureAwait( false );
            using var shutdown = cancellationToken.Register( runtime.CancelBackgroundIndexing );
            result = await runtime.Indexer.ManageRootsAsync( operation, request.Path, request.RootId, import, cancellationToken ).ConfigureAwait( false );
            await (( IndexCoordinator ) runtime.Indexer).WaitForBackgroundReindexAsync( cancellationToken ).ConfigureAwait( false );
        }
        if ( result is null )
        {
            throw new InvalidOperationException( "The daemon did not return indexed folders." );
        }

        if ( arguments.HasFlag( "--json" ) )
        {
            Console.WriteLine( JsonSerializer.Serialize( result, CommandDeckJsonContext.Default.IndexRootsResult ) );
            return 0;
        }
        foreach ( var root in result.Roots )
        {
            Console.WriteLine( $"{root.Id}  source={root.Source} enabled={root.Enabled}  " +
                PreferencesTextFormatter.EscapeTerminalText( root.Path ) );
            Console.WriteLine( "  " + PreferencesTextFormatter.EscapeTerminalText( root.Issue ?? root.Status ) );
        }
        return 0;
    }

    private static async ValueTask<IndexRootPreferences> ReadIndexRootsImportAsync( string path, CancellationToken cancellationToken )
    {
        await using FileStream stream = new( PathExpansion.Expand( path ), FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous );
        var buffer = new byte[ProtocolLimits.MaximumCustomizationExportBytes + 1];
        var count = 0;
        while ( count < buffer.Length )
        {
            var read = await stream.ReadAsync( buffer.AsMemory( count ), cancellationToken ).ConfigureAwait( false );
            if ( read == 0 )
            {
                break;
            }
            count += read;
        }
        if ( count > ProtocolLimits.MaximumCustomizationExportBytes )
        {
            throw new InvalidOperationException( "Customization import exceeds 256 KiB." );
        }

        using var document = JsonDocument.Parse( buffer.AsMemory( 0, count ) );
        if ( !document.RootElement.TryGetProperty( "indexRoots", out var roots ) )
        {
            throw new InvalidOperationException( "Customization export must contain indexRoots." );
        }
        var preferences = JsonSerializer.Deserialize( roots, CommandDeckJsonContext.Default.IndexRootPreferences );
        if ( preferences is null )
        {
            throw new InvalidOperationException( "Indexed folder preferences are required." );
        }

        return preferences;
    }

    private static async ValueTask<UnixProtocolClient?> ConnectPreferenceDaemonAsync(
        CliArguments arguments, CancellationToken cancellationToken )
    {
        UnixProtocolClient? client = null;
        if ( arguments.GetOption( "--config" ) is null || arguments.GetOption( "--socket" ) is not null )
        {
            try
            {
                client = await UnixProtocolClient.ConnectAsync( arguments.GetOption( "--socket" ), cancellationToken ).ConfigureAwait( false );
            }
            catch ( SocketException exception ) when ( arguments.GetOption( "--socket" ) is null &&
                exception.SocketErrorCode is SocketError.ConnectionRefused or SocketError.AddressNotAvailable )
            {
                // No daemon: the local runtime takes the same index journal lease.
            }
        }

        return client;
    }

}
