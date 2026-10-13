using System.Threading.Channels;

namespace CommandDeck;

public static class ClipboardHelperFraming
{
    public const int MaximumQueuedFrames = 8;

    public static Channel<ClipboardHelperFrame> CreateQueue( ) => Channel.CreateBounded<ClipboardHelperFrame>(
        new BoundedChannelOptions( MaximumQueuedFrames )
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        } );

    public static async ValueTask<ClipboardHelperFrame?> ReadAsync( Stream stream, CancellationToken cancellationToken,
        Action<ClipboardHelperHeader>? validateHeader = null )
    {
        var headerBytes = new byte[ClipboardHelperHeader.Size];
        var first = await stream.ReadAsync( headerBytes.AsMemory( 0, 1 ), cancellationToken ).ConfigureAwait( false );
        if ( first == 0 )
        {
            return null;
        }

        await stream.ReadExactlyAsync( headerBytes.AsMemory( 1 ), cancellationToken ).ConfigureAwait( false );
        var header = ClipboardHelperHeader.Decode( headerBytes );
        validateHeader?.Invoke( header );
        // Decode and validate all lengths before allocating the bounded payload.
        var payload = new byte[header.PayloadLength];
        await stream.ReadExactlyAsync( payload, cancellationToken ).ConfigureAwait( false );
        return new( header, payload );
    }

    // A transport owns one writer per pipe. Data and control use different pipes.
    public static async ValueTask WriteAsync( Stream stream, ClipboardHelperFrame frame, CancellationToken cancellationToken )
    {
        if ( frame.Payload.Length != frame.Header.PayloadLength )
        {
            throw new InvalidDataException( "Clipboard helper payload length does not match its header." );
        }

        var header = new byte[ClipboardHelperHeader.Size];
        frame.Header.Encode( header );
        await stream.WriteAsync( header, cancellationToken ).ConfigureAwait( false );
        await stream.WriteAsync( frame.Payload, cancellationToken ).ConfigureAwait( false );
        await stream.FlushAsync( cancellationToken ).ConfigureAwait( false );
    }
}
