using System.Buffers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;

namespace CommandDeck;

public sealed class TransientUnitCodexLauncher( string systemdRunPath, string systemctlPath = "/usr/bin/systemctl" )
    : IAgentProcessLauncher
{
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;

    public static string UnitName( string generation ) => $"commanddeck-codex-{generation}";

    public static IReadOnlyList<string> BuildArguments( CodexLaunchRequest request )
    {
        ArgumentNullException.ThrowIfNull( request );
        var socket = request.SocketPath;
        if ( socket is null )
        {
            throw new InvalidOperationException( "A socket path is required." );
        }

        List<string> arguments =
        [
            "--user",
            "--unit=" + UnitName(request.Generation),
            "--collect",
            "--quiet",
            "--property=WorkingDirectory=" + request.WorkingDirectory,
            "--setenv=CODEX_HOME=" + request.RuntimeHome,
            "--",
            request.BinaryPath
        ];
        arguments.AddRange( CodexArgv.AppServerSocket( socket ) );
        CodexArgv.AssertSafe( arguments );
        return arguments;
    }

    public async ValueTask<IAgentProcessHandle> LaunchAsync(
        CodexLaunchRequest request,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( request );
        if ( !File.Exists( systemdRunPath ) )
        {
            throw new AiOperationException(
                AiErrorCategories.Unavailable,
                safeSummary: "systemd-run is unavailable; unrestricted mode cannot start." );
        }
        var socketPath = request.SocketPath;
        if ( socketPath is null )
        {
            throw new InvalidOperationException( "A socket path is required." );
        }

        var arguments = BuildArguments( request );
        var started = await ProcessRunner.RunAsync(
            systemdRunPath,
            arguments,
            null,
            null,
            TimeSpan.FromSeconds( AgentLimits.StartupDeadlineSeconds ),
            cancellationToken ).ConfigureAwait( false );
        if ( started.ExitCode != 0 )
        {
            throw new AiOperationException(
                AiErrorCategories.Unavailable,
                safeSummary: "The user manager refused to start the unrestricted agent unit." );
        }
        var unit = UnitName( request.Generation );
        try
        {
            await WaitForSocketAsync( socketPath, cancellationToken ).ConfigureAwait( false );
            var socket = await ConnectVerifiedAsync( socketPath, cancellationToken ).ConfigureAwait( false );
            var webSocket = await UpgradeAsync( socket, cancellationToken ).ConfigureAwait( false );
            return new TransientUnitHandle( webSocket, unit, systemctlPath );
        }
        catch
        {
            await StopUnitAsync( systemctlPath, unit ).ConfigureAwait( false );
            throw;
        }
    }

    private static async ValueTask WaitForSocketAsync( string socketPath, CancellationToken cancellationToken )
    {
        var deadline = DateTime.UtcNow.AddSeconds( AgentLimits.StartupDeadlineSeconds );
        var danglingLink = false;
        while ( DateTime.UtcNow < deadline )
        {
            // Codex 0.157 and later links this path to its socket under /tmp. A
            // link without a socket is not ready yet; a daemon with a private
            // /tmp never sees the socket.
            var exists = File.Exists( socketPath );
            danglingLink = exists && File.ResolveLinkTarget( socketPath, returnFinalTarget: true ) is { Exists: false };
            if ( exists && !danglingLink && OperatingSystem.IsLinux( ) )
            {
                var mode = File.GetUnixFileMode( socketPath );
                if ( (mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead |
                    UnixFileMode.OtherWrite)) != 0 )
                {
                    throw new AiOperationException(
                        AiErrorCategories.PolicyDenied,
                        safeSummary: "The unrestricted agent socket is not private." );
                }
                return;
            }
            await Task.Delay( 100, cancellationToken ).ConfigureAwait( false );
        }
        throw danglingLink
            ? new AiOperationException(
                AiErrorCategories.Unavailable,
                safeSummary: "The daemon cannot reach the Codex socket because its service unit hides the " +
                    "shared temporary directory. Reinstall CommandDeck to update the unit." )
            : new AiOperationException(
                AiErrorCategories.Timeout,
                safeSummary: "The unrestricted agent did not open its socket in time." );
    }

    private static async ValueTask<Socket> ConnectVerifiedAsync( string socketPath, CancellationToken cancellationToken )
    {
        Socket socket = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        try
        {
            await socket.ConnectAsync( new UnixDomainSocketEndPoint( socketPath ), cancellationToken ).ConfigureAwait( false );
            var credentials = new byte[12];
            var length = socket.GetRawSocketOption( SolSocket, SoPeerCred, credentials );
            if ( length < 12 )
            {
                throw new AiOperationException(
                    AiErrorCategories.PolicyDenied,
                    safeSummary: "The unrestricted agent peer identity could not be read." );
            }
            var peerUid = MemoryMarshal.Read<uint>( credentials.AsSpan( 4, 4 ) );
            if ( peerUid != LinuxMountIdentity.CurrentEffectiveUserId( ) )
            {
                throw new AiOperationException(
                    AiErrorCategories.PolicyDenied,
                    safeSummary: "The unrestricted agent socket belongs to another user." );
            }
            return socket;
        }
        catch
        {
            socket.Dispose( );
            throw;
        }
    }

    private static async ValueTask<ClientWebSocket> UpgradeAsync( Socket socket, CancellationToken cancellationToken )
    {
        SocketsHttpHandler handler = new( )
        {
            ConnectCallback = ( _, _ ) => ValueTask.FromResult<Stream>( new NetworkStream( socket, ownsSocket: true ) ),
            AllowAutoRedirect = false
        };
        ClientWebSocket webSocket = new( );
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            timeout.CancelAfter( TimeSpan.FromSeconds( AgentLimits.StartupDeadlineSeconds ) );
            await webSocket.ConnectAsync(
                new Uri( "ws://localhost/" ),
                new HttpMessageInvoker( handler ),
                timeout.Token ).ConfigureAwait( false );
            return webSocket;
        }
        catch
        {
            webSocket.Dispose( );
            handler.Dispose( );
            throw;
        }
    }

    internal static async ValueTask StopUnitAsync( string systemctlPath, string unit )
    {
        try
        {
            await ProcessRunner.RunAsync(
                systemctlPath,
                ["--user", "stop", unit],
                null,
                null,
                TimeSpan.FromSeconds( AgentLimits.StartupDeadlineSeconds ),
                CancellationToken.None ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
        }
    }
}

