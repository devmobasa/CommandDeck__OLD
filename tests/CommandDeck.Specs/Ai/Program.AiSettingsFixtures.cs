using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static (Task Task, StrongBox<AiDone?> Done) StartHeldGeneration( CommandDeckRuntime runtime, string client, string providerId )
    {
        StrongBox<AiDone?> done = new( );
        var task = Task.Run( async ( ) =>
        {
            var preflight = await runtime.CreateAiPreflightAsync( client, new AiDraft
            {
                ProviderId = providerId,
                ModelId = "m1",
                UserText = "hold this stream",
                Attachments = []
            }, default );
            await runtime.StreamAiAsync( client, preflight.Id, preflight.PayloadDigest, preflight.Generation,
                _ => ValueTask.CompletedTask, value =>
                {
                    done.Value = value;
                    return ValueTask.CompletedTask;
                }, default );
        } );
        return (task, done);
    }

    private static async Task<string> WriteManagedSpecConfigAsync( string root, AiOptions ai )
    {
        var config = Path.Combine( root, "config.json" );
        CommandDeckOptions options = new( )
        {
            Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
            Indexing = new IndexingOptions { Roots = [], IndexDesktopApplications = false, IndexOmarchyCommands = false },
            Privacy = new PrivacyOptions { StoreUsageHistory = false, NeverIndex = [] },
            Ai = ai,
            Agent = new AgentOptions { RuntimeDirectory = Path.Combine( root, "runtime" ), StateDirectory = Path.Combine( root, "state" ) }
        };
        await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
        return config;
    }

    private static AiProviderOptions LocalProviderDefinition(
        string id,
        string baseUrl,
        string model = "m1",
        string? displayName = null ) => new( )
        {
            Id = id,
            DisplayName = displayName ?? id,
            Kind = AiAdapterKinds.Ollama,
            BaseUrl = baseUrl.TrimEnd( '/' ),
            Model = model
        };

    private static UnixFileMode UnixModeOf( string path ) =>
        OperatingSystem.IsLinux( ) ? File.GetUnixFileMode( path ) : UnixFileMode.None;

    private static async Task<AiOperationException> ThrowsAiAsync<T>( Func<Task<T>> action )
    {
        try
        {
            _ = await action( );
        }
        catch ( AiOperationException exception )
        {
            return exception;
        }
        throw new InvalidOperationException( "Expected AiOperationException." );
    }

    /// <summary>
    /// Loopback Ollama stand-in: <c>GET /api/tags</c> lists two models and
    /// <c>POST /api/chat</c> streams one chunk, then holds until released.
    /// </summary>
    private sealed class ManagedAiFixtureServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cancellation = new( );
        private readonly Task _server;
        private readonly List<Task> _handlers = [];
        private readonly object _handlersLock = new( );
        private TaskCompletionSource _release = new( TaskCreationOptions.RunContinuationsAsynchronously );
        private int _tagRequests;
        private int _chatRequests;

        private ManagedAiFixtureServer( HttpListener listener, string baseUrl )
        {
            _listener = listener;
            BaseUrl = baseUrl;
            _server = ServeAsync( );
        }

        public string BaseUrl { get; }
        public int TagStatus { get; set; } = 200;
        public bool EmptyTags { get; set; }
        public int TagRequests => Volatile.Read( ref _tagRequests );
        public int ChatRequests => Volatile.Read( ref _chatRequests );
        public TaskCompletionSource GenerationStarted { get; private set; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public static ValueTask<ManagedAiFixtureServer> StartAsync( )
        {
            int port;
            using ( System.Net.Sockets.TcpListener reservation = new( IPAddress.Loopback, 0 ) )
            {
                reservation.Start( );
                port = (( IPEndPoint ) reservation.LocalEndpoint).Port;
            }
            var baseUrl = $"http://127.0.0.1:{port}/";
            HttpListener listener = new( );
            listener.Prefixes.Add( baseUrl );
            listener.Start( );
            return ValueTask.FromResult( new ManagedAiFixtureServer( listener, baseUrl ) );
        }

        public void ResetGeneration( )
        {
            GenerationStarted = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
            _release = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        }

        public void ReleaseGeneration( ) => _release.TrySetResult( );

        private async Task ServeAsync( )
        {
            try
            {
                while ( !_cancellation.IsCancellationRequested )
                {
                    var context = await _listener.GetContextAsync( ).WaitAsync( _cancellation.Token );
                    var handler = HandleAsync( context );
                    lock ( _handlersLock )
                    {
                        _handlers.Add( handler );
                    }
                }
            }
            catch ( Exception exception ) when ( exception is OperationCanceledException or HttpListenerException or IOException )
            {
            }
        }

        private async Task HandleAsync( HttpListenerContext context )
        {
            try
            {
                await context.Request.InputStream.CopyToAsync( Stream.Null, _cancellation.Token );
                if ( context.Request.Url?.AbsolutePath.EndsWith( "/api/tags", StringComparison.Ordinal ) == true )
                {
                    Interlocked.Increment( ref _tagRequests );
                    context.Response.StatusCode = TagStatus;
                    context.Response.ContentType = "application/json";
                    var body = Encoding.UTF8.GetBytes( EmptyTags
                        ? "{\"models\":[]}"
                        : "{\"models\":[{\"name\":\"m1\"},{\"name\":\"m2\"}]}" );
                    await context.Response.OutputStream.WriteAsync( body, _cancellation.Token );
                    context.Response.Close( );
                    return;
                }
                Interlocked.Increment( ref _chatRequests );
                var release = _release;
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/x-ndjson";
                context.Response.SendChunked = true;
                var first = Encoding.UTF8.GetBytes( "{\"model\":\"m1\",\"message\":{\"content\":\"partial\"},\"done\":false}\n" );
                await context.Response.OutputStream.WriteAsync( first, _cancellation.Token );
                await context.Response.OutputStream.FlushAsync( _cancellation.Token );
                GenerationStarted.TrySetResult( );
                await release.Task.WaitAsync( _cancellation.Token );
                var last = Encoding.UTF8.GetBytes( "{\"model\":\"m1\",\"message\":{\"content\":\" done\"},\"done\":true,\"done_reason\":\"stop\"}\n" );
                await context.Response.OutputStream.WriteAsync( last, _cancellation.Token );
                context.Response.Close( );
            }
            catch ( Exception exception ) when ( exception is OperationCanceledException or HttpListenerException or IOException )
            {
            }
        }

        public async ValueTask DisposeAsync( )
        {
            _cancellation.Cancel( );
            _listener.Close( );
            await _server.ConfigureAwait( false );
            Task[] handlers;
            lock ( _handlersLock )
            {
                handlers = [.. _handlers];
            }
            await Task.WhenAll( handlers ).ConfigureAwait( false );
            _cancellation.Dispose( );
        }
    }
}
