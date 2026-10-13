namespace CommandDeck;

/// <summary>
/// A one-holder gate over a revision number. A caller captures the revision,
/// later enters the gate, and is turned away if the revision moved on in the
/// meantime; a rejected caller never holds the gate. The lease releases the gate
/// once, however often it is disposed.
/// </summary>
internal sealed class RevisionGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new( 1, 1 );
    private long _revision;

    public long Revision => Volatile.Read( ref _revision );

    /// <summary>Moves to the next revision; entries that captured an older one fail.</summary>
    public long Advance( ) => Interlocked.Increment( ref _revision );

    /// <summary>
    /// Waits for the gate, then checks <paramref name="expectedRevision"/> against
    /// the current revision; null admits any revision.
    /// </summary>
    public async ValueTask<IDisposable> EnterAsync(
        long? expectedRevision,
        Func<Exception> stale,
        CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );

        if ( expectedRevision is { } expected && expected != Revision )
        {
            _gate.Release( );
            throw stale( );
        }

        return new Lease( _gate );
    }

    public void Dispose( ) => _gate.Dispose( );

    private sealed class Lease( SemaphoreSlim gate ) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose( ) => Interlocked.Exchange( ref _gate, null )?.Release( );
    }
}
