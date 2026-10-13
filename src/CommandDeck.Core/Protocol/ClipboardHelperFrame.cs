using System.Buffers.Binary;

namespace CommandDeck;

public enum ClipboardHelperFrameType : ushort
{
    SelectionStart = 1, InventoryChunk, InventoryEnd, ReceiveOffer, OfferChunk, OfferEnd,
    SelectionEnd, WriteStart, WriteChunk, WriteEnd, Cancel, Status, Ack, Configure, Shutdown
}

public readonly record struct ClipboardHelperHeader(
    ClipboardHelperFrameType Type, ulong HostEpoch, Guid SelectionId, int OfferIndex, uint Sequence, int PayloadLength )
{
    public const int Size = 64;
    public const int MaximumPayload = 60 * 1024;
    public const int MaximumFrame = 65536;
    public const ushort Version = 1;
    private const uint Magic = 0x42434443; // ASCII CDCB, little-endian.

    public static ClipboardHelperHeader Decode( ReadOnlySpan<byte> bytes )
    {
        if ( bytes.Length != Size || BinaryPrimitives.ReadUInt32LittleEndian( bytes ) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian( bytes[4..] ) != Version ||
            BinaryPrimitives.ReadUInt32LittleEndian( bytes[12..] ) != 0 || bytes[52..].ContainsAnyExcept( ( byte ) 0 ) )
        {
            throw new InvalidDataException( "Invalid clipboard helper frame header." );
        }

        var encodedLength = BinaryPrimitives.ReadUInt32LittleEndian( bytes[8..] );
        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian( bytes[48..] );
        if ( encodedLength > MaximumFrame || payloadLength > MaximumPayload || encodedLength != payloadLength + Size )
        {
            throw new InvalidDataException( "Clipboard helper frame exceeds its encoded bounds." );
        }

        ClipboardHelperHeader header = new( ( ClipboardHelperFrameType ) BinaryPrimitives.ReadUInt16LittleEndian( bytes[6..] ),
            BinaryPrimitives.ReadUInt64LittleEndian( bytes[16..] ), new Guid( bytes.Slice( 24, 16 ), bigEndian: true ),
            BinaryPrimitives.ReadInt32LittleEndian( bytes[40..] ), BinaryPrimitives.ReadUInt32LittleEndian( bytes[44..] ),
            ( int ) payloadLength );
        header.Validate( );
        return header;
    }

    public void Encode( Span<byte> bytes )
    {
        Validate( );
        if ( bytes.Length != Size )
        {
            throw new ArgumentException( "Clipboard helper header buffer must be exactly 64 bytes.", nameof( bytes ) );
        }

        bytes.Clear( );
        BinaryPrimitives.WriteUInt32LittleEndian( bytes, Magic );
        BinaryPrimitives.WriteUInt16LittleEndian( bytes[4..], Version );
        BinaryPrimitives.WriteUInt16LittleEndian( bytes[6..], ( ushort ) Type );
        BinaryPrimitives.WriteUInt32LittleEndian( bytes[8..], ( uint ) (Size + PayloadLength) );
        BinaryPrimitives.WriteUInt64LittleEndian( bytes[16..], HostEpoch );
        SelectionId.TryWriteBytes( bytes.Slice( 24, 16 ), bigEndian: true, out _ );
        BinaryPrimitives.WriteInt32LittleEndian( bytes[40..], OfferIndex );
        BinaryPrimitives.WriteUInt32LittleEndian( bytes[44..], Sequence );
        BinaryPrimitives.WriteInt32LittleEndian( bytes[48..], PayloadLength );
    }

    private void Validate( )
    {
        if ( !Enum.IsDefined( Type ) || HostEpoch == 0 || OfferIndex is < -1 or > 511 || PayloadLength is < 0 or > MaximumPayload )
        {
            throw new InvalidDataException( "Invalid clipboard helper frame fields." );
        }

        if ( Type is not (ClipboardHelperFrameType.Configure or ClipboardHelperFrameType.Status or ClipboardHelperFrameType.Shutdown) && SelectionId == Guid.Empty )
        {
            throw new InvalidDataException( "Clipboard helper frame requires a selection or write identity." );
        }
    }
}

public sealed record ClipboardHelperFrame( ClipboardHelperHeader Header, ReadOnlyMemory<byte> Payload );