public sealed class WebSocketTransport( ClientWebSocket socket, int maximumBytes, Func<ValueTask>? onDispose = null )
    : IJsonRpcTransport
{
    private int _disposed;

    public async ValueTask<string?> ReceiveAsync( CancellationToken cancellationToken )
    {
        ArrayBufferWriter<byte> buffer = new( 16 * 1024 );
        while ( true )
        {
            var memory = buffer.GetMemory( 16 * 1024 );
            var result = await socket.ReceiveAsync( memory, cancellationToken )
                .ConfigureAwait( false );
            if ( result.MessageType == WebSocketMessageType.Close )
            {
                return null;
            }
            buffer.Advance( result.Count );
            if ( buffer.WrittenCount > maximumBytes )
            {
                throw new PayloadTooLargeException( "The agent frame exceeds the supported size." );
            }
            if ( result.EndOfMessage )
            {
                return Encoding.UTF8.GetString( buffer.WrittenSpan );
            }
        }
    }

    public ValueTask SendAsync( string message, CancellationToken cancellationToken ) =>
        socket.SendAsync( new ReadOnlyMemory<byte>( Encoding.UTF8.GetBytes( message ) ), WebSocketMessageType.Text, true, cancellationToken );

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        try
        {
            if ( socket.State == WebSocketState.Open )
            {
                using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 2 ) );
                await socket.CloseOutputAsync( WebSocketCloseStatus.NormalClosure, "stopping", timeout.Token )
                    .ConfigureAwait( false );
            }
        }
        catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or IOException )
        {
        }
        socket.Dispose( );
        if ( onDispose is not null )
        {
            await onDispose( ).ConfigureAwait( false );
        }
    }
}

public sealed class TransientUnitHandle : IAgentProcessHandle
{
    private readonly TaskCompletionSource _exited = new( TaskCreationOptions.RunContinuationsAsynchronously );
    private readonly string _unit;
    private readonly string _systemctlPath;
    private int _disposed;

    public TransientUnitHandle( ClientWebSocket socket, string unit, string systemctlPath )
    {
        _unit = unit;
        _systemctlPath = systemctlPath;
        Transport = new WebSocketTransport( socket, AgentLimits.MaximumFrameBytes, ( ) =>
        {
            _exited.TrySetResult( );
            return ValueTask.CompletedTask;
        } );
    }

    public IJsonRpcTransport Transport { get; }

    public Task Exited => _exited.Task;

    public string Unit => _unit;

    public async ValueTask StopAsync( CancellationToken cancellationToken )
    {
        await TransientUnitCodexLauncher.StopUnitAsync( _systemctlPath, _unit ).ConfigureAwait( false );
        _exited.TrySetResult( );
    }

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        await Transport.DisposeAsync( ).ConfigureAwait( false );
        await StopAsync( CancellationToken.None ).ConfigureAwait( false );
    }
}
