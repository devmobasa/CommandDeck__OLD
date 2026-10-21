using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task SocketAiClearIsACompletionBarrierAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-ai-clear-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        await using var provider = await ScriptedOllamaServer.StartAsync( );
        var socket = SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" );
        var config = Path.Combine( root, "config.json" );
        CommandDeckOptions options = new( )
        {
            Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
            Indexing = new IndexingOptions
            {
                Roots = [],
                IndexDesktopApplications = false,
                IndexOmarchyCommands = false
            },
            Privacy = new PrivacyOptions { StoreUsageHistory = false, NeverIndex = [] },
            Ai = new AiOptions
            {
                Enabled = true,
                Providers =
                [
                    new AiProviderOptions
                    {
                        Id = "clear-test",
                        DisplayName = "Clear test",
                        Kind = AiAdapterKinds.Ollama,
                        BaseUrl = provider.BaseUrl + "ollama",
                        Model = "clear-model"
                    }
                ]
            }
        };
        await File.WriteAllTextAsync( config, System.Text.Json.JsonSerializer.Serialize(
            options, CommandDeckJsonContext.Default.CommandDeckOptions ) );

        using CancellationTokenSource serverCancellation = new( );
        try
        {
            await using var runtime = await CommandDeckRuntime.CreateAsync(
                config, CancellationToken.None );
            UnixProtocolServer server = new( runtime, socket );
            var serverTask = server.RunAsync( serverCancellation.Token );
            await using var client = await ConnectWithRetryAsync( socket );
            var first = await RequestPreflightAsync( client, 1, "generation to clear" );
            var queued = await RequestPreflightAsync( client, 2, "stale queued preflight" );

            await client.SendAsync( new ProtocolRequest
            {
                RequestId = 3,
                Operation = ProtocolOperations.AiStart,
                PreflightId = first.Id,
                PayloadDigest = first.PayloadDigest,
                Generation = first.Generation,
                SessionId = "socket-clear-session-000000000000000"
            }, CancellationToken.None );
            await provider.GenerationStarted.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            await SendQueuedPreflightsAsync( client );
            await client.SendAsync( new ProtocolRequest
            {
                RequestId = 7,
                Operation = ProtocolOperations.AiClear
            }, CancellationToken.None );

            (var generationDone, var clearDone, var queuedRejected, var ordered, var terminal) =
                await ReadClearCompletionAsync( client );
            AssertClearCompletion( generationDone, clearDone, queuedRejected, ordered );
            Equal( AiGenerationOutcomes.Cancelled, terminal?.Outcome );

            await client.SendAsync( new ProtocolRequest
            {
                RequestId = 8,
                Operation = ProtocolOperations.AiStart,
                PreflightId = queued.Id,
                PayloadDigest = queued.PayloadDigest,
                Generation = queued.Generation,
                SessionId = "socket-stale-session-00000000000000"
            }, CancellationToken.None );
            var stale = await ReadMessageAsync(
                client, 8, ProtocolMessageTypes.AiDone, CancellationToken.None );
            AssertStaleTerminal( stale );

            await client.SendAsync( new ProtocolRequest
            {
                RequestId = 9,
                Operation = ProtocolOperations.AiOutput,
                Generation = first.Generation,
                OutputAction = "copy"
            }, CancellationToken.None );
            var output = await ReadMessageAsync(
                client, 9, ProtocolMessageTypes.ActionResult, CancellationToken.None );
            AssertFailedAction(
                output,
                "Cleared generation unexpectedly retained executable output." );
            await ProveCompletedOutputAndChallengeArePurgedAsync( client, root );

            serverCancellation.Cancel( );
            await serverTask;
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async ValueTask SendQueuedPreflightsAsync( UnixProtocolClient client )
    {
        for ( long requestId = 4; requestId <= 6; requestId++ )
        {
            await client.SendAsync( new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.AiPreflight,
                AiDraft = new AiDraft
                {
                    ProviderId = "clear-test",
                    ModelId = "clear-model",
                    UserText = $"queued operation {requestId}",
                    Attachments = []
                }
            }, CancellationToken.None );
        }
    }

    private static async ValueTask ProveCompletedOutputAndChallengeArePurgedAsync(
        UnixProtocolClient client,
        string root )
    {
        var completed = await RequestPreflightAsync( client, 10, "completed output" );
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 11,
            Operation = ProtocolOperations.AiStart,
            PreflightId = completed.Id,
            PayloadDigest = completed.PayloadDigest,
            Generation = completed.Generation,
            SessionId = "socket-complete-session-000000000000"
        }, CancellationToken.None );
        var completedTerminal = await ReadMessageAsync(
            client, 11, ProtocolMessageTypes.AiDone, CancellationToken.None );
        Equal( AiGenerationOutcomes.Completed, completedTerminal?.AiDone?.Outcome );

        var existingOutput = Path.Combine( root, "existing-output.txt" );
        await File.WriteAllTextAsync( existingOutput, "original" );
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 12,
            Operation = ProtocolOperations.AiOutput,
            Generation = completed.Generation,
            OutputAction = "save",
            Path = existingOutput
        }, CancellationToken.None );
        var challenge = await ReadMessageAsync(
            client, 12, ProtocolMessageTypes.ActionResult, CancellationToken.None );
        var confirmationToken = RequireOverwriteChallenge( challenge );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 13,
            Operation = ProtocolOperations.AiClear
        }, CancellationToken.None );
        _ = await ReadMessageAsync(
            client, 13, ProtocolMessageTypes.Done, CancellationToken.None );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 14,
            Operation = ProtocolOperations.AiOutput,
            Generation = completed.Generation,
            OutputAction = "save",
            Path = existingOutput,
            ConfirmationToken = confirmationToken
        }, CancellationToken.None );
        var purged = await ReadMessageAsync(
            client, 14, ProtocolMessageTypes.ActionResult, CancellationToken.None );
        AssertFailedAction(
            purged,
            "ai.clear retained completed output or its overwrite authority." );
        Equal( "original", await File.ReadAllTextAsync( existingOutput ) );
    }

    private static void AssertClearCompletion(
        bool generationDone,
        bool clearDone,
        bool queuedRejected,
        bool ordered )
    {
        if ( !generationDone || !clearDone || !queuedRejected || !ordered )
        {
            throw new InvalidOperationException(
                $"Completion state was generation={generationDone}, clear={clearDone}, " +
                $"queuedRejected={queuedRejected}, ordered={ordered}." );
        }
    }

    private static void AssertStaleTerminal( ProtocolMessage? stale )
    {
        if ( stale?.AiDone?.Outcome != AiGenerationOutcomes.Failed ||
            stale.AiDone.Error?.Category != AiErrorCategories.StalePreflight )
        {
            throw new InvalidOperationException(
                "Cleared preflight was not rejected as a stale AI terminal." );
        }
    }

    private static string RequireOverwriteChallenge( ProtocolMessage? challenge )
    {
        if ( challenge?.ActionResult?.Succeeded != false ||
            string.IsNullOrWhiteSpace( challenge.ActionResult.ConfirmationToken ) )
        {
            throw new InvalidOperationException(
                "Socket save did not issue an overwrite confirmation challenge." );
        }
        return challenge.ActionResult.ConfirmationToken;
    }

    private static void AssertFailedAction( ProtocolMessage? message, string failure )
    {
        if ( message?.ActionResult?.Succeeded != false )
        {
            throw new InvalidOperationException( failure );
        }
    }

    private static async ValueTask<(
        bool GenerationDone,
        bool ClearDone,
        bool QueuedRejected,
        bool Ordered,
        AiDone? Terminal)>
        ReadClearCompletionAsync( UnixProtocolClient client )
    {
        var generationDone = false;
        var clearDone = false;
        HashSet<long> queuedErrors = [];
        HashSet<long> queuedDone = [];
        var messageIndex = 0;
        var generationDoneIndex = -1;
        var clearDoneIndex = -1;
        AiDone? terminal = null;
        using CancellationTokenSource readTimeout = new( TimeSpan.FromSeconds( 5 ) );
        await foreach ( var line in client.ReadRawMessagesAsync( readTimeout.Token ) )
        {
            var message = DeserializeMessage( line );
            messageIndex++;
            switch (message?.RequestId, message?.Type)
            {
                case (3, ProtocolMessageTypes.AiDone ):
                    terminal = message.AiDone;
                    break;
                case (3, ProtocolMessageTypes.Done ):
                    generationDone = true;
                    generationDoneIndex = messageIndex;
                    break;
                case ( >= 4 and <= 6, ProtocolMessageTypes.Error ):
                    queuedErrors.Add( message.RequestId );
                    break;
                case ( >= 4 and <= 6, ProtocolMessageTypes.Done ):
                    queuedDone.Add( message.RequestId );
                    break;
                case (7, ProtocolMessageTypes.Error ):
                    throw new InvalidOperationException( $"ai.clear failed: {message.Error}" );
                case (7, ProtocolMessageTypes.Done ):
                    clearDone = true;
                    clearDoneIndex = messageIndex;
                    break;
            }
            if ( generationDone && clearDone && queuedDone.Count == 3 )
            {
                break;
            }
        }
        return (
            generationDone,
            clearDone,
            queuedErrors.SetEquals( [4L, 5L, 6L] ) && queuedDone.SetEquals( [4L, 5L, 6L] ),
            generationDoneIndex > 0 && clearDoneIndex > generationDoneIndex,
            terminal);
    }

    private static async ValueTask<AiPreflight> RequestPreflightAsync(
        UnixProtocolClient client,
        long requestId,
        string prompt )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = requestId,
            Operation = ProtocolOperations.AiPreflight,
            AiDraft = new AiDraft
            {
                ProviderId = "clear-test",
                ModelId = "clear-model",
                UserText = prompt,
                Attachments = []
            }
        }, CancellationToken.None );
        var message = await ReadMessageAsync(
            client, requestId, ProtocolMessageTypes.AiPreflightResult, CancellationToken.None );
        var result = message?.AiPreflight;
        if ( result is null )
        {
            throw new InvalidOperationException( "Socket preflight did not return authority." );
        }

        return result;
    }

    private sealed class ScriptedOllamaServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cancellation = new( );
        private readonly Task _server;
        private readonly List<Task> _handlers = [];
        private readonly object _handlersLock = new( );
        private int _generationCount;

        private ScriptedOllamaServer( HttpListener listener, string baseUrl )
        {
            _listener = listener;
            BaseUrl = baseUrl;
            _server = ServeAsync( );
        }

        public string BaseUrl { get; }
        public TaskCompletionSource GenerationStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously );

        public static ValueTask<ScriptedOllamaServer> StartAsync( )
        {
            int port;
            using ( TcpListener reservation = new( IPAddress.Loopback, 0 ) )
            {
                reservation.Start( );
                port = (( IPEndPoint ) reservation.LocalEndpoint).Port;
            }
            var baseUrl = $"http://127.0.0.1:{port}/";
            HttpListener listener = new( );
            listener.Prefixes.Add( baseUrl );
            listener.Start( );
            return ValueTask.FromResult( new ScriptedOllamaServer( listener, baseUrl ) );
        }

        private async Task ServeAsync( )
        {
            try
            {
                while ( !_cancellation.IsCancellationRequested )
                {
                    var context = await _listener.GetContextAsync( )
                        .WaitAsync( _cancellation.Token );
                    var handler = HandleAsync( context );
                    lock ( _handlersLock )
                    {
                        _handlers.Add( handler );
                    }
                }
            }
            catch ( Exception exception ) when (
                exception is OperationCanceledException or HttpListenerException or IOException )
            {
            }
        }

        private async Task HandleAsync( HttpListenerContext context )
        {
            try
            {
                await context.Request.InputStream.CopyToAsync(
                    Stream.Null, _cancellation.Token );
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/x-ndjson";
                context.Response.SendChunked = true;
                var generation = Interlocked.Increment( ref _generationCount );
                var payload = generation == 1
                    ? "{\"model\":\"clear-model\",\"message\":{\"content\":\"partial\"}," +
                        "\"done\":false}\n"
                    : "{\"model\":\"clear-model\",\"message\":{\"content\":\"complete\"}," +
                        "\"done\":false}\n" +
                        "{\"model\":\"clear-model\",\"message\":{\"content\":\"\"}," +
                        "\"done\":true,\"done_reason\":\"stop\"}\n";
                var chunk = Encoding.UTF8.GetBytes( payload );
                await context.Response.OutputStream.WriteAsync( chunk, _cancellation.Token );
                await context.Response.OutputStream.FlushAsync( _cancellation.Token );
                if ( generation == 1 )
                {
                    GenerationStarted.TrySetResult( );
                    await Task.Delay( Timeout.InfiniteTimeSpan, _cancellation.Token );
                }
                else
                {
                    context.Response.Close( );
                }
            }
            catch ( Exception exception ) when (
                exception is OperationCanceledException or HttpListenerException or IOException )
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
