namespace CommandDeck;

// Listings live in a short in-memory cache, and each is read by at most one
// hey process at a time: identical searches in flight wait for the same read.
// Only successful listings are kept: a sign-in, a reconnect, or a slow answer
// must be retried on the next search, not replayed. A listing is kept with the
// client's change generation from before it was read, so a triage change
// retires every listing read before it, and a read started before a change is
// not shared after it.
public sealed partial class HeyProvider
{
    private readonly Lock _cacheGate = new( );
    private readonly LinkedList<(string Key, DateTimeOffset Expires, long Generation, HeyListing Listing)> _cache = new( );
    private readonly Dictionary<string, PendingRead> _pending = new( StringComparer.Ordinal );

    // One hey process and the searches waiting for it. It is stopped only when
    // every waiter has given up, and its fields are guarded by _cacheGate.
    private sealed class PendingRead( long generation )
    {
        public long Generation { get; } = generation;

        public TaskCompletionSource<HeyListing> Listing { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public CancellationTokenSource Stop { get; } = new( );

        public int Waiters
        {
            get; set;
        }

        public bool Finished
        {
            get; set;
        }
    }

    private async ValueTask<HeyListing> CachedAsync(
        string key,
        TimeSpan lifetime,
        bool pause,
        Func<CancellationToken, ValueTask<HeyListing>> read,
        CancellationToken cancellationToken )
    {
        var cached = TryGetCached( key );
        if ( cached is not null )
        {
            return cached;
        }

        // A newer keystroke cancels this search before any process starts; a
        // read already in flight for the same words starts none either.
        if ( pause && _typingPause > TimeSpan.Zero && !IsPending( key ) )
        {
            await Task.Delay( _typingPause, cancellationToken ).ConfigureAwait( false );
        }

        var (listing, pending, started) = Join( key );
        if ( listing is not null )
        {
            return listing;
        }

        if ( started )
        {
            _ = ReadAsync( key, lifetime, pending!, read );
        }

        try
        {
            return await pending!.Listing.Task.WaitAsync( cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            Leave( key, pending! );
        }
    }

    private bool IsPending( string key )
    {
        lock ( _cacheGate )
        {
            return _pending.TryGetValue( key, out var pending ) && pending.Generation == _client.ChangeGeneration;
        }
    }

    // A listing cached meanwhile, a read in flight to wait for, or a new read
    // this caller starts.
    private (HeyListing? Listing, PendingRead? Pending, bool Started) Join( string key )
    {
        var now = _time.GetUtcNow( );
        lock ( _cacheGate )
        {
            var generation = _client.ChangeGeneration;
            var cached = TryGetCachedLocked( key, now, generation );
            if ( cached is not null )
            {
                return (cached, null, false);
            }

            if ( _pending.TryGetValue( key, out var pending ) && pending.Generation == generation )
            {
                pending.Waiters++;
                return (null, pending, false);
            }

            pending = new PendingRead( generation )
            {
                Waiters = 1
            };
            _pending[key] = pending;
            return (null, pending, true);
        }
    }

    private async Task ReadAsync( string key, TimeSpan lifetime, PendingRead pending, Func<CancellationToken, ValueTask<HeyListing>> read )
    {
        try
        {
            var listing = await read( pending.Stop.Token ).ConfigureAwait( false );
            if ( listing.Failure is null )
            {
                Store( key, listing, lifetime, pending.Generation );
            }

            pending.Listing.TrySetResult( listing );
        }
        catch ( OperationCanceledException exception )
        {
            pending.Listing.TrySetCanceled( exception.CancellationToken );
        }
        catch ( Exception exception )
        {
            // Every waiter sees what went wrong; nobody waits forever.
            pending.Listing.TrySetException( exception );
        }
        finally
        {
            lock ( _cacheGate )
            {
                pending.Finished = true;
                Forget( key, pending );
            }
        }
    }

    private void Leave( string key, PendingRead pending )
    {
        lock ( _cacheGate )
        {
            pending.Waiters--;
            if ( pending.Waiters > 0 || pending.Finished )
            {
                return;
            }

            Forget( key, pending );
        }

        // Outside the lock: stopping hey runs its cancellation callbacks here.
        pending.Stop.Cancel( );
    }

    private void Forget( string key, PendingRead pending )
    {
        if ( _pending.TryGetValue( key, out var current ) && current == pending )
        {
            _pending.Remove( key );
        }
    }

    private HeyListing? TryGetCached( string key )
    {
        var now = _time.GetUtcNow( );
        lock ( _cacheGate )
        {
            return TryGetCachedLocked( key, now, _client.ChangeGeneration );
        }
    }

    private HeyListing? TryGetCachedLocked( string key, DateTimeOffset now, long generation )
    {
        for ( var node = _cache.First; node is not null; node = node.Next )
        {
            if ( node.Value.Key != key )
            {
                continue;
            }

            _cache.Remove( node );
            if ( now > node.Value.Expires || node.Value.Generation != generation )
            {
                return null;
            }

            _cache.AddFirst( node );
            return node.Value.Listing;
        }

        return null;
    }

    private void Store( string key, HeyListing listing, TimeSpan lifetime, long generation )
    {
        var expires = _time.GetUtcNow( ) + lifetime;
        lock ( _cacheGate )
        {
            // Read before a later change: it could only replace a newer listing.
            if ( generation != _client.ChangeGeneration )
            {
                return;
            }

            for ( var node = _cache.First; node is not null; node = node.Next )
            {
                if ( node.Value.Key == key )
                {
                    _cache.Remove( node );
                    break;
                }
            }

            _cache.AddFirst( (key, expires, generation, listing) );
            while ( _cache.Count > CacheEntries )
            {
                _cache.RemoveLast( );
            }
        }
    }
}
