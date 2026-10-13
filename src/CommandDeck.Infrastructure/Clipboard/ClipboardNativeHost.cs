using System.IO.Pipes;
using System.Threading.Channels;

namespace CommandDeck;

public static class ClipboardNativeHost
{
    public static async Task<int> RunAsync( string controlRead, string controlWrite, ulong epoch,
        CancellationToken cancellationToken )
    {
        using AnonymousPipeClientStream control = new( PipeDirection.In, controlRead );
        using AnonymousPipeClientStream acknowledgement = new( PipeDirection.Out, controlWrite );
        using ClipboardPipeStream input = new( 0, FileAccess.Read );
        using ClipboardPipeStream output = new( 1, FileAccess.Write );
        using ClipboardPipeStream controlInput = new( control.SafePipeHandle.DangerousGetHandle( ).ToInt32( ), FileAccess.Read );
        using ClipboardPipeStream acknowledgementOutput = new( acknowledgement.SafePipeHandle.DangerousGetHandle( ).ToInt32( ), FileAccess.Write );
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );

        // The Wayland thread sleeps in poll; queue changes and cancellation wake it.
        using WaylandWakeSignal wake = new( );
        using var wakeOnCancellation = lifetime.Token.Register( wake.Signal );
        var commands = ClipboardHelperFraming.CreateQueue( );
        var controls = ClipboardHelperFraming.CreateQueue( );
        var events = ClipboardHelperFraming.CreateQueue( );
        var acknowledgements = ClipboardHelperFraming.CreateQueue( );
        var native = Task.Factory.StartNew( ( ) =>
        {
            using ClipboardWaylandCapture host = new( epoch, commands.Reader, controls.Reader, events, acknowledgements.Writer, wake );
            host.Run( null, lifetime.Token );
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default );
        Task[] tasks = [native, ReadAsync( input, commands.Writer ), ReadAsync( controlInput, controls.Writer ),
            WriteAsync( output, events.Reader, wake ), WriteAsync( acknowledgementOutput, acknowledgements.Reader, freed: null )];
        try
        {
            var completed = await Task.WhenAny( tasks ).ConfigureAwait( false );
            await completed.ConfigureAwait( false );
            return 0;
        }
        finally
        {
            await lifetime.CancelAsync( ).ConfigureAwait( false );
            try
            {
                await Task.WhenAll( tasks ).ConfigureAwait( false );
            }
            catch ( Exception exception ) when ( exception is IOException or OperationCanceledException or ObjectDisposedException ) { }
        }

        async Task ReadAsync( Stream stream, ChannelWriter<ClipboardHelperFrame> writer )
        {
            while ( await writer.WaitToWriteAsync( lifetime.Token ).ConfigureAwait( false ) )
            {
                var frame = await ClipboardHelperFraming.ReadAsync( stream, lifetime.Token ).ConfigureAwait( false );
                if ( frame is null )
                {
                    return;
                }

                await writer.WriteAsync( frame, lifetime.Token ).ConfigureAwait( false );
                wake.Signal( );
            }
        }

        // Signals the freed queue slot before the slower pipe write, so the Wayland thread can refill it.
        async Task WriteAsync( Stream stream, ChannelReader<ClipboardHelperFrame> reader, WaylandWakeSignal? freed )
        {
            await foreach ( var frame in reader.ReadAllAsync( lifetime.Token ).ConfigureAwait( false ) )
            {
                freed?.Signal( );
                await ClipboardHelperFraming.WriteAsync( stream, frame, lifetime.Token ).ConfigureAwait( false );
            }
        }
    }
}
