namespace CommandDeck;

internal sealed class ClipboardCaptureAdmission(
    ClipboardPayloadReservation reservation, ClipboardOfferPlan plan, ulong hostEpoch, Guid selection, uint firstSequence,
    Func<bool> captureCurrent, Func<ClipboardPayloadReservation, CancellationToken, ValueTask> publish ) : IDisposable
{
    private readonly ClipboardHelperReceiveState _state = new( hostEpoch, selection,
        plan.Offers.Select( static offer => new KeyValuePair<int, long>( offer.InventoryIndex, offer.MaximumBytes ) ).ToArray( ), firstSequence );
    private ClipboardPayloadWriter? _writer;
    private bool _closed;
    internal bool Complete => _state.Complete;
    internal bool ContentRejected { get; private set; }

    internal void BeginOffer( int inventoryIndex )
    {
        ObjectDisposedException.ThrowIf( _closed, this );
        if ( _writer is not null || !captureCurrent( ) )
        {
            throw new InvalidOperationException( "Clipboard capture admission changed." );
        }

        _writer = reservation.Owner.OpenWriter( reservation, inventoryIndex );
    }

    internal async ValueTask AcceptAsync( ClipboardHelperFrame frame, CancellationToken token )
    {
        ObjectDisposedException.ThrowIf( _closed, this );
        token.ThrowIfCancellationRequested( );
        if ( !captureCurrent( ) )
        {
            throw new InvalidOperationException( "Clipboard capture authority changed." );
        }

        _state.Accept( frame );
        if ( frame.Header.Type == ClipboardHelperFrameType.SelectionEnd )
        {
            try
            {
                await publish( reservation, token ).ConfigureAwait( false );
            }
            catch ( InvalidDataException )
            {
                // Content validation rejected this selection only; the helper transport remains valid.
                ContentRejected = true;
                throw;
            }

            Dispose( );
            return;
        }

        if ( _writer is null )
        {
            throw new InvalidOperationException( "Clipboard payload was received without an admitted writer." );
        }

        if ( frame.Header.Type == ClipboardHelperFrameType.OfferChunk )
        {
            await _writer.WriteAsync( frame.Payload, token ).ConfigureAwait( false );
        }
        else
        {
            _writer.Complete( );
            _writer = null;
        }
    }

    public void Dispose( )
    {
        if ( _closed )
        {
            return;
        }

        _closed = true;
        _writer?.Dispose( );
        reservation.Dispose( );
        reservation.Owner.Cleanup( );
    }
}
