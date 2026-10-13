using System.Buffers.Binary;
using System.Text;

namespace CommandDeck;

public sealed record ClipboardServeManifestEntry( string Mime, long Length );

/// <summary>Daemon-to-helper serving frames: one bounded manifest, ordered chunks, then a terminal end frame.</summary>
public static class ClipboardServeProtocol
{
    public const int MaximumOffers = 16;
    public const long MaximumOfferBytes = 16 * 1024 * 1024;
    public const long MaximumSelectionBytes = 24 * 1024 * 1024;

    public static ClipboardHelperFrame WriteStart( ulong epoch, Guid writeId, IReadOnlyList<ClipboardServeManifestEntry> manifest )
    {
        ArgumentNullException.ThrowIfNull( manifest );
        Validate( manifest );
        var bytes = new byte[4 + manifest.Sum( static entry => 10 + entry.Mime.Length )];
        BinaryPrimitives.WriteUInt32LittleEndian( bytes, ( uint ) manifest.Count );
        var offset = 4;
        foreach ( var entry in manifest )
        {
            BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset ), ( ushort ) entry.Mime.Length );
            offset += 2;
            offset += Encoding.ASCII.GetBytes( entry.Mime, bytes.AsSpan( offset ) );
            BinaryPrimitives.WriteUInt64LittleEndian( bytes.AsSpan( offset ), ( ulong ) entry.Length );
            offset += 8;
        }

        return new( new( ClipboardHelperFrameType.WriteStart, epoch, writeId, -1, 0, bytes.Length ), bytes );
    }

    public static IReadOnlyList<ClipboardServeManifestEntry> ReadWriteStart( ClipboardHelperFrame frame )
    {
        ArgumentNullException.ThrowIfNull( frame );
        var span = frame.Payload.Span;
        if ( frame.Header.Type != ClipboardHelperFrameType.WriteStart || frame.Header.OfferIndex != -1 ||
            frame.Header.Sequence != 0 || span.Length < 4 )
        {
            throw new InvalidDataException( "Clipboard serve manifest frame is invalid." );
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian( span );
        if ( count is < 1 or > MaximumOffers )
        {
            throw new InvalidDataException( "Clipboard serve manifest count is invalid." );
        }

        List<ClipboardServeManifestEntry> manifest = [];
        var offset = 4;
        for ( var index = 0; index < count; index++ )
        {
            if ( span.Length < offset + 2 )
            {
                throw new InvalidDataException( "Clipboard serve manifest is truncated." );
            }

            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian( span[offset..] );
            offset += 2;
            if ( nameLength is < 1 or > 256 || span.Length < offset + nameLength + 8 )
            {
                throw new InvalidDataException( "Clipboard serve manifest name is invalid." );
            }

            var name = Encoding.ASCII.GetString( span.Slice( offset, nameLength ) );
            offset += nameLength;
            var length = BinaryPrimitives.ReadUInt64LittleEndian( span[offset..] );
            offset += 8;
            if ( length > MaximumOfferBytes )
            {
                throw new InvalidDataException( "Clipboard serve manifest length is invalid." );
            }

            manifest.Add( new( name, ( long ) length ) );
        }

        if ( offset != span.Length )
        {
            throw new InvalidDataException( "Clipboard serve manifest has trailing bytes." );
        }

        Validate( manifest );
        return manifest;
    }

    public static ClipboardHelperFrame WriteChunk( ulong epoch, Guid writeId, int offerIndex, uint sequence, ReadOnlyMemory<byte> bytes ) =>
        new( new( ClipboardHelperFrameType.WriteChunk, epoch, writeId, offerIndex, sequence, bytes.Length ), bytes );

    public static ClipboardHelperFrame WriteEnd( ulong epoch, Guid writeId, uint sequence, int offerCount )
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian( bytes, ( uint ) offerCount );
        return new( new( ClipboardHelperFrameType.WriteEnd, epoch, writeId, -1, sequence, 4 ), bytes );
    }

    public static int ReadWriteEnd( ClipboardHelperFrame frame )
    {
        ArgumentNullException.ThrowIfNull( frame );
        if ( frame.Header.Type != ClipboardHelperFrameType.WriteEnd || frame.Header.OfferIndex != -1 || frame.Payload.Length != 4 )
        {
            throw new InvalidDataException( "Clipboard serve end frame is invalid." );
        }

        return ( int ) BinaryPrimitives.ReadUInt32LittleEndian( frame.Payload.Span );
    }

    private static void Validate( IReadOnlyList<ClipboardServeManifestEntry> manifest )
    {
        if ( manifest.Count is < 1 or > MaximumOffers || manifest.Sum( static entry => entry.Length ) > MaximumSelectionBytes ||
            manifest.Any( static entry => entry.Length is < 1 or > MaximumOfferBytes || entry.Mime.Length is < 1 or > 256 ||
                entry.Mime.Any( static character => character is < '!' or > '~' ) ) ||
            manifest.Select( static entry => entry.Mime ).Distinct( StringComparer.Ordinal ).Count( ) != manifest.Count ||
            manifest.Any( static entry => string.Equals( entry.Mime, ClipboardOfferPlan.OwnerMime, StringComparison.OrdinalIgnoreCase ) ) )
        {
            throw new InvalidDataException( "Clipboard serve manifest violates its bounds." );
        }
    }
}
