namespace CommandDeck;

internal sealed partial class IncrementalIndexEngine
{
    private readonly object _policyWatcherLock = new( );
    private readonly Dictionary<string, FileSystemWatcher> _policyWatchers = new( StringComparer.Ordinal );
    private bool _policyWatchersDisposed;

    public void SavePolicyState( )
    {
        if ( !PolicyReconciliationRequired )
        {
            _policyState.Write( _exclusions );
        }
        RefreshPolicyWatchers( );
    }

    private void RefreshPolicyWatchers( )
    {
        if ( !_options.EnableIncrementalIndexing || !_journal.IsOwner )
        {
            return;
        }
        lock ( _policyWatcherLock )
        {
            if ( _policyWatchersDisposed )
            {
                return;
            }
            var directories = _exclusions.ExternalControlDirectories( ).ToHashSet( StringComparer.Ordinal );
            foreach ( var existing in _policyWatchers.Keys.Where( path => !directories.Contains( path ) ).ToArray( ) )
            {
                _policyWatchers[existing].Dispose( );
                _policyWatchers.Remove( existing );
            }
            foreach ( var directory in directories )
            {
                if ( _policyWatchers.ContainsKey( directory ) )
                {
                    continue;
                }
                try
                {
                    FileSystemWatcher watcher = new( directory )
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                            NotifyFilters.LastWrite | NotifyFilters.Size
                    };
                    watcher.Created += OnUpsert;
                    watcher.Changed += OnUpsert;
                    watcher.Deleted += OnDelete;
                    watcher.Renamed += OnRename;
                    watcher.Error += ( _, _ ) => RequireReconciliation( "policy-watcher-overflow" );
                    watcher.EnableRaisingEvents = true;
                    _policyWatchers.Add( directory, watcher );
                }
                catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or ArgumentException )
                {
                    SetError( $"Could not watch repository ignore rules: {exception.Message}" );
                }
            }
        }
    }

    private void DisposePolicyWatchers( )
    {
        lock ( _policyWatcherLock )
        {
            _policyWatchersDisposed = true;
            foreach ( var watcher in _policyWatchers.Values )
            {
                watcher.Dispose( );
            }
            _policyWatchers.Clear( );
        }
    }

    private async ValueTask ReconcilePendingAsync( CancellationToken cancellationToken )
    {
        string? reason;
        lock ( _statusLock )
        {
            _reconciling = true;
            reason = _reconciliationReason;
        }
        // Capture every affected root and the sequence before scanning. Events
        // arriving during reconciliation must survive that scan's checkpoint.
        var snapshot = _journal.ReadPending( );
        var checkpoint = snapshot.Count > 0 ? snapshot[^1].Sequence : _journal.LastSequence;
        var affectedRoots = reason is "event-storm" or "watcher-overflow"
            ? snapshot.SelectMany( static entry => entry.OldPath is null ? new[] { entry.Path } : [entry.Path, entry.OldPath] )
                .Select( path => FindRoot( path )?.Path ).OfType<string>( )
                .Distinct( StringComparer.Ordinal ).ToArray( )
            : [];
        try
        {
            var recoveryRoots = snapshot.Where( static entry => entry.Kind == IncrementalEventKind.Overflow )
                .Select( entry => FindRoot( entry.Path )?.Path ).OfType<string>( )
                .ToHashSet( StringComparer.Ordinal );
            // Lost directory events can leave both missing and stale watches.
            // Replace affected instances before scanning; retained roots keep
            // buffering events while their shared reader restarts.
            if ( recoveryRoots.Count > 0 )
            {
                var targetsChanged = RestoreWatchers( rootId: null, recoveryRoots, cancellationToken );
                if ( targetsChanged )
                {
                    affectedRoots = [];
                }
                else if ( affectedRoots.Length > 0 )
                {
                    affectedRoots = affectedRoots.Concat( recoveryRoots ).Distinct( StringComparer.Ordinal ).ToArray( );
                }
            }

            _lastReconciledRootCount = affectedRoots.Length;
            var reconciled = await _reconcile( affectedRoots, cancellationToken ).ConfigureAwait( false );
            if ( !reconciled )
            {
                lock ( _statusLock )
                {
                    _reconciling = false;
                }
                ScheduleRetry( cancellationToken );
                return;
            }
            SavePolicyState( );
            _journal.AdvanceCheckpoint( checkpoint, ContentExtractor.ExtractorVersion );
            lock ( _statusLock )
            {
                _reconciling = false;
                if ( _pausedUntilFullScan )
                {
                    return;
                }
                _reconciliationRequired = false;
                _reconciliationReason = null;
                _lastSuccessAt = DateTimeOffset.UtcNow;
                _lastError = null;
            }
            if ( _journal.PendingCount > 0 )
            {
                Signal( );
            }
        }
        catch ( Exception exception ) when ( exception is not OperationCanceledException )
        {
            SetError( exception.Message );
            lock ( _statusLock )
            {
                _reconciling = false;
            }
            ScheduleRetry( cancellationToken );
        }
    }
}
