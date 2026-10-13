namespace CommandDeck;

// Open With is optional enrichment for the file rows a search shows. A search
// waits a bounded time for it; when detection takes longer, the search cancels it
// and returns the rows with their own actions, so a slow `file` process never
// holds up an application match. Detection runs on its own worker, so a stat that
// blocks cannot hold the search either. Work that is slow to stop after
// cancellation keeps one of two slots until it ends; a search that finds both
// taken skips Open With. Runtime shutdown joins that work before the catalog closes.
internal sealed class OpenWithDetection : IAsyncDisposable
{
    // Detection for the rows a search shows took 10-44 ms cold on a 100k-file
    // catalog and 44-62 ms for 50 paths on a loaded desktop. The wait leaves room
    // for that and caps what a stalled `file` adds to a search.
    internal static readonly TimeSpan DefaultWait = TimeSpan.FromMilliseconds( 150 );

    private const int MaximumHandlersPerFile = 5;
    private const int MaximumOutstandingDetections = 2;

    private readonly IFileHandlerCatalog _catalog;
    private readonly TimeSpan _wait;
    private readonly SemaphoreSlim _slots = new( MaximumOutstandingDetections, MaximumOutstandingDetections );
    private readonly object _gate = new( );
    private readonly HashSet<Task> _active = [];
    private bool _stopping;
    private Task? _drain;

    public OpenWithDetection( IFileHandlerCatalog catalog, TimeSpan wait )
    {
        ArgumentNullException.ThrowIfNull( catalog );
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual( wait, TimeSpan.Zero );

        _catalog = catalog;
        _wait = wait;
    }

    // The Open With actions for each path, or null when detection did not finish
    // within the wait or no slot was free.
    public async ValueTask<IReadOnlyDictionary<string, IReadOnlyList<CommandAction>>?> DetectAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken )
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        var detection = Start( paths, cancellation.Token );
        if ( detection is null )
        {
            cancellation.Dispose( );
            return null;
        }

        await (( Task ) detection).WaitAsync( _wait, cancellationToken )
            .ConfigureAwait( ConfigureAwaitOptions.SuppressThrowing );
        if ( detection.IsCompleted )
        {
            cancellation.Dispose( );
            cancellationToken.ThrowIfCancellationRequested( );
            try
            {
                return await detection.ConfigureAwait( false );
            }
            catch ( Exception ) when ( !cancellationToken.IsCancellationRequested )
            {
                return null;
            }
        }

        // A stat on a mount that stopped answering, or a `file` process blocked in
        // I/O, can outlast cancellation, so the search does not wait for it to unwind.
        cancellation.Cancel( );
        _ = detection.ContinueWith(
            _ => cancellation.Dispose( ),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default );
        cancellationToken.ThrowIfCancellationRequested( );
        return null;
    }

    public ValueTask DisposeAsync( )
    {
        lock ( _gate )
        {
            _stopping = true;
            _drain ??= DrainAsync( [.. _active] );
            return new ValueTask( _drain );
        }
    }

    private Task<IReadOnlyDictionary<string, IReadOnlyList<CommandAction>>>? Start(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken )
    {
        lock ( _gate )
        {
            if ( _stopping || !_slots.Wait( 0, CancellationToken.None ) )
            {
                return null;
            }

            var detection = Task.Run(
                async ( ) =>
                {
                    try
                    {
                        return await _catalog.GetOpenWithActionsAsync( paths, MaximumHandlersPerFile, cancellationToken )
                            .ConfigureAwait( false );
                    }
                    finally
                    {
                        _slots.Release( );
                    }
                },
                CancellationToken.None );

            Track( detection );
            return detection;
        }
    }

    private void Track( Task detection )
    {
        _active.Add( detection );
        _ = detection.ContinueWith(
            completed =>
            {
                // A detection the search stopped waiting for has no caller left to
                // report its failure to.
                _ = completed.Exception;
                lock ( _gate )
                {
                    _active.Remove( completed );
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default );
    }

    private async Task DrainAsync( Task[] detections )
    {
        await Task.WhenAll( detections ).ConfigureAwait( ConfigureAwaitOptions.SuppressThrowing );
        _slots.Dispose( );
    }
}
