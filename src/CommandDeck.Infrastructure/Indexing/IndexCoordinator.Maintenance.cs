namespace CommandDeck;

public sealed partial class IndexCoordinator
{
    private readonly IndexMaintenanceStateStore _maintenanceStateStore;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _lifetime = new( );
    private readonly object _maintenanceStateLock = new( );
    private readonly object _startLock = new( );
    private DateTimeOffset? _maintenanceLastStartedAt;
    private DateTimeOffset? _maintenanceLastCompletedAt;
    private DateTimeOffset? _maintenanceNextRunAt;
    private string? _maintenanceLastError;
    private long _maintenanceRemovedStalePathCount;
    private int _maintenanceRunning;
    private Task? _maintenanceTask;
    private Task? _disabledPolicyTask;

    public async ValueTask<bool> RunMaintenanceIfDueAsync(
        CancellationToken cancellationToken )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );
        if ( !_options.Indexing.EnableScheduledMaintenance )
        {
            return false;
        }

        EnsureMaintenanceScheduled( );
        var now = _timeProvider.GetUtcNow( );
        lock ( _maintenanceStateLock )
        {
            if ( _maintenanceNextRunAt is null || now < _maintenanceNextRunAt )
            {
                return false;
            }
        }

        if ( Interlocked.CompareExchange( ref _maintenanceRunning, 1, 0 ) != 0 )
        {
            return false;
        }

        try
        {
            lock ( _maintenanceStateLock )
            {
                _maintenanceLastStartedAt = now;
                _maintenanceLastError = null;
                PersistMaintenanceStateUnderLock( lastRunSucceeded: false );
            }

            await StartAsync( cancellationToken ).ConfigureAwait( false );
            EnsureCatalogMutationOwnership( );
            await _indexLock.WaitAsync( cancellationToken ).ConfigureAwait( false );
            long removed;
            try
            {
                CancelFullScan( );
                removed = await RemoveStaleIndexedPathsAsync( cancellationToken )
                    .ConfigureAwait( false );
                var integrity = await _catalogOperations.CheckIntegrityAsync(
                    cancellationToken ).ConfigureAwait( false );
                if ( !integrity.Succeeded )
                {
                    throw new InvalidOperationException(
                        "Scheduled catalog integrity validation failed." );
                }

                var optimized = await _catalogOperations.OptimizeAsync(
                    cancellationToken ).ConfigureAwait( false );
                if ( !optimized.Succeeded )
                {
                    throw new InvalidOperationException(
                        "Scheduled catalog optimization failed." );
                }
            }
            finally
            {
                StartBackgroundReindex( );
                _indexLock.Release( );
            }

            var completed = _timeProvider.GetUtcNow( );
            lock ( _maintenanceStateLock )
            {
                _maintenanceLastCompletedAt = completed;
                _maintenanceNextRunAt = completed + MaintenanceInterval;
                _maintenanceLastError = null;
                _maintenanceRemovedStalePathCount = removed;
                PersistMaintenanceStateUnderLock( lastRunSucceeded: true );
            }

            return true;
        }
        catch ( Exception exception ) when ( exception is not OperationCanceledException )
        {
            lock ( _maintenanceStateLock )
            {
                _maintenanceLastError = exception.Message;
                _maintenanceNextRunAt = _timeProvider.GetUtcNow( ) + MaintenanceInterval;
                PersistMaintenanceStateUnderLock( lastRunSucceeded: false );
            }

            return true;
        }
        finally
        {
            Interlocked.Exchange( ref _maintenanceRunning, 0 );
        }
    }

    private TimeSpan MaintenanceInterval =>
        TimeSpan.FromMinutes( _options.Indexing.MaintenanceIntervalMinutes );

    private async Task RunMaintenanceLoopAsync( CancellationToken cancellationToken )
    {
        while ( !cancellationToken.IsCancellationRequested )
        {
            DateTimeOffset next;
            lock ( _maintenanceStateLock )
            {
                next = _maintenanceNextRunAt ?? _timeProvider.GetUtcNow( ) + MaintenanceInterval;
            }

            var delay = next - _timeProvider.GetUtcNow( );
            if ( delay > TimeSpan.Zero )
            {
                await Task.Delay( delay, _timeProvider, cancellationToken ).ConfigureAwait( false );
            }

            await RunMaintenanceIfDueAsync( cancellationToken ).ConfigureAwait( false );
        }
    }

    private async ValueTask<long> RemoveStaleIndexedPathsAsync(
        CancellationToken cancellationToken )
    {
        const int pathBatchSize = 256;
        long removed = 0;
        string? afterPath = null;
        while ( true )
        {
            var paths = await _catalog.GetIndexedPathsAfterAsync(
                afterPath,
                pathBatchSize,
                cancellationToken ).ConfigureAwait( false );
            if ( paths.Count == 0 )
            {
                return removed;
            }

            afterPath = paths[^1];
            var stale = paths
                .Where( static path => !File.Exists( path ) && !Directory.Exists( path ) )
                .Select( static path => new IndexedPathChange
                {
                    Path = path,
                    RemoveDescendants = true
                } )
                .ToList( );
            if ( stale.Count > 0 )
            {
                await _catalog.ApplyPathChangesAsync( stale, cancellationToken )
                    .ConfigureAwait( false );
                removed += stale.Count;
            }

            if ( paths.Count < pathBatchSize )
            {
                return removed;
            }
        }
    }

    private void EnsureMaintenanceScheduled( )
    {
        lock ( _maintenanceStateLock )
        {
            if ( _maintenanceNextRunAt is not null )
            {
                return;
            }

            _maintenanceNextRunAt = _timeProvider.GetUtcNow( ) + MaintenanceInterval;
            PersistMaintenanceStateUnderLock( lastRunSucceeded: false );
        }
    }

    private IncrementalIndexStatus WithMaintenanceStatus( IncrementalIndexStatus status ) =>
        new( )
        {
            Started = status.Started,
            Reconciling = status.Reconciling,
            ReconciliationRequired = status.ReconciliationRequired,
            ReconciliationReason = status.ReconciliationReason,
            WatchedRootCount = status.WatchedRootCount,
            UnwatchedRoots = status.UnwatchedRoots,
            PendingEventCount = status.PendingEventCount,
            LastEventAt = status.LastEventAt,
            LastSuccessAt = status.LastSuccessAt,
            LastError = status.LastError,
            ExtractorVersion = status.ExtractorVersion,
            Roots = status.Roots,
            IgnoredEventCount = status.IgnoredEventCount,
            MaximumObservedBatchCount = status.MaximumObservedBatchCount,
            LastReconciledRootCount = status.LastReconciledRootCount,
            Maintenance = GetMaintenanceStatus( )
        };

    private IndexMaintenanceStatus GetMaintenanceStatus( )
    {
        lock ( _maintenanceStateLock )
        {
            return new IndexMaintenanceStatus
            {
                Enabled = _options.Indexing.EnableScheduledMaintenance,
                Running = Volatile.Read( ref _maintenanceRunning ) != 0,
                LastStartedAt = _maintenanceLastStartedAt,
                LastCompletedAt = _maintenanceLastCompletedAt,
                NextRunAt = _maintenanceNextRunAt,
                LastError = _maintenanceLastError,
                RemovedStalePathCount = _maintenanceRemovedStalePathCount
            };
        }
    }

    private void RestoreMaintenanceState( PersistedIndexMaintenanceState state )
    {
        _maintenanceLastStartedAt = FromUnixMilliseconds( state.LastStartedUnixMilliseconds );
        _maintenanceLastCompletedAt = FromUnixMilliseconds( state.LastCompletedUnixMilliseconds );
        _maintenanceNextRunAt = FromUnixMilliseconds( state.NextRunUnixMilliseconds );
        _maintenanceLastError = state.LastError;
        _maintenanceRemovedStalePathCount = state.RemovedStalePathCount;
    }

    private void PersistMaintenanceStateUnderLock( bool lastRunSucceeded ) =>
        _maintenanceStateStore.Write( new PersistedIndexMaintenanceState
        {
            LastStartedUnixMilliseconds = _maintenanceLastStartedAt?.ToUnixTimeMilliseconds( ),
            LastCompletedUnixMilliseconds = _maintenanceLastCompletedAt?.ToUnixTimeMilliseconds( ),
            NextRunUnixMilliseconds = _maintenanceNextRunAt?.ToUnixTimeMilliseconds( ),
            LastError = _maintenanceLastError,
            RemovedStalePathCount = _maintenanceRemovedStalePathCount,
            LastRunSucceeded = lastRunSucceeded
        } );

    private static DateTimeOffset? FromUnixMilliseconds( long? value ) =>
        value is null ? null : DateTimeOffset.FromUnixTimeMilliseconds( value.Value );
}
