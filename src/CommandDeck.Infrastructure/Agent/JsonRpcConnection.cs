using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommandDeck;

public interface IJsonRpcTransport : IAsyncDisposable
{
    ValueTask<string?> ReceiveAsync( CancellationToken cancellationToken );
    ValueTask SendAsync( string message, CancellationToken cancellationToken );
}

public sealed class JsonRpcException( int code, string message ) : InvalidOperationException( message )
{
    public int Code { get; } = code;
}

public sealed class JsonRpcConnection : IAsyncDisposable
{
    public const int DefaultMaximumServerRequests = 16;

    public const int DefaultMaximumUnsentReplies = 16;

    private const int ServerErrorCode = -32000;

    private readonly IJsonRpcTransport _transport;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new( );
    private readonly SemaphoreSlim _writeGate = new( 1, 1 );
    private readonly CancellationTokenSource _lifetime = new( );
    private readonly int _maximumPendingRequests;
    private readonly int _maximumServerRequests;
    private readonly HashSet<Task> _serverRequests = [];
    private readonly object _serverRequestsLock = new( );

    // Every server request holds a slot from admission until its reply is written:
    // the running handlers plus the replies that wait for the transport.
    private readonly SemaphoreSlim _unansweredServerRequests;
    private int _admittedServerRequests;
    private long _nextId;
    private Task? _readLoop;
    private int _disposed;

