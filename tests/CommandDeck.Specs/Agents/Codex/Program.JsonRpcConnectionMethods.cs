using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly TimeSpan JsonRpcReplyDeadline = TimeSpan.FromSeconds( 5 );

    // Long enough for a connection that does not wait to read 40 queued frames;
    // a connection that waits never reads them, so this only limits detection.
    private static readonly TimeSpan UnreadReplySettle = TimeSpan.FromMilliseconds( 250 );

    private static async Task JsonRpcConnectionAnswersUnexpectedHandlerFailuresAsync( )
    {
        Channel<string> inbound = Channel.CreateUnbounded<string>( );
        Channel<string> outbound = Channel.CreateUnbounded<string>( );
        await using JsonRpcConnection connection = new( new InMemoryTransport( inbound.Reader, outbound.Writer ) );
        Queue<Func<ValueTask<JsonNode?>>> failures = new(
        [
            ( ) => throw new ArgumentException( "/home/user/private path" ),
            static async ( ) =>
            {
                await Task.Yield( );
                throw new NotSupportedException( "/home/user/private detail" );
            },
            static async ( ) =>
            {
                await Task.Yield( );
                throw new SqliteException( "disk I/O error at /home/user/.local/state", 10 );
            }
        ] );
        connection.RequestHandler = ( _, _, _ ) => failures.Dequeue( )( );
        connection.Start( );

        for ( var id = 1; id <= 3; id++ )
        {
            SendServerRequest( inbound.Writer, id );
            var reply = await ReadPeerFrameAsync( outbound.Reader );

            Equal( id, reply["id"]?.GetValue<int>( ) ?? 0 );
            Equal( -32000, reply["error"]?["code"]?.GetValue<int>( ) ?? 0 );
            Equal( "The request failed.", ( string? ) reply["error"]?["message"] );
        }

        True( !connection.IsFaulted, "an unexpected handler failure faulted the connection" );
    }

    private static async Task JsonRpcConnectionBoundsConcurrentServerRequestsAsync( )
    {
        Channel<string> inbound = Channel.CreateUnbounded<string>( );
        Channel<string> outbound = Channel.CreateUnbounded<string>( );
        TaskCompletionSource release = new( TaskCreationOptions.RunContinuationsAsynchronously );
        await using JsonRpcConnection connection = new(
            new InMemoryTransport( inbound.Reader, outbound.Writer ),
            maximumServerRequests: 2 );
        connection.RequestHandler = async ( _, _, token ) =>
        {
            await release.Task.WaitAsync( token );
            return new JsonObject { ["handled"] = true };
        };
        connection.Start( );

        SendServerRequest( inbound.Writer, 1 );
        SendServerRequest( inbound.Writer, 2 );
        SendServerRequest( inbound.Writer, 3 );
        var rejected = await ReadPeerFrameAsync( outbound.Reader );
        Equal( 3, rejected["id"]?.GetValue<int>( ) ?? 0 );
        Equal( -32000, rejected["error"]?["code"]?.GetValue<int>( ) ?? 0 );

        release.SetResult( );
        HashSet<int> handled = [];
        for ( var index = 0; index < 2; index++ )
        {
            var reply = await ReadPeerFrameAsync( outbound.Reader );
            True( reply["result"]?["handled"]?.GetValue<bool>( ) == true, "an admitted request was not handled" );
            handled.Add( reply["id"]?.GetValue<int>( ) ?? 0 );
        }

        True( handled.SetEquals( [1, 2] ), "the admitted requests were not the first two" );
        SendServerRequest( inbound.Writer, 4 );
        var admitted = await ReadPeerFrameAsync( outbound.Reader );
        Equal( 4, admitted["id"]?.GetValue<int>( ) ?? 0 );
        True( admitted["result"]?["handled"]?.GetValue<bool>( ) == true, "a finished request did not free its slot" );
    }

    private static async Task JsonRpcConnectionStopsReadingWhileRepliesAreUnsentAsync( )
    {
        const int requests = 40;
        UnreadReplyPeer peer = new( );
        await using var connection = peer.Connect( );

        peer.SendRequests( requests );
        await peer.Bounded.WaitAsync( JsonRpcReplyDeadline );

        // A connection that kept admitting requests reads on to the marker at once.
        await peer.ReadEverything.WaitAsync( UnreadReplySettle ).ConfigureAwait( ConfigureAwaitOptions.SuppressThrowing );
        True( !peer.ReadEverything.IsCompleted, "the connection read every request while no reply could be sent" );
        True( peer.Executed <= UnreadReplyPeer.Bound, "more requests ran than the limit and the reply backlog allow" );

        peer.ReadReplies( );
        HashSet<int> answered = [];
        for ( var index = 0; index < requests; index++ )
        {
            var reply = await ReadPeerFrameAsync( peer.Outbound );
            True( reply["result"]?["handled"]?.GetValue<bool>( ) == true, "a request that waited for the backlog was not handled" );
            answered.Add( reply["id"]?.GetValue<int>( ) ?? 0 );
        }

        Equal( requests, answered.Count );
        await peer.ReadEverything.WaitAsync( JsonRpcReplyDeadline );
    }

    private static async Task JsonRpcConnectionDisposalEndsAWaitForUnsentRepliesAsync( )
    {
        UnreadReplyPeer peer = new( );
        var connection = peer.Connect( );
        peer.SendRequests( 40 );
        await peer.Bounded.WaitAsync( JsonRpcReplyDeadline );

        await connection.DisposeAsync( ).AsTask( ).WaitAsync( JsonRpcReplyDeadline );

        True( peer.ReplyCancelled.IsCompleted, "disposal did not cancel the reply the agent never read" );
        True( connection.Completion.IsCompleted, "the read loop did not stop" );
        True( !connection.IsFaulted, "disposal faulted the connection" );
    }

    private static async Task JsonRpcConnectionDisposeWaitsForInFlightHandlersAsync( )
    {
        Channel<string> inbound = Channel.CreateUnbounded<string>( );
        Channel<string> outbound = Channel.CreateUnbounded<string>( );
        TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        var observedCancellation = false;
        var finished = false;
        JsonRpcConnection connection = new( new InMemoryTransport( inbound.Reader, outbound.Writer ) );
        connection.RequestHandler = async ( _, _, token ) =>
        {
            entered.TrySetResult( );
            try
            {
                await Task.Delay( Timeout.InfiniteTimeSpan, token );
            }
            catch ( OperationCanceledException )
            {
                observedCancellation = true;
                await Task.Delay( 100, CancellationToken.None );
                finished = true;
                throw;
            }

            return null;
        };
        connection.Start( );

        SendServerRequest( inbound.Writer, 1 );
        await entered.Task.WaitAsync( JsonRpcReplyDeadline );
        await connection.DisposeAsync( ).AsTask( ).WaitAsync( JsonRpcReplyDeadline );

        True( observedCancellation, "disposal did not cancel the in-flight handler" );
        True( finished, "DisposeAsync returned while a handler was still running" );
    }

    private static void SendServerRequest( ChannelWriter<string> inbound, int id ) =>
        inbound.TryWrite( new JsonObject
        {
            ["id"] = id,
            ["method"] = "item/tool/call",
            ["params"] = new JsonObject( )
        }.ToJsonString( ) );

    private static async ValueTask<JsonObject> ReadPeerFrameAsync( ChannelReader<string> outbound )
    {
        using CancellationTokenSource deadline = new( JsonRpcReplyDeadline );
        string frame;
        try
        {
            frame = await outbound.ReadAsync( deadline.Token );
        }
        catch ( OperationCanceledException ) when ( deadline.IsCancellationRequested )
        {
            throw new InvalidOperationException(
                $"The agent got no reply within {JsonRpcReplyDeadline.TotalSeconds:0} s." );
        }

        var reply = JsonNode.Parse( frame ) as JsonObject;
        if ( reply is null )
        {
            throw new InvalidOperationException( "The connection sent a non-object frame." );
        }

        return reply;
    }

    /// <summary>
    /// An agent that floods the connection with requests but reads no reply until
    /// told to. Its handler answers at once, so every admitted request that has
    /// not been answered is a reply waiting to be written.
    /// </summary>
    private sealed class UnreadReplyPeer : IJsonRpcTransport
    {
        public const int MaximumServerRequests = 2;
        public const int MaximumUnsentReplies = 3;
        public const int Bound = MaximumServerRequests + MaximumUnsentReplies;

        private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>( );
        private readonly Channel<string> _outbound = Channel.CreateUnbounded<string>( );
        private readonly TaskCompletionSource _reading = new( TaskCreationOptions.RunContinuationsAsynchronously );
        private readonly TaskCompletionSource _replyCancelled = new( TaskCreationOptions.RunContinuationsAsynchronously );
        private readonly TaskCompletionSource _bounded = new( TaskCreationOptions.RunContinuationsAsynchronously );
        private readonly TaskCompletionSource _readEverything = new( TaskCreationOptions.RunContinuationsAsynchronously );
        private int _executed;

        public ChannelReader<string> Outbound => _outbound.Reader;

        public Task Bounded => _bounded.Task;

        public Task ReadEverything => _readEverything.Task;

        public Task ReplyCancelled => _replyCancelled.Task;

        public int Executed => Volatile.Read( ref _executed );

        public JsonRpcConnection Connect( )
        {
            JsonRpcConnection connection = new(
                this,
                maximumServerRequests: MaximumServerRequests,
                maximumUnsentReplies: MaximumUnsentReplies );
            connection.RequestHandler = ( _, _, _ ) =>
            {
                if ( Interlocked.Increment( ref _executed ) == Bound )
                {
                    _bounded.TrySetResult( );
                }

                return ValueTask.FromResult<JsonNode?>( new JsonObject { ["handled"] = true } );
            };
            connection.NotificationHandler = ( _, _, _ ) =>
            {
                _readEverything.TrySetResult( );
                return ValueTask.CompletedTask;
            };

            connection.Start( );
            return connection;
        }

        public void SendRequests( int count )
        {
            for ( var id = 1; id <= count; id++ )
            {
                SendServerRequest( _inbound.Writer, id );
            }

            // The marker follows the last request: reading it means every request was read.
            _inbound.Writer.TryWrite( new JsonObject { ["method"] = "peer/marker" }.ToJsonString( ) );
        }

        public void ReadReplies( ) => _reading.TrySetResult( );

        public async ValueTask<string?> ReceiveAsync( CancellationToken cancellationToken ) =>
            await _inbound.Reader.ReadAsync( cancellationToken );

        public async ValueTask SendAsync( string message, CancellationToken cancellationToken )
        {
            try
            {
                await _reading.Task.WaitAsync( cancellationToken );
            }
            catch ( OperationCanceledException )
            {
                _replyCancelled.TrySetResult( );
                throw;
            }

            _outbound.Writer.TryWrite( message );
        }

        public ValueTask DisposeAsync( )
        {
            _outbound.Writer.TryComplete( );
            return ValueTask.CompletedTask;
        }
    }
}
