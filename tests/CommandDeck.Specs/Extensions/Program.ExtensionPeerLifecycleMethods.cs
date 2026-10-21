using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionPeerRequiresExplicitStartAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        await using ExtensionPeer peer = new( pipes.Host, pipes.Host, true, [], ( _, _ ) => throw new InvalidOperationException( ) );
        using CancellationTokenSource deadline = new( TimeSpan.FromMilliseconds( 300 ) );
        await ThrowsAsync<InvalidOperationException>( async ( ) => await peer.CallAsync( "initialize", null, deadline.Token ) );
        Throws<InvalidOperationException>( ( ) => peer.Notify( "initialized" ) );
    }

    private static async Task ExtensionPeerCallsAfterDisposalReportCancellationAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        ExtensionPeer peer = new( pipes.Host, pipes.Host, true, [], ( _, _ ) => throw new InvalidOperationException( ) );
        peer.Start( );

        // Revocation disposes the peer before a handler's linked token observes the asynchronous cancellation.
        await peer.DisposeAsync( );

        await ThrowsAsync<OperationCanceledException>( async ( ) => await peer.CallAsync( "command.data", null, CancellationToken.None ) );
        peer.Notify( "initialized" );
    }
}

public static partial class Program
{
    private static async Task ExtensionPeerDisposalBoundsUncooperativeReadsAsync( )
    {
        using UncooperativeExtensionStream input = new( );
        using MemoryStream output = new( );
        await using ExtensionPeer peer = new( input, output, true, [], ( _, _ ) => throw new InvalidOperationException( ) );
        peer.Start( );
        peer.Start( );
        await input.Entered.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
        try
        {
            await peer.DisposeAsync( ).AsTask( ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( 1, input.Reads );
            True( input.Disposed );
            await peer.DisposeAsync( );
            Throws<ObjectDisposedException>( peer.Start );
        }
        finally { input.Release.TrySetResult( ); }
    }

    private sealed class UncooperativeExtensionStream : Stream
    {
        public TaskCompletionSource Entered { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource Release { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public int Reads { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException( );
        public override long Position { get => throw new NotSupportedException( ); set => throw new NotSupportedException( ); }
        public override async ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
        {
            Reads++;
            Entered.TrySetResult( );
            await Release.Task;
            return 0;
        }
        public override void Flush( ) => throw new NotSupportedException( );
        public override int Read( byte[] buffer, int offset, int count ) => throw new NotSupportedException( );
        public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( );
        public override void SetLength( long value ) => throw new NotSupportedException( );
        public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException( );
        protected override void Dispose( bool disposing ) { Disposed = true; base.Dispose( disposing ); }
    }
}