    public JsonRpcConnection(
        IJsonRpcTransport transport,
        int maximumPendingRequests = 64,
        int maximumServerRequests = DefaultMaximumServerRequests,
        int maximumUnsentReplies = DefaultMaximumUnsentReplies )
    {
        if ( transport is null )
        {
            throw new ArgumentNullException( nameof( transport ) );
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( maximumServerRequests );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( maximumUnsentReplies );

        _transport = transport;
        _maximumPendingRequests = maximumPendingRequests;
        _maximumServerRequests = maximumServerRequests;
        _unansweredServerRequests = new SemaphoreSlim(
            maximumServerRequests + maximumUnsentReplies,
            maximumServerRequests + maximumUnsentReplies );
    }

    public Func<string, JsonNode?, CancellationToken, ValueTask>? NotificationHandler { get; set; }

    public Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>>? RequestHandler { get; set; }

    public Exception? Fault { get; private set; }

    public Task Completion => _readLoop ?? Task.CompletedTask;

    public bool IsFaulted => Fault is not null;

    public void Start( )
    {
        if ( _readLoop is not null )
        {
            throw new InvalidOperationException( "The connection is already started." );
        }
        _readLoop = Task.Run( ReadLoopAsync );
    }

    public async ValueTask<JsonNode?> CallAsync(
        string method,
        JsonObject? parameters,
        CancellationToken cancellationToken )
    {
        if ( _pending.Count >= _maximumPendingRequests )
        {
            throw new JsonRpcException( -32000, "Too many pending requests." );
        }
        var id = Interlocked.Increment( ref _nextId );
        TaskCompletionSource<JsonNode?> completion = new( TaskCreationOptions.RunContinuationsAsynchronously );
        _pending[id] = completion;
        JsonObject frame = new( )
        {
            ["id"] = id,
            ["method"] = method
        };
        if ( parameters is not null )
        {
            frame["params"] = parameters;
        }
        try
        {
            await SendFrameAsync( frame, cancellationToken ).ConfigureAwait( false );
            using var registration = cancellationToken.Register(
                static state => (( TaskCompletionSource<JsonNode?> ) state!).TrySetCanceled( ),
                completion );
            return await completion.Task.ConfigureAwait( false );
        }
        finally
        {
            _pending.TryRemove( id, out _ );
        }
    }

    public ValueTask NotifyAsync( string method, JsonObject? parameters, CancellationToken cancellationToken )
    {
        JsonObject frame = new( )
        {
            ["method"] = method
        };
        if ( parameters is not null )
        {
            frame["params"] = parameters;
        }
        return SendFrameAsync( frame, cancellationToken );
    }

    private async ValueTask SendFrameAsync( JsonObject frame, CancellationToken cancellationToken )
    {
        var text = frame.ToJsonString( );
        await _writeGate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            await _transport.SendAsync( text, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _writeGate.Release( );
        }
    }

    private async Task ReadLoopAsync( )
    {
        var token = _lifetime.Token;
        try
        {
            while ( !token.IsCancellationRequested )
            {
                var line = await _transport.ReceiveAsync( token ).ConfigureAwait( false );
                if ( line is null )
                {
                    throw new EndOfStreamException( "The agent connection closed." );
                }
                if ( string.IsNullOrWhiteSpace( line ) )
                {
                    continue;
                }
                await DispatchAsync( line, token ).ConfigureAwait( false );
            }
        }
        catch ( Exception exception ) when ( exception is not OperationCanceledException || !token.IsCancellationRequested )
        {
            Fault = exception;
        }
        finally
        {
            FailPending( );
        }
    }

    private async ValueTask DispatchAsync( string line, CancellationToken token )
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse( line );
        }
        catch ( JsonException )
        {
            throw new InvalidDataException( "The agent sent a malformed frame." );
        }
        if ( node is not JsonObject frame )
        {
            throw new InvalidDataException( "The agent sent a non-object frame." );
        }
        var idNode = frame["id"];
        var method = ( string? ) frame["method"];
        if ( method is not null && idNode is not null )
        {
            await AdmitServerRequestAsync( idNode, method, frame["params"], token ).ConfigureAwait( false );
            return;
        }
        if ( method is not null )
        {
            if ( NotificationHandler is { } handler )
            {
                await handler( method, frame["params"], token ).ConfigureAwait( false );
            }
            return;
        }
        if ( idNode is not null && TryReadId( idNode, out var id ) &&
            _pending.TryRemove( id, out var completion ) )
        {
            if ( frame["error"] is JsonObject error )
            {
                var code = JsonNodeReader.TryReadInt32( error["code"], out var parsed ) ? parsed : -32000;
                var message = ( string? ) error["message"] ?? "Unknown agent error.";
                completion.TrySetException( new JsonRpcException( code, message ) );
            }
            else
            {
                completion.TrySetResult( frame["result"] );
            }
        }
    }

    private async ValueTask AdmitServerRequestAsync(
        JsonNode idNode,
        string method,
        JsonNode? parameters,
        CancellationToken token )
    {
        // Each request holds a slot until its reply is written. Refusal below keeps
        // running handlers within their limit, so the slots run out only when a
        // backlog of replies waits for the transport because the agent is not
        // reading them. Until one is written, the agent's next frames stay unread.
        await _unansweredServerRequests.WaitAsync( token ).ConfigureAwait( false );

        if ( Interlocked.Increment( ref _admittedServerRequests ) > _maximumServerRequests )
        {
            Interlocked.Decrement( ref _admittedServerRequests );

            // Refusing inline keeps a flooding peer waiting on its own refusals
            // instead of growing the set of running handlers.
            try
            {
                await SendReplyAsync(
                    ErrorReply( idNode, ServerErrorCode, "Too many concurrent requests." ),
                    token ).ConfigureAwait( false );
            }
            finally
            {
                _unansweredServerRequests.Release( );
            }

            return;
        }

        var handler = HandleServerRequestAsync( idNode, method, parameters, token );
        lock ( _serverRequestsLock )
        {
            _serverRequests.Add( handler );
        }

        _ = ForgetServerRequestAsync( handler );
    }

    private async Task ForgetServerRequestAsync( Task handler )
    {
        await handler.ConfigureAwait( ConfigureAwaitOptions.SuppressThrowing );

        lock ( _serverRequestsLock )
        {
            _serverRequests.Remove( handler );
        }
    }

    private async Task HandleServerRequestAsync(
        JsonNode idNode,
        string method,
        JsonNode? parameters,
        CancellationToken token )
    {
        try
        {
            JsonObject reply;
            try
            {
                reply = await InvokeRequestHandlerAsync( idNode, method, parameters, token ).ConfigureAwait( false );
            }
            finally
            {
                // Free the handler slot before replying, so a peer that answers the
                // reply with its next request is never refused for one that finished.
                Interlocked.Decrement( ref _admittedServerRequests );
            }

            await SendReplyAsync( reply, token ).ConfigureAwait( false );
        }
        finally
        {
            // The request stops counting once its reply is written or the write fails.
            _unansweredServerRequests.Release( );
        }
    }

    private async ValueTask<JsonObject> InvokeRequestHandlerAsync(
        JsonNode idNode,
        string method,
        JsonNode? parameters,
        CancellationToken token )
    {
        try
        {
            if ( RequestHandler is null )
            {
                throw new JsonRpcException( -32601, "The request is not supported." );
            }
            var result = await RequestHandler( method, parameters, token ).ConfigureAwait( false );
            return new JsonObject
            {
                ["id"] = idNode.DeepClone( ),
                ["result"] = result ?? new JsonObject( )
            };
        }
        catch ( OperationCanceledException )
        {
            return ErrorReply( idNode, ServerErrorCode, "The request was cancelled." );
        }
        catch ( JsonRpcException exception )
        {
            return ErrorReply( idNode, exception.Code, exception.Message );
        }
        catch ( Exception exception ) when ( exception is not OutOfMemoryException )
        {
            // Every request gets a reply, or the agent waits for it forever. The
            // failure's own text can name local paths, so it stays in the daemon.
            return ErrorReply( idNode, ServerErrorCode, "The request failed." );
        }
    }

    private static JsonObject ErrorReply( JsonNode idNode, int code, string message ) => new( )
    {
        ["id"] = idNode.DeepClone( ),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
    };

    private async ValueTask SendReplyAsync( JsonObject reply, CancellationToken token )
    {
        try
        {
            await SendFrameAsync( reply, token ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or ObjectDisposedException or OperationCanceledException )
        {
            // The connection is closing; the pending request will fail through the read loop.
        }
    }

    private static bool TryReadId( JsonNode node, out long id ) => JsonNodeReader.TryReadInt64( node, out id );

    private void FailPending( )
    {
        foreach ( (var key, var completion) in _pending )
        {
            _pending.TryRemove( key, out _ );
            completion.TrySetException( Fault ?? new OperationCanceledException( "The agent connection closed." ) );
        }
    }

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        await _lifetime.CancelAsync( ).ConfigureAwait( false );
        await _transport.DisposeAsync( ).ConfigureAwait( false );
        if ( _readLoop is not null )
        {
            try
            {
                await _readLoop.ConfigureAwait( false );
            }
            catch ( Exception )
            {
                // The loop reports faults through the Fault property.
            }
        }

        // The read loop has stopped admitting requests, and the lifetime token has
        // cancelled the handlers still running and the replies still unwritten.
        // Wait for them, so none runs on against the transport, the write gate,
        // the request slots, or its caller's disposed state.
        Task[] handlers;
        lock ( _serverRequestsLock )
        {
            handlers = [.. _serverRequests];
        }
        await Task.WhenAll( handlers ).ConfigureAwait( ConfigureAwaitOptions.SuppressThrowing );

        FailPending( );
        _lifetime.Dispose( );
        _writeGate.Dispose( );
        _unansweredServerRequests.Dispose( );
    }
}

public sealed class LineDelimitedTransport : IJsonRpcTransport
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly BoundedLineReader _reader;
    private readonly Func<ValueTask>? _onDispose;

    public LineDelimitedTransport( Stream input, Stream output, int maximumFrameBytes, Func<ValueTask>? onDispose = null )
    {
        _input = input;
        _output = output;
        _reader = new BoundedLineReader( input, maximumFrameBytes );
        _onDispose = onDispose;
    }

    public ValueTask<string?> ReceiveAsync( CancellationToken cancellationToken ) =>
        _reader.ReadLineAsync( cancellationToken );

    public async ValueTask SendAsync( string message, CancellationToken cancellationToken )
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes( message + "\n" );
        await _output.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
        await _output.FlushAsync( cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask DisposeAsync( )
    {
        try
        {
            await _output.DisposeAsync( ).ConfigureAwait( false );
        }
        catch ( IOException )
        {
        }
        await _input.DisposeAsync( ).ConfigureAwait( false );
        if ( _onDispose is not null )
        {
            await _onDispose( ).ConfigureAwait( false );
        }
    }
}
