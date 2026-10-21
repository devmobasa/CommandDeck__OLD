using System.Net.Sockets;
using System.Text;

namespace CommandDeck;

public sealed partial class UnixProtocolServer
{
    internal const int MaximumClients = 32;
    private int _connectedClients;
    internal int ConnectedClients => Volatile.Read( ref _connectedClients );
    internal Task Completion { get; private set; } = Task.CompletedTask;
    private readonly CommandDeckRuntime _runtime;
    private readonly string _socketPath;

    public UnixProtocolServer( CommandDeckRuntime runtime, string? socketPath )
    {
        _runtime = runtime;
        _socketPath = string.IsNullOrWhiteSpace( socketPath )
            ? PathExpansion.GetDefaultSocketPath( )
            : PathExpansion.Expand( socketPath );
    }

    public Task RunAsync( CancellationToken cancellationToken ) => Completion = RunCoreAsync( cancellationToken );

    private async Task RunCoreAsync( CancellationToken cancellationToken )
    {
        using var shutdown = cancellationToken.Register( _runtime.CancelBackgroundIndexing );
        PrepareSocketPath( );

        using Socket listener = new(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified );
        listener.Bind( new UnixDomainSocketEndPoint( _socketPath ) );
        listener.Listen( 32 );

        TrySetSocketPermissions( _socketPath );
        AppLog.Info( $"Listening on {_socketPath}" );
        using CancellationTokenSource connections = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        using SemaphoreSlim admission = new( MaximumClients, MaximumClients );
        ClientTaskTracker clients = new( );

        try
        {
            while ( !cancellationToken.IsCancellationRequested )
            {
                var socket = await listener.AcceptAsync( cancellationToken ).ConfigureAwait( false );
                if ( !admission.Wait( 0, CancellationToken.None ) )
                {
                    socket.Dispose( );
                    continue;
                }

                clients.Track( RunClientAsync( socket, admission, connections.Token ) );
            }
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
        }
        finally
        {
            _runtime.CancelBackgroundIndexing( );
            listener.Dispose( );
            await connections.CancelAsync( ).ConfigureAwait( false );
            await Task.WhenAll( clients.Snapshot( ) ).ConfigureAwait( false );
            TryDeleteSocket( );
        }
    }

