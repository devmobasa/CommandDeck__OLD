using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardHelperHeaderIsBoundedAndStableAsync( )
    {
        var fixture = Convert.FromHexString( File.ReadAllText( Path.Combine( FindRepositoryRoot( ), "tests/fixtures/clipboard-helper/header-v1.hex" ) ).Trim( ) );
        ClipboardHelperHeader header = new( ClipboardHelperFrameType.OfferChunk, 0x0102030405060708,
            Guid.Parse( "11223344-5566-7788-99aa-bbccddeeff00" ), 2, 7, 3 );
        var encoded = new byte[64];
        header.Encode( encoded );
        True( fixture.SequenceEqual( encoded ), "header wire layout matches the frozen golden fixture" );
        Equal( header, ClipboardHelperHeader.Decode( fixture ) );
        foreach ( var offset in new[] { 0, 4, 6, 12, 52, 63 } )
        {
            var corrupt = fixture.ToArray( );
            corrupt[offset] = 255;
            Throws<InvalidDataException>( ( ) => ClipboardHelperHeader.Decode( corrupt ) );
        }

        var huge = fixture.ToArray( );
        BinaryPrimitives.WriteUInt32LittleEndian( huge.AsSpan( 8 ), uint.MaxValue );
        BinaryPrimitives.WriteUInt32LittleEndian( huge.AsSpan( 48 ), uint.MaxValue - 64 );
        using MemoryStream stream = new( huge.Concat( new byte[] { 1, 2, 3 } ).ToArray( ) );
        await ThrowsAsync<InvalidDataException>( async ( ) => await ClipboardHelperFraming.ReadAsync( stream, default ) );
        Equal( 64L, stream.Position );
        using MemoryStream truncated = new( fixture[..63] );
        await ThrowsAsync<EndOfStreamException>( async ( ) => await ClipboardHelperFraming.ReadAsync( truncated, default ) );
        using MemoryStream empty = new( );
        True( await ClipboardHelperFraming.ReadAsync( empty, default ) is null, "clean frame-boundary EOF is explicit" );
    }

    private static void ClipboardHelperInventoryRequiresCompleteSequentialNames( )
    {
        var selection = Guid.NewGuid( );
        ClipboardHelperInventory inventory = new( 1, selection, 512 );
        for ( var group = 0; group < 16; group++ )
        {
            var frame = InventoryFrame( selection, group * 32, 32, ( uint ) (group + 1) );
            Equal( 8322, frame.Payload.Length );
            inventory.Accept( frame );
            True( !inventory.Complete, "chunks alone cannot authorize payload receive" );
        }

        inventory.Accept( new( new( ClipboardHelperFrameType.InventoryEnd, 1, selection, -1, 17, 0 ), ReadOnlyMemory<byte>.Empty ) );
        True( inventory.Complete, "announced inventory is complete only after end" );
        Equal( 512, inventory.Names.Count );
        Equal( 256, Encoding.ASCII.GetByteCount( inventory.Names[^1] ) );
        Throws<InvalidDataException>( ( ) => inventory.Accept( InventoryFrame( selection, 0, 1, 1 ) ) );

        ClipboardHelperInventory incomplete = new( 1, selection, 2 );
        var first = InventoryFrame( selection, 0, 1, 1 );
        incomplete.Accept( first );
        Throws<InvalidDataException>( ( ) => incomplete.Accept( first ) );
        Throws<InvalidDataException>( ( ) => incomplete.Accept( InventoryFrame( Guid.NewGuid( ), 1, 1, 2 ) ) );
        Throws<InvalidDataException>( ( ) => incomplete.Accept( new( new( ClipboardHelperFrameType.InventoryEnd, 1, selection, -1, 2, 0 ), ReadOnlyMemory<byte>.Empty ) ) );
        Throws<InvalidDataException>( ( ) => incomplete.Accept( new( new( ClipboardHelperFrameType.OfferChunk, 1, selection, 0, 2, 1 ), new byte[1] ) ) );
        incomplete.Accept( InventoryFrame( selection, 1, 1, 2 ) );
        incomplete.Accept( new( new( ClipboardHelperFrameType.InventoryEnd, 1, selection, -1, 3, 0 ), ReadOnlyMemory<byte>.Empty ) );
        True( incomplete.Complete, "rejected frames do not partially append MIME declarations" );
    }

    private static ClipboardHelperFrame InventoryFrame( Guid selection, int firstIndex, int count, uint sequence )
    {
        using MemoryStream output = new( );
        using BinaryWriter writer = new( output, Encoding.ASCII, leaveOpen: true );
        writer.Write( ( ushort ) count );
        for ( var index = firstIndex; index < firstIndex + count; index++ )
        {
            var name = ("mime-" + index).PadRight( 256, 'x' );
            writer.Write( ( ushort ) index );
            writer.Write( ( ushort ) name.Length );
            writer.Write( Encoding.ASCII.GetBytes( name ) );
        }

        var payload = output.ToArray( );
        return new( new( ClipboardHelperFrameType.InventoryChunk, 1, selection, -1, sequence, payload.Length ), payload );
    }

    private static async Task ClipboardHelperStreamsWithoutWholeSelectionBuffersAsync( )
    {
        var selection = Guid.NewGuid( );
        const int total = 24 * 1024 * 1024;
        ClipboardHelperReceiveState state = new( 1, selection, [new( 7, total )], 0 );
        var chunk = new byte[ClipboardHelperHeader.MaximumPayload];
        Random.Shared.NextBytes( chunk );
        using var sentDigest = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
        using var readDigest = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
        using MemoryStream wire = new( ClipboardHelperHeader.MaximumFrame );
        var sent = 0;
        uint sequence = 0;
        while ( sent < total )
        {
            var size = Math.Min( chunk.Length, total - sent );
            ClipboardHelperFrame frame = new( new( ClipboardHelperFrameType.OfferChunk, 1, selection, 7, sequence++, size ), chunk.AsMemory( 0, size ) );
            wire.SetLength( 0 );
            wire.Position = 0;
            await ClipboardHelperFraming.WriteAsync( wire, frame, default );
            True( wire.Length <= ClipboardHelperHeader.MaximumFrame, "only one bounded frame is buffered by the fixture" );
            wire.Position = 0;
            var read = await ClipboardHelperFraming.ReadAsync( wire, default, state.ValidateHeader );
            True( read is not null, "framed payload is available" );
            state.Accept( read! );
            sentDigest.AppendData( chunk.AsSpan( 0, size ) );
            readDigest.AppendData( read!.Payload.Span );
            sent += size;
        }

        var end = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian( end, total );
        state.Accept( new( new( ClipboardHelperFrameType.OfferEnd, 1, selection, 7, sequence++, 8 ), end ) );
        var selectionEnd = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian( selectionEnd, 1 );
        state.Accept( new( new( ClipboardHelperFrameType.SelectionEnd, 1, selection, -1, sequence, 4 ), selectionEnd ) );
        True( state.Complete, "counts and all offers finish the transport-only selection" );
        Equal( ( long ) total, state.ReceivedBytes );
        True( sentDigest.GetHashAndReset( ).SequenceEqual( readDigest.GetHashAndReset( ) ), "streaming preserves all raw bytes" );

        ClipboardHelperReceiveState small = new( 1, selection, [new( 7, 32 * 1024 )], 0 );
        wire.SetLength( 0 );
        wire.Position = 0;
        await ClipboardHelperFraming.WriteAsync( wire, new( new( ClipboardHelperFrameType.OfferChunk, 1, selection, 7, 0, chunk.Length ), chunk ), default );
        wire.Position = 0;
        await ThrowsAsync<InvalidDataException>( async ( ) => await ClipboardHelperFraming.ReadAsync( wire, default, small.ValidateHeader ) );
        Equal( 64L, wire.Position );
        Equal( 0L, small.ReceivedBytes );
        Throws<InvalidDataException>( ( ) => small.Accept( new( new( ClipboardHelperFrameType.OfferChunk, 2, selection, 7, 0, 1 ), new byte[1] ) ) );
        Throws<InvalidDataException>( ( ) => small.Accept( new( new( ClipboardHelperFrameType.OfferChunk, 1, selection, 8, 0, 1 ), new byte[1] ) ) );
        Throws<InvalidDataException>( ( ) => small.Accept( new( new( ClipboardHelperFrameType.OfferChunk, 1, selection, 7, 1, 1 ), new byte[1] ) ) );
    }

    private static async Task ClipboardHelperQueuesBackpressureIndependentlyAsync( )
    {
        var data = ClipboardHelperFraming.CreateQueue( );
        var control = ClipboardHelperFraming.CreateQueue( );
        ClipboardHelperFrame frame = new( new( ClipboardHelperFrameType.OfferChunk, 1, Guid.NewGuid( ), 0, 0, 1 ), new byte[1] );
        for ( var index = 0; index < 8; index++ )
        {
            True( data.Writer.TryWrite( frame ), "bounded data queue accepts its capacity" );
        }

        True( !data.Writer.TryWrite( frame ), "ninth frame cannot bypass backpressure" );
        using CancellationTokenSource cancelled = new( );
        var pending = data.Writer.WriteAsync( frame, cancelled.Token );
        True( !pending.IsCompleted, "full data queue pauses its writer" );
        True( control.Writer.TryWrite( new( frame.Header with
        {
            Type = ClipboardHelperFrameType.Cancel,
            OfferIndex = -1,
            PayloadLength = 0
        }, ReadOnlyMemory<byte>.Empty ) ),
            "independent control queue remains writable under data pressure" );
        cancelled.Cancel( );
        await ThrowsAsync<OperationCanceledException>( async ( ) => await pending );
    }
}
