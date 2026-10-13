using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunExtensionProbeAsync( CliArguments arguments, CancellationToken cancellationToken )
    {
        var artifactPath = arguments.GetOption( "--artifact" );
        if ( artifactPath is null )
        {
            throw new ArgumentException( "--artifact is required." );
        }

        var artifact = Path.GetFullPath( artifactPath );
        var entrypoint = arguments.GetOption( "--entrypoint" ) ?? "CommandDeck.ExtensionProbe";
        var root = Path.Combine( Path.GetTempPath( ), "commanddeck-extension-probe-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( root );
        try
        {
            return await RunExtensionProbeCoreAsync( artifact, entrypoint, root, arguments.HasFlag( "--hold" ), cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task<int> RunExtensionProbeCoreAsync( string artifact, string entrypoint, string root, bool hold, CancellationToken cancellationToken )
    {
        var sentinel = Path.Combine( root, "host-sentinel" );
        var socketPath = Path.Combine( root, "host.sock" );
        await File.WriteAllTextAsync( sentinel, "synthetic host secret", cancellationToken ).ConfigureAwait( false );
        using Socket socket = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        socket.Bind( new UnixDomainSocketEndPoint( socketPath ) );
        socket.Listen( );
        using TcpListener listener = new( IPAddress.Loopback, 0 );
        listener.Start( );
        var executable = Environment.ProcessPath!;
        string[] prefix = Path.GetFileNameWithoutExtension( executable ) == "dotnet" ? [Environment.GetCommandLineArgs( )[0]] : [];
        ExtensionSupervisor supervisor = new( executable, prefix );
        var watch = Stopwatch.StartNew( );
        var allocated = GC.GetTotalAllocatedBytes( );
        var process = ( ExtensionProcess ) await supervisor.StartAsync(
            new( artifact, entrypoint, "commanddeck.probe", ExtensionLaunchProfile.Interactive ), cancellationToken ).ConfigureAwait( false );
        JsonElement? report = null;
        long rss = 0;
        long tasks = 0;
        try
        {
            await using ExtensionPeer peer = new( process.Input, process.Output, true, ["storage.get", "views.replace"], ( frame, _ ) =>
            {
                if ( frame.Method == "storage.get" )
                {
                    return ValueTask.FromResult( JsonElement.Parse( "{\"value\":\"nested-rpc-ok\"}" ) );
                }
                report = frame.Params;
                return ValueTask.FromResult( JsonElement.Parse( "{\"viewId\":\"probe\",\"revision\":1}" ) );
            } );
            peer.Start( );
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            timeout.CancelAfter( TimeSpan.FromSeconds( 5 ) );
            var initialize = new InitializeRequest( new ProtocolVersion( ), "probe", 1, "probe",
                ExtensionPurposes.Interactive, [ExtensionFeatures.Commands, ExtensionFeatures.ListView], new SessionLimits( ) );
            var response = await peer.CallAsync( "initialize", JsonSerializer.SerializeToElement( initialize, ExtensionJsonContext.Default.InitializeRequest ), timeout.Token ).ConfigureAwait( false );
            var initialized = response.Deserialize( ExtensionJsonContext.Default.InitializeResult );
            if ( initialized?.Protocol.Major != 1 || initialized.Commands.Length != 1 || initialized.Commands[0] != "probe" )
            {
                throw new InvalidDataException( "The probe handshake is invalid." );
            }
            peer.Notify( "initialized" );
            using MemoryStream input = new( );
            using ( Utf8JsonWriter writer = new( input ) )
            {
                writer.WriteStartObject( );
                writer.WriteString( "hostFile", sentinel );
                writer.WriteString( "hostSocket", socketPath );
                writer.WriteString( "hostHome", Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ) );
                writer.WriteNumber( "port", (( IPEndPoint ) listener.LocalEndpoint).Port );
                writer.WriteEndObject( );
            }
            await peer.CallAsync( "command.open", JsonElement.Parse( input.ToArray( ) ), timeout.Token ).ConfigureAwait( false );
            watch.Stop( );
            if ( report is null )
            {
                throw new InvalidDataException( "The probe did not produce a view." );
            }
            foreach ( var name in new[] { "fileDenied", "homeDenied", "socketDenied", "networkDenied", "packageWriteDenied", "temporaryWorks", "environmentClean", "ptraceDenied" } )
            {
                if ( !report.Value.GetProperty( name ).GetBoolean( ) )
                {
                    throw new InvalidDataException( $"Sandbox assertion failed: {name}." );
                }
            }
            if ( report.Value.GetProperty( "storage" ).GetProperty( "value" ).GetString( ) != "nested-rpc-ok" )
            {
                throw new InvalidDataException( "The nested RPC response was lost." );
            }
            await Task.Delay( 200, timeout.Token ).ConfigureAwait( false );
            rss = long.Parse( await File.ReadAllTextAsync( "/sys/fs/cgroup" + process.ControlGroup + "/memory.current", timeout.Token ).ConfigureAwait( false ), System.Globalization.CultureInfo.InvariantCulture );
            tasks = long.Parse( await File.ReadAllTextAsync( "/sys/fs/cgroup" + process.ControlGroup + "/pids.current", timeout.Token ).ConfigureAwait( false ), System.Globalization.CultureInfo.InvariantCulture );
            if ( hold )
            {
                Console.WriteLine( "PROBE_READY " + process.ControlGroup );
                await Console.Out.FlushAsync( cancellationToken ).ConfigureAwait( false );
                await Task.Delay( Timeout.Infinite, cancellationToken ).ConfigureAwait( false );
            }
        }
        catch
        {
            Console.Error.WriteLine( process.ReadDiagnostics( ) );
            throw;
        }
        finally
        {
            await process.DisposeAsync( ).ConfigureAwait( false );
        }
        var stopped = !Directory.Exists( "/sys/fs/cgroup" + process.ControlGroup );
        if ( !stopped )
        {
            throw new InvalidDataException( "The probe left a populated resource group." );
        }
        using Utf8JsonWriter output = new( Console.OpenStandardOutput( ) );
        output.WriteStartObject( );
        output.WriteString( "runtimeProfile", ExtensionSandbox.RuntimeProfile );
        output.WriteBoolean( "hostAot", !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported );
        output.WriteNumber( "initialViewMilliseconds", watch.Elapsed.TotalMilliseconds );
        output.WriteNumber( "hostAllocatedBytes", GC.GetTotalAllocatedBytes( ) - allocated );
        output.WriteNumber( "cgroupMemoryBytes", rss );
        output.WriteNumber( "cgroupTasks", tasks );
        output.WriteBoolean( "descendantsStopped", stopped );
        output.WritePropertyName( "report" );
        report!.Value.WriteTo( output );
        output.WriteEndObject( );
        output.Flush( );
        Console.WriteLine( );
        return 0;
    }
}