    private async Task RunClientAsync( Socket socket, SemaphoreSlim admission, CancellationToken cancellationToken )
    {
        // Closing the descriptor also releases a writer blocked behind a slow reader.
        using var close = cancellationToken.Register( socket.Dispose );
        Interlocked.Increment( ref _connectedClients );
        try
        {
            await HandleClientSafelyAsync( socket, cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception )
        {
            AppLog.Debug( $"Client connection stopped: {exception.GetType( ).Name}" );
        }
        finally
        {
            socket.Dispose( );
            Interlocked.Decrement( ref _connectedClients );
            admission.Release( );
        }
    }

    private async Task HandleClientSafelyAsync(
        Socket socket,
        CancellationToken serverCancellationToken )
    {
        using ( socket )
        {
            await using ( NetworkStream stream = new( socket, ownsSocket: false ) )
            {
                await using ( StreamWriter writer = new(
                stream,
                new UTF8Encoding( false ),
                bufferSize: 16_384,
                leaveOpen: true )
                {
                    AutoFlush = true
                } )
                {
                    BoundedLineReader boundedReader = new(
                        stream,
                        ProtocolLimits.MaximumRequestBytes );
                    using ClientWriter messages = new( writer, socket );
                    await using ClientSession session = new( _runtime, serverCancellationToken );
                    try
                    {
                        while ( !serverCancellationToken.IsCancellationRequested )
                        {
                            string? line;
                            try
                            {
                                line = await boundedReader.ReadLineAsync( serverCancellationToken )
                                    .ConfigureAwait( false );
                            }
                            catch ( Exception exception ) when (
                                exception is InvalidDataException or PayloadTooLargeException )
                            {
                                await messages.WriteAsync( new ProtocolMessage
                                {
                                    RequestId = 0,
                                    Type = ProtocolMessageTypes.Error,
                                    Error = exception.Message
                                } ).ConfigureAwait( false );
                                continue;
                            }

                            if ( line is null )
                            {
                                break;
                            }

                            ProtocolRequest? request = null;
                            try
                            {
                                request = ProtocolJson.DeserializeRequest( line );
                                if ( request is not null )
                                {
                                    ProtocolRequestValidator.Validate( request, _runtime.Contributions );
                                }
                            }
                            catch ( Exception exception ) when (
                                exception is System.Text.Json.JsonException or InvalidOperationException )
                            {
                                var requestId = request?.RequestId ?? 0;
                                if ( session.HasActiveRequest( requestId ) )
                                {
                                    await WriteDuplicateActiveRequestAsync( messages ).ConfigureAwait( false );
                                    break;
                                }

                                await messages.WriteAsync( new ProtocolMessage
                                {
                                    RequestId = requestId,
                                    Type = ProtocolMessageTypes.Error,
                                    Error = exception is System.Text.Json.JsonException
                                        ? $"Invalid JSON: {exception.Message}"
                                        : SafeProtocolError( exception )
                                } ).ConfigureAwait( false );
                                if ( requestId > 0 )
                                {
                                    await messages.DoneAsync( requestId ).ConfigureAwait( false );
                                }
                                continue;
                            }

                            if ( request is null )
                            {
                                continue;
                            }

                            if ( session.HasActiveRequest( request.RequestId ) )
                            {
                                await WriteDuplicateActiveRequestAsync( messages ).ConfigureAwait( false );
                                break;
                            }

                            if ( string.Equals(
                                request.Operation,
                                ProtocolOperations.Cancel,
                                StringComparison.Ordinal ) )
                            {
                                await HandleCancelRequestAsync( messages, request, session )
                                    .ConfigureAwait( false );
                                continue;
                            }

                            if ( !ProtocolRoutes.TryResolve( request.Operation, out var route ) )
                            {
                                await messages.WriteAsync( new ProtocolMessage
                                {
                                    RequestId = request.RequestId,
                                    Type = ProtocolMessageTypes.Error,
                                    Error = SafeProtocolError( UnknownOperation( request ) )
                                } ).ConfigureAwait( false );
                                await messages.DoneAsync( request.RequestId ).ConfigureAwait( false );
                                continue;
                            }

                            var admissionStatus = session.Admit(
                                request,
                                route.Admission,
                                out var requestCancellation,
                                out var aiEpoch );
                            if ( admissionStatus == RequestAdmissionStatus.AtCapacity )
                            {
                                await messages.WriteAsync( new ProtocolMessage
                                {
                                    RequestId = request.RequestId,
                                    Type = ProtocolMessageTypes.Error,
                                    Error = "This client already has four active requests."
                                } ).ConfigureAwait( false );
                                await messages.DoneAsync( request.RequestId ).ConfigureAwait( false );
                                continue;
                            }

                            if ( admissionStatus == RequestAdmissionStatus.Duplicate )
                            {
                                await WriteDuplicateActiveRequestAsync( messages ).ConfigureAwait( false );
                                break;
                            }

                            if ( requestCancellation is null )
                            {
                                throw new InvalidOperationException( "Admitted request has no cancellation owner." );
                            }

                            // ai.clear purges at once and then holds the reader until the
                            // AI requests admitted before it finish, so nothing read after
                            // the clear can observe the old state.
                            if ( route.Admission == ProtocolAdmission.AiClearBarrier )
                            {
                                _runtime.ClearDirectAiClient( session.OwnerId );
                                await HandleTrackedRequestAsync(
                                    messages,
                                    request,
                                    route,
                                    requestCancellation,
                                    session,
                                    aiEpoch ).ConfigureAwait( false );
                                continue;
                            }

                            session.Track( HandleTrackedRequestAsync(
                                messages,
                                request,
                                route,
                                requestCancellation,
                                session,
                                aiEpoch ) );
                        }
                    }
                    catch ( OperationCanceledException ) when ( serverCancellationToken.IsCancellationRequested )
                    {
                    }
                    catch ( IOException exception )
                    {
                        AppLog.Debug( $"Client disconnected: {exception.Message}" );
                    }
                    catch ( Exception exception )
                    {
                        AppLog.Error( "Client handler failed.", exception );
                    }
                }
            }
        }
    }

    private static ValueTask WriteDuplicateActiveRequestAsync( ClientWriter writer ) =>
        writer.WriteAsync( new ProtocolMessage
        {
            RequestId = 0,
            Type = ProtocolMessageTypes.Error,
            Error = "An active requestId was reused. This connection is closing."
        } );

    private static async ValueTask HandleCancelRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientSession session )
    {
        session.CancelRequest( request.TargetRequestId );

        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }

