using System.Security.Cryptography;

namespace CommandDeck;

internal sealed class ClipboardPayloadWriter(
    ClipboardPayloadLedger ledger, ClipboardPayloadReservation reservation, Guid id,
    ClipboardPlannedOffer offer, ClipboardPayloadLease lease ) : IDisposable
{
    private readonly object _gate = new( );
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
    private long _length;
    private bool _closed;
    private bool _completed;
    private bool _writing;
    private bool _released;

    internal async ValueTask WriteAsync( ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken )
    {
        lock ( _gate )
        {
            ObjectDisposedException.ThrowIf( _closed, this );
            if ( _writing )
            {
                throw new InvalidOperationException( "Clipboard offer writes must be sequential." );
            }

            if ( bytes.Length > ClipboardHelperHeader.MaximumPayload || _length + bytes.Length > offer.MaximumBytes )
            {
                Dispose( );
                throw new InvalidDataException( "Clipboard offer exceeded its admitted byte bound." );
            }

            _writing = true;
        }

        try
        {
            await lease.Stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
            lock ( _gate )
            {
                ObjectDisposedException.ThrowIf( _closed, this );
                _hash.AppendData( bytes.Span );
                _length += bytes.Length;
            }
        }
        catch
        {
            Dispose( );
            throw;
        }
        finally
        {
            lock ( _gate )
            {
                _writing = false;
                if ( _closed )
                {
                    ReleaseResources( );
                }
            }
        }
    }

    internal ClipboardCompletedPayload Complete( )
    {
        lock ( _gate )
        {
            ObjectDisposedException.ThrowIf( _closed, this );
            if ( _writing )
            {
                throw new InvalidOperationException( "Clipboard offer write is still running." );
            }

            try
            {
                lease.Stream.Flush( flushToDisk: true );
                lease.Dispose( );
                ClipboardCompletedPayload payload = new( id, offer, _length, Convert.ToHexStringLower( _hash.GetHashAndReset( ) ) );
                ledger.CompletePayload( reservation, payload );
                _completed = true;
                return payload;
            }
            finally
            {
                Dispose( );
            }
        }
    }

    public void Dispose( )
    {
        lock ( _gate )
        {
            if ( _closed )
            {
                return;
            }

            _closed = true;
            if ( !_writing )
            {
                ReleaseResources( );
            }

            if ( !_completed )
            {
                ledger.Abandon( reservation );
            }
        }
    }

    private void ReleaseResources( )
    {
        if ( _released )
        {
            return;
        }

        _released = true;
        try
        {
            lease.Dispose( );
        }
        finally
        {
            _hash.Dispose( );
        }
    }
}
