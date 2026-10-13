using System.Buffers.Binary;
using System.Text;

namespace CommandDeck;

public enum ClipboardCaptureStatus : uint
{
    Ready = 1, Configured, SelectionCancelled, SelectionRejected, Unavailable, Serving, ServingEnded, WriteRejected
}

public static class ClipboardCaptureProtocol
{
    public static ClipboardHelperFrame Status( ulong epoch, ClipboardCaptureStatus status, uint detail = 0, Guid selection = default )
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian( bytes, ( uint ) status );
        BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 4 ), detail );
        return new( new( ClipboardHelperFrameType.Status, epoch, selection, -1, 0, bytes.Length ), bytes );
    }

    public static (ClipboardCaptureStatus Status, uint Detail) ReadStatus( ClipboardHelperFrame frame )
    {
        Require( frame, ClipboardHelperFrameType.Status, 8 );
        var status = ( ClipboardCaptureStatus ) BinaryPrimitives.ReadUInt32LittleEndian( frame.Payload.Span );
        if ( !Enum.IsDefined( status ) )
        {
            throw new InvalidDataException( "Unknown clipboard capture status." );
        }

        return (status, BinaryPrimitives.ReadUInt32LittleEndian( frame.Payload.Span[4..] ));
    }

    public static ClipboardHelperFrame Configure( ulong epoch, ClipboardConsent consent, uint sequence = 0 )
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian( bytes, ( uint ) consent );
        return new( new( ClipboardHelperFrameType.Configure, epoch, Guid.Empty, -1, sequence, 4 ), bytes );
    }

    public static ClipboardConsent ReadConfiguration( ClipboardHelperFrame frame )
    {
        Require( frame, ClipboardHelperFrameType.Configure, 4 );
        var consent = ( ClipboardConsent ) BinaryPrimitives.ReadUInt32LittleEndian( frame.Payload.Span );
        if ( !Enum.IsDefined( consent ) )
        {
            throw new InvalidDataException( "Invalid clipboard capture consent." );
        }

        return consent;
    }

    public static ClipboardHelperFrame SelectionStart( ulong epoch, Guid selection, int count )
    {
        if ( count is < 0 or > 512 )
        {
            throw new InvalidDataException( "Clipboard inventory exceeds its count bound." );
        }

        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian( bytes, ( uint ) count );
        return new( new( ClipboardHelperFrameType.SelectionStart, epoch, selection, -1, 0, 4 ), bytes );
    }

    public static int ReadSelectionStart( ClipboardHelperFrame frame )
    {
        Require( frame, ClipboardHelperFrameType.SelectionStart, 4 );
        var count = BinaryPrimitives.ReadUInt32LittleEndian( frame.Payload.Span );
        if ( frame.Header.Sequence != 0 || count > 512 )
        {
            throw new InvalidDataException( "Clipboard inventory start is invalid." );
        }

        return ( int ) count;
    }

    public static ClipboardHelperFrame InventoryChunk( ulong epoch, Guid selection, uint sequence, IReadOnlyList<string> names, int first )
    {
        if ( names.Count > 512 || first < 0 || first >= names.Count )
        {
            throw new InvalidDataException( "Clipboard inventory chunk bounds are invalid." );
        }

        var count = Math.Min( 32, names.Count - first );
        var bytes = new byte[8322];
        BinaryPrimitives.WriteUInt16LittleEndian( bytes, ( ushort ) count );
        var offset = 2;
        for ( var index = first; index < first + count; index++ )
        {
            var name = names[index];
            if ( name.Length is < 1 or > 256 || name.Any( static character => character is < ' ' or > '~' ) )
            {
                throw new InvalidDataException( "Clipboard MIME declaration is invalid." );
            }

            BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset ), ( ushort ) index );
            BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset + 2 ), ( ushort ) name.Length );
            offset += 4;
            offset += Encoding.ASCII.GetBytes( name, bytes.AsSpan( offset ) );
        }

        return new( new( ClipboardHelperFrameType.InventoryChunk, epoch, selection, -1, sequence, offset ), bytes.AsMemory( 0, offset ) );
    }

    private static void Require( ClipboardHelperFrame frame, ClipboardHelperFrameType type, int length )
    {
        if ( frame.Header.Type != type || frame.Header.OfferIndex != -1 || frame.Header.PayloadLength != length || frame.Payload.Length != length )
        {
            throw new InvalidDataException( "Clipboard capture message shape is invalid." );
        }
    }
}