    private async Task HandleTrackedRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ProtocolRoute route,
        CancellationTokenSource requestCancellation,
        ClientSession session,
        long aiEpoch )
    {
        try
        {
            await HandleRequestAsync(
                writer,
                request,
                route,
                session.ContextFor( request.RequestId, requestCancellation.Token ),
                session.AiBarrier,
                aiEpoch )
                .ConfigureAwait( false );
        }
        catch ( Exception exception ) when (
            exception is IOException or ObjectDisposedException )
        {
            AppLog.Debug( $"Client disconnected during request: {exception.Message}" );
        }
        finally
        {
            session.Finish( request.RequestId, route.Admission, requestCancellation );
        }
    }

    private static InvalidOperationException UnknownOperation( ProtocolRequest request ) =>
        new( $"Unknown operation '{request.Operation}'." );

    private static string RequireValue( string? value, string message ) =>
        string.IsNullOrWhiteSpace( value ) ? throw new InvalidOperationException( message ) : value;

    private void PrepareSocketPath( )
    {
        Directory.CreateDirectory( Path.GetDirectoryName( _socketPath )! );
        if ( !File.Exists( _socketPath ) )
        {
            return;
        }

        try
        {
            using Socket probe = new(
                AddressFamily.Unix,
                SocketType.Stream,
                ProtocolType.Unspecified );
            probe.Connect( new UnixDomainSocketEndPoint( _socketPath ) );
            throw new InvalidOperationException(
                $"Another CommandDeck daemon is already listening on {_socketPath}." );
        }
        catch ( SocketException )
        {
            File.Delete( _socketPath );
        }
    }

    private void TryDeleteSocket( )
    {
        try
        {
            File.Delete( _socketPath );
        }
        catch ( IOException )
        {
        }
    }

    private static void TrySetSocketPermissions( string path )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite );
        }
        catch ( Exception exception ) when (
            exception is PlatformNotSupportedException or IOException or UnauthorizedAccessException )
        {
            AppLog.Debug( $"Could not restrict socket permissions: {exception.Message}" );
        }
    }

    private static void TryCancel( CancellationTokenSource source )
    {
        try
        {
            source.Cancel( );
        }
        catch ( ObjectDisposedException )
        {
        }
    }

    private sealed class ClientTaskTracker
    {
        private readonly object _gate = new( );
        private readonly HashSet<Task> _tasks = [];

        public void Track( Task task )
        {
            lock ( _gate )
            {
                _tasks.Add( task );
            }

            _ = task.ContinueWith(
                completed => Remove( completed ),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default );
        }

        public Task[] Snapshot( )
        {
            lock ( _gate )
            {
                return [.. _tasks];
            }
        }

        private void Remove( Task task )
        {
            lock ( _gate )
            {
                _tasks.Remove( task );
            }
        }
    }

    private sealed class ClientWriter : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly Socket _socket;
        private readonly SemaphoreSlim _gate = new( 1, 1 );

        public ClientWriter( StreamWriter writer, Socket socket )
        {
            _writer = writer;
            _socket = socket;
        }

        public ValueTask WriteAsync( ProtocolMessage message ) => WriteCancellableAsync( message, CancellationToken.None );

        public async ValueTask WriteCancellableAsync( ProtocolMessage message, CancellationToken cancellationToken )
        {
            using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
            using var close = deadline.Token.Register( _socket.Dispose );
            using var linked = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, deadline.Token );
            cancellationToken = linked.Token;
            await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
            try
            {
                var line = ProtocolJson.SerializeMessage( message );
                if ( ProtocolJson.Utf8ByteCount( line ) > ProtocolLimits.MaximumResponseBytes )
                {
                    line = ProtocolJson.SerializeMessage( new ProtocolMessage
                    {
                        RequestId = message.RequestId,
                        Type = ProtocolMessageTypes.Error,
                        Error = "Response exceeds the protocol frame limit."
                    } );
                }

                await _writer.WriteLineAsync( line.AsMemory( ), cancellationToken )
                    .ConfigureAwait( false );
            }
            finally
            {
                _gate.Release( );
            }
        }

        public ValueTask DoneAsync( long requestId, SearchCompletion? searchCompletion = null ) =>
            WriteAsync( new ProtocolMessage
            {
                RequestId = requestId,
                Type = ProtocolMessageTypes.Done,
                SearchCompletion = searchCompletion
            } );

        public void Dispose( ) => _gate.Dispose( );
    }
}
