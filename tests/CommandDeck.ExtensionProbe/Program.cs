using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.ExtensionProbe;

internal static partial class Program
{
    private static async Task Main( string[] args )
    {
        if ( args.Length > 0 )
        {
            if ( args[0] == "--descendant" )
            {
                using var grandchild = Process.Start( new ProcessStartInfo( Environment.ProcessPath! )
                {
                    ArgumentList = { "--grandchild" },
                    UseShellExecute = false
                } );
            }
            await Task.Delay( Timeout.Infinite );
            return;
        }
        ExtensionPeer? peer = null;
        var initialized = false;
        var ready = false;
        TaskCompletionSource finished = new( TaskCreationOptions.RunContinuationsAsynchronously );
        peer = new ExtensionPeer( Console.OpenStandardInput( ), Console.OpenStandardOutput( ), false,
            ["initialize", "initialized", "command.open", "shutdown"], async ( frame, ct ) =>
            {
                switch ( frame.Method )
                {
                    case "initialize" when !initialized:
                        var request = frame.Params!.Value.Deserialize( ExtensionJsonContext.Default.InitializeRequest )!;
                        if ( request.Protocol.Major != 1 || request.CommandId != "probe" )
                        {
                            throw new InvalidDataException( "Unsupported probe initialization." );
                        }
                        initialized = true;
                        return JsonSerializer.SerializeToElement( new InitializeResult( new ProtocolVersion( ), "0.1.0-preview.1", ["probe"] ), ExtensionJsonContext.Default.InitializeResult );
                    case "initialized" when initialized:
                        ready = true;
                        return JsonElement.Parse( "{}" );
                    case "command.open" when ready:
                        var storage = await peer!.CallAsync( "storage.get", JsonElement.Parse( "{\"key\":\"probe\"}" ), ct );
                        var report = Probe( frame.Params!.Value, storage );
                        var view = await peer.CallAsync( "views.replace", report, ct );
                        return view;
                    case "shutdown":
                        finished.TrySetResult( );
                        return JsonElement.Parse( "{}" );
                    default:
                        throw new InvalidDataException( "Invalid probe state." );
                }
            } );
        peer.Start( );
        await using ( peer )
        {
            await Task.WhenAny( finished.Task, peer.Completion );
        }
    }

    private static JsonElement Probe( JsonElement parameters, JsonElement storage )
    {
        var hostFile = parameters.GetProperty( "hostFile" ).GetString( )!;
        var hostSocket = parameters.GetProperty( "hostSocket" ).GetString( )!;
        var port = parameters.GetProperty( "port" ).GetInt32( );
        var fileDenied = Open( hostFile, 0 ) < 0;
        var homeDenied = !Directory.Exists( parameters.GetProperty( "hostHome" ).GetString( )! );
        var socketDenied = AttemptDenied( ( ) =>
        {
            using Socket socket = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
            socket.Connect( new UnixDomainSocketEndPoint( hostSocket ) );
        } );
        var networkDenied = AttemptDenied( ( ) =>
        {
            using Socket socket = new( AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp );
            socket.Connect( new IPEndPoint( IPAddress.Loopback, port ) );
        } );
        var packageWriteDenied = AttemptDenied( ( ) => File.WriteAllText( "/extension/escape.txt", "not allowed" ) );
        File.WriteAllText( "/tmp/private.txt", "ephemeral" );
        var temporaryWorks = File.ReadAllText( "/tmp/private.txt" ) == "ephemeral";
        var environmentClean = Environment.GetEnvironmentVariable( "COMMANDDECK_PROBE_SECRET" ) is null;
        var ptraceDenied = Ptrace( 0, 0, 0, 0 ) == -1;
        using var descendant = Process.Start( new ProcessStartInfo( Environment.ProcessPath! )
        {
            ArgumentList = { "--descendant" },
            UseShellExecute = false
        } );
        using MemoryStream bytes = new( );
        using ( Utf8JsonWriter writer = new( bytes ) )
        {
            writer.WriteStartObject( );
            writer.WriteString( "kind", "list" );
            writer.WriteString( "title", "Extension sandbox probe" );
            writer.WriteBoolean( "fileDenied", fileDenied );
            writer.WriteBoolean( "homeDenied", homeDenied );
            writer.WriteBoolean( "socketDenied", socketDenied );
            writer.WriteBoolean( "networkDenied", networkDenied );
            writer.WriteBoolean( "packageWriteDenied", packageWriteDenied );
            writer.WriteBoolean( "temporaryWorks", temporaryWorks );
            writer.WriteBoolean( "environmentClean", environmentClean );
            writer.WriteBoolean( "ptraceDenied", ptraceDenied );
            writer.WritePropertyName( "storage" );
            storage.WriteTo( writer );
            writer.WriteEndObject( );
        }
        return JsonElement.Parse( bytes.ToArray( ) );
    }

    private static bool AttemptDenied( Action action )
    {
        try
        {
            action( );
            return false;
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or SocketException )
        {
            return true;
        }
    }

    [LibraryImport( "libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8 )]
    private static partial int Open( string path, int flags );

    [LibraryImport( "libc", EntryPoint = "ptrace" )]
    private static partial long Ptrace( int request, int pid, nint address, nint data );
}
