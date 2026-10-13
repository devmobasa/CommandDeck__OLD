using System.Buffers.Binary;
using System.Text;

namespace CommandDeck;

public sealed class ClipboardHelperInventory( ulong hostEpoch, Guid selectionId, int advertisedCount )
{
    private readonly List<string> _names = [];
    private uint _sequence = 1;
    public bool Complete { get; private set; }
    public IReadOnlyList<string> Names => _names.AsReadOnly( );

    public void Accept( ClipboardHelperFrame frame )
    {
        if ( advertisedCount is < 0 or > 512 || Complete || frame.Header.HostEpoch != hostEpoch ||
            frame.Header.SelectionId != selectionId || frame.Header.OfferIndex != -1 || frame.Header.Sequence != _sequence ||
            frame.Payload.Length != frame.Header.PayloadLength )
        {
            throw new InvalidDataException( "Clipboard inventory identity, sequence or bounds are invalid." );
        }

        if ( frame.Header.Type == ClipboardHelperFrameType.InventoryEnd )
        {
            if ( frame.Payload.Length != 0 || _names.Count != advertisedCount )
            {
                throw new InvalidDataException( "Clipboard inventory ended with an incorrect MIME count." );
            }

            Complete = true;
            return;
        }

        if ( frame.Header.Type != ClipboardHelperFrameType.InventoryChunk || frame.Payload.Length is < 2 or > 8322 )
        {
            throw new InvalidDataException( "Expected a bounded clipboard inventory chunk." );
        }

        ReadChunk( frame.Payload.Span );
        _sequence++;
    }

    private void ReadChunk( ReadOnlySpan<byte> payload )
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian( payload );
        if ( count is < 1 or > 32 || _names.Count + count > advertisedCount )
        {
            throw new InvalidDataException( "Clipboard inventory chunk exceeds its MIME count." );
        }

        var offset = 2;
        List<string> pending = [];
        for ( var index = 0; index < count; index++ )
        {
            if ( payload.Length - offset < 4 )
            {
                throw new InvalidDataException( "Truncated clipboard MIME declaration." );
            }

            var offerIndex = BinaryPrimitives.ReadUInt16LittleEndian( payload[offset..] );
            var length = BinaryPrimitives.ReadUInt16LittleEndian( payload[(offset + 2)..] );
            offset += 4;
            if ( offerIndex != _names.Count + index || length is < 1 or > 256 || payload.Length - offset < length )
            {
                throw new InvalidDataException( "Clipboard MIME index or length is invalid." );
            }

            var name = payload.Slice( offset, length );
            if ( name.ContainsAnyExceptInRange( ( byte ) 0x20, ( byte ) 0x7e ) )
            {
                throw new InvalidDataException( "Clipboard MIME names must contain printable ASCII." );
            }

            pending.Add( Encoding.ASCII.GetString( name ) );
            offset += length;
        }

        if ( offset != payload.Length )
        {
            throw new InvalidDataException( "Clipboard inventory chunk contains trailing bytes." );
        }

        _names.AddRange( pending );
    }
}
