namespace CommandDeck;

/// <summary>Shared nonqueued capacity for transient clipboard HTML and image work.</summary>
internal static class ClipboardProjectionWorker
{
    private static readonly SemaphoreSlim Worker = new( 1, 1 );

    internal static IDisposable? TryAcquire( CancellationToken token )
    {
        token.ThrowIfCancellationRequested( );
        return Worker.Wait( 0, token ) ? new Lease( ) : null;
    }

    private sealed class Lease : IDisposable
    {
        private int _released;

        public void Dispose( )
        {
            if ( Interlocked.Exchange( ref _released, 1 ) == 0 )
            {
                Worker.Release( );
            }
        }
    }
}
