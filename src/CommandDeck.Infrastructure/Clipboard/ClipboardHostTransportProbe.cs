using System.IO.Pipes;

namespace CommandDeck;

/// <summary>Synthetic transport exercise. Does not connect to Wayland or inspect any clipboard.</summary>
public static class ClipboardHostTransportProbe
{
    public static async Task<int> RunAsync( string controlRead, string controlWrite, ulong epoch, bool acknowledge,
        CancellationToken cancellationToken )
    {
        using AnonymousPipeClientStream control = new( PipeDirection.In, controlRead );
        using AnonymousPipeClientStream acknowledgements = new( PipeDirection.Out, controlWrite );
        using var output = Console.OpenStandardOutput( );
        using var floodCancellation = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        var flood = Task.Run( async ( ) =>
        {
            await ClipboardHelperFraming.WriteAsync( output,
                new( new( ClipboardHelperFrameType.Status, epoch, Guid.Empty, -1, 0, 0 ), ReadOnlyMemory<byte>.Empty ),
                floodCancellation.Token ).ConfigureAwait( false );
            var bytes = new byte[ClipboardHelperHeader.MaximumPayload];
            var selection = Guid.NewGuid( );
            for ( uint index = 0; ; index++ )
            {
                await ClipboardHelperFraming.WriteAsync( output,
                    new( new( ClipboardHelperFrameType.OfferChunk, epoch, selection, 0, index, bytes.Length ), bytes ),
                    floodCancellation.Token ).ConfigureAwait( false );
            }
        }, CancellationToken.None );

        try
        {
            while ( await ClipboardHelperFraming.ReadAsync( control, cancellationToken ).ConfigureAwait( false ) is { } frame )
            {
                if ( frame.Header.HostEpoch != epoch || frame.Header.Type != ClipboardHelperFrameType.Cancel || frame.Payload.Length != 0 )
                {
                    return 2;
                }

                if ( acknowledge )
                {
                    await ClipboardHelperFraming.WriteAsync( acknowledgements,
                        new( new( ClipboardHelperFrameType.Ack, epoch, frame.Header.SelectionId, -1, 0, 0 ), ReadOnlyMemory<byte>.Empty ),
                        cancellationToken ).ConfigureAwait( false );
                }
            }

            return 0;
        }
        finally
        {
            await floodCancellation.CancelAsync( ).ConfigureAwait( false );
            output.Dispose( );
            try
            {
                await flood.ConfigureAwait( false );
            }
            catch ( Exception exception ) when ( exception is OperationCanceledException or IOException or ObjectDisposedException ) { }
        }
    }
}
