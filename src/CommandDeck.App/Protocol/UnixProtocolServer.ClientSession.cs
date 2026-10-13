using System.Collections.Concurrent;

namespace CommandDeck;

public sealed partial class UnixProtocolServer
{
    private readonly record struct ClientRequestContext(
        string OwnerId,
        long RequestId,
        CancellationToken CancellationToken );

    private enum RequestAdmissionStatus
    {
        Admitted,
        AtCapacity,
        Duplicate
    }

    private sealed class ClientSession : IAsyncDisposable
    {
        private readonly IClientLifecycle _lifecycle;
        private readonly CancellationToken _serverCancellationToken;
        private readonly ConcurrentDictionary<long, CancellationTokenSource> _activeRequests = new( );
        private readonly ClientTaskTracker _pendingRequests = new( );
        private readonly RevisionGate _aiBarrier = new( );
        private long _activeSearchRequestId;
        private int _disposed;

        public ClientSession( IClientLifecycle lifecycle, CancellationToken serverCancellationToken )
        {
            _lifecycle = lifecycle;
            _serverCancellationToken = serverCancellationToken;
            OwnerId = $"socket-{Guid.NewGuid( ):N}";
            _lifecycle.OpenClient( OwnerId );
        }

        public string OwnerId { get; }

        public RevisionGate AiBarrier => _aiBarrier;

        public ClientRequestContext ContextFor( long requestId, CancellationToken cancellationToken ) =>
            new( OwnerId, requestId, cancellationToken );

        public bool HasActiveRequest( long requestId ) => _activeRequests.ContainsKey( requestId );

        /// <summary>
        /// Admits a request under its route's admission: a slot and a cancellation
        /// owner, then the search replacement or AI epoch the admission asks for.
        /// </summary>
        public RequestAdmissionStatus Admit(
            ProtocolRequest request,
            ProtocolAdmission admission,
            out CancellationTokenSource? cancellation,
            out long aiEpoch )
        {
            cancellation = null;
            aiEpoch = 0;

            // ai.clear must reach its barrier even when AI work it clears holds every slot.
            if ( admission != ProtocolAdmission.AiClearBarrier &&
                 _activeRequests.Count >= ProtocolLimits.MaximumConcurrentRequestsPerClient )
            {
                return RequestAdmissionStatus.AtCapacity;
            }

            var source = CancellationTokenSource.CreateLinkedTokenSource( _serverCancellationToken );
            if ( !_activeRequests.TryAdd( request.RequestId, source ) )
            {
                source.Dispose( );
                return RequestAdmissionStatus.Duplicate;
            }

            cancellation = source;
            switch ( admission )
            {
                case ProtocolAdmission.ReplacesSearch:
                    ReplaceSearch( request.RequestId );
                    break;
                case ProtocolAdmission.AiOrdered:
                    aiEpoch = _aiBarrier.Revision;
                    break;
                case ProtocolAdmission.AiClearBarrier:
                    aiEpoch = _aiBarrier.Advance( );
                    break;
            }

            return RequestAdmissionStatus.Admitted;
        }

        public void CancelRequest( long requestId )
        {
            if ( _activeRequests.TryGetValue( requestId, out var source ) )
            {
                TryCancel( source );
            }
        }

        private void ReplaceSearch( long requestId )
        {
            var previous = Interlocked.Exchange( ref _activeSearchRequestId, requestId );
            if ( previous > 0 )
            {
                CancelRequest( previous );
            }
        }

        public void Finish( long requestId, ProtocolAdmission admission, CancellationTokenSource cancellation )
        {
            _activeRequests.TryRemove( requestId, out _ );
            if ( admission == ProtocolAdmission.ReplacesSearch )
            {
                Interlocked.CompareExchange( ref _activeSearchRequestId, 0, requestId );
            }

            cancellation.Dispose( );
        }

        public void Track( Task request ) => _pendingRequests.Track( request );

        public async ValueTask DisposeAsync( )
        {
            if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
            {
                return;
            }

            _lifecycle.RevokeClient( OwnerId );
            foreach ( var request in _activeRequests.Values )
            {
                TryCancel( request );
            }

            try
            {
                await _lifecycle.CloseClientSessionsAsync( OwnerId ).ConfigureAwait( false );
            }
            catch ( Exception exception )
            {
                AppLog.Debug( $"Extension client cleanup failed: {exception.GetType( ).Name}" );
            }
            finally
            {
                try
                {
                    await Task.WhenAll( _pendingRequests.Snapshot( ) ).ConfigureAwait( false );
                }
                finally
                {
                    _lifecycle.ReleaseClient( OwnerId );
                    _aiBarrier.Dispose( );
                }
            }
        }
    }
}
