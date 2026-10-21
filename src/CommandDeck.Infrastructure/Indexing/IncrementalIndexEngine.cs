namespace CommandDeck;

internal sealed partial class IncrementalIndexEngine : IAsyncDisposable
{
    private readonly IndexingOptions _options;
    private readonly IncrementalEventJournal _journal;
    private readonly IndexRootStateStore _rootStateStore;
    private IndexExclusionPolicy _exclusions;
    private readonly IndexPolicyStateStore _policyState;
    private long _ignoredEventCount;
    public bool PolicyReconciliationRequired { get; private set; }
    private readonly Func<IReadOnlyList<IncrementalPathRequest>, CancellationToken, ValueTask<bool>>
        _applyPaths;
    private readonly Func<IReadOnlyList<string>, CancellationToken, ValueTask<bool>> _reconcile;
    private readonly List<string> _unwatchedRoots = [];
    private readonly SemaphoreSlim _signal = new( 0, 1 );
    private readonly CancellationTokenSource _lifetime = new( );
    private readonly object _statusLock = new( );
    private readonly Dictionary<string, RootRuntimeState> _roots = new( StringComparer.Ordinal );
    private Task? _worker;
    private bool _started;
    private bool _watchersDisposed;
    private readonly object _watcherLock = new( );
    private bool _reconciling;
    private bool _reconciliationRequired;
    private bool _pausedUntilFullScan;
    private string? _reconciliationReason;
    private DateTimeOffset? _lastEventAt;
    private DateTimeOffset? _lastSuccessAt;
    private string? _lastError;
    private long _eventVersion;
    private int _maximumObservedBatchCount;
    private int _lastReconciledRootCount;

    public IncrementalIndexEngine(
        CommandDeckOptions options,
        IndexExclusionPolicy exclusions,
        Func<IReadOnlyList<IncrementalPathRequest>, CancellationToken, ValueTask<bool>> applyPaths,
        Func<IReadOnlyList<string>, CancellationToken, ValueTask<bool>> reconcile,
        IncrementalEventJournal? journal = null,
        IInotifySystem? inotify = null )
    {
        _options = options.Indexing;
        _journal = journal ?? new IncrementalEventJournal( options.Storage.DatabasePath );
        _inotify = inotify ?? LinuxInotifySystem.Instance;
        _rootStateStore = new IndexRootStateStore( options.Storage.DatabasePath );
        _exclusions = exclusions;
        _policyState = new IndexPolicyStateStore( options.Storage.DatabasePath );
        _applyPaths = applyPaths;
        _reconcile = reconcile;
        InitializeRootStates( );
    }

    public ValueTask StartAsync( CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        if ( _started )
        {
            return ValueTask.CompletedTask;
        }

        if ( !_journal.Initialize( ) )
        {
            _started = true;
            SetError( "Incremental indexing is owned by another CommandDeck runtime." );
            return ValueTask.CompletedTask;
        }
        PolicyReconciliationRequired = !_policyState.IsCurrent( _exclusions );
        if ( !_options.EnableIncrementalIndexing )
        {
            _started = true;
            PersistRootStates( );
            return ValueTask.CompletedTask;
        }

        StartRootWatchers( );

        _started = true;
        _worker = Task.Run( ( ) => RunAsync( _lifetime.Token ), CancellationToken.None );

        if ( _journal.IsCorrupted )
        {
            RequireReconciliation( "event-journal-corrupt" );
        }
        else if ( _journal.UnsyncedWindowLost )
        {
            RequireReconciliation( "event-journal-unsynced" );
        }
        else if ( PolicyReconciliationRequired )
        {
            RequireReconciliation( "index-policy-changed" );
        }
        else if ( !string.Equals(
            _journal.ExtractorVersion,
            ContentExtractor.ExtractorVersion,
            StringComparison.Ordinal ) )
        {
            RequireReconciliation( "extractor-version-changed" );
        }
        else if ( _journal.PendingCount > 0 )
        {
            Signal( );
        }

        RefreshPolicyWatchers( );
        PersistRootStates( );

        return ValueTask.CompletedTask;
    }

    private void StartRootWatchers( )
    {
        List<(string Path, IndexRootOptions Options)> roots = [];
        foreach ( var rootOptions in _options.Roots )
        {
            string root;
            try
            {
                root = IndexRootPaths.Normalize( rootOptions.Path );
            }
            catch ( ArgumentException )
            {
                _unwatchedRoots.Add( rootOptions.Path );
                MarkUnwatched( rootOptions.Path, "Configured root path is invalid." );
                continue;
            }

            if ( !Directory.Exists( root ) )
            {
                _unwatchedRoots.Add( root );
                MarkUnwatched( root, "Folder does not exist." );
                continue;
            }

            roots.Add( (root, rootOptions) );
        }

        if ( OperatingSystem.IsLinux( ) )
        {
            StartTreeWatchers( roots );
        }
        else
        {
            StartPortableWatchers( roots );
        }

    }

    // Called under the coordinator's catalog gate. Old queued events are checked
    // against the new file policy before they can publish any rows.
    public void ReplaceRoots( IndexExclusionPolicy exclusions )
    {
        PolicyReconciliationRequired = true;
        lock ( _watcherLock )
        {
            DisposeWatchers( );
            _exclusions = exclusions;
            lock ( _statusLock )
            {
                _roots.Clear( );
                _unwatchedRoots.Clear( );
                InitializeRootStates( );
            }
        }
        if ( _started && CanMutateCatalog )
        {
            PersistRootStates( );
        }
    }

    public long CaptureCheckpoint( ) => _journal.LastSequence;

    internal void ReportWatcherOverflow( string rootPath ) => RecordOverflow( rootPath );

    public bool CanMutateCatalog =>
        _journal.IsOwner;

    public bool IsPausedUntilFullScan
    {
        get
        {
            lock ( _statusLock )
            {
                return _pausedUntilFullScan;
            }
        }
    }

    public void CompleteFullScan( long checkpoint ) => CompleteScan( checkpoint );

    private void CompleteScan( long? checkpoint )
    {
        _policyState.Write( _exclusions );
        PolicyReconciliationRequired = false;
        RefreshPolicyWatchers( );
        if ( checkpoint is long sequence && _options.EnableIncrementalIndexing && _journal.IsOwner )
        {
            _journal.AdvanceCheckpoint( sequence, ContentExtractor.ExtractorVersion );
        }

        lock ( _statusLock )
        {
            _reconciling = false;
            _reconciliationRequired = false;
            _pausedUntilFullScan = false;
            _reconciliationReason = null;
            _lastSuccessAt = DateTimeOffset.UtcNow;
            _lastError = null;
        }

        if ( _journal.PendingCount > 0 )
        {
            Signal( );
        }
    }

    public void PauseUntilFullScan( string reason )
    {
        lock ( _statusLock )
        {
            _pausedUntilFullScan = true;
            _reconciling = false;
            _reconciliationRequired = true;
            _reconciliationReason = reason;
        }
    }

    public IncrementalIndexStatus GetStatus( )
    {
        var coverage = SnapshotWatchCoverage( );
        lock ( _statusLock )
        {
            foreach ( var root in _roots.Values )
            {
                coverage.TryGetValue( root.Path, out var rootCoverage );
                root.WatchedDirectoryCount = rootCoverage.Watched;
                root.UnwatchedDirectoryCount = rootCoverage.Unwatched;
            }

            var watchLimitReached = coverage.Values.Any( static value => value.Unwatched > 0 );
            return new IncrementalIndexStatus
            {
                Started = _started,
                Reconciling = _reconciling,
                ReconciliationRequired = _reconciliationRequired,
                ReconciliationReason = _reconciliationReason,
                WatchedRootCount = WatchingRootCount( ),
                UnwatchedRoots = [.. _unwatchedRoots],
                PendingEventCount = _journal.PendingCount,
                LastEventAt = _lastEventAt,
                LastSuccessAt = _lastSuccessAt,
                LastError = _lastError ?? (watchLimitReached ? WatchLimitMessage : null),
                ExtractorVersion = ContentExtractor.ExtractorVersion,
                MaximumObservedBatchCount = _maximumObservedBatchCount,
                LastReconciledRootCount = _lastReconciledRootCount,
                IgnoredEventCount = Interlocked.Read( ref _ignoredEventCount ),
                Roots = _roots.Values
                    .OrderBy( static root => root.Path, StringComparer.OrdinalIgnoreCase )
                    .Select( static root => root.Snapshot( ) )
                    .ToList( )
            };
        }
    }

    public void Pause( string? rootId )
    {
        lock ( _statusLock )
        {
            foreach ( var root in SelectRoots( rootId ) )
            {
                root.Paused = true;
            }

            PersistRootStatesUnderLock( );
        }
    }

    public string[] Resume( string? rootId )
    {
        lock ( _statusLock )
        {
            var selected = SelectRoots( rootId ).ToArray( );
            foreach ( var root in selected )
            {
                root.Paused = false;
            }

            PersistRootStatesUnderLock( );
            return selected.Select( static root => root.Path ).ToArray( );
        }
    }

    public void RecordRootScan( string path, long indexedItemCount )
    {
        lock ( _statusLock )
        {
            var root = FindRoot( path );
            if ( root is not null )
            {
                root.IndexedItemCount = Math.Max( 0, indexedItemCount );
                root.PendingEventCount = 0;
                root.LastSuccessAt = DateTimeOffset.UtcNow;
                root.LastError = null;
                PersistRootStatesUnderLock( );
            }
        }
    }

    internal (IReadOnlyList<IncrementalWorkItem> Items, bool Complete) GetWorkItemsSnapshot( )
    {
        bool reconciliationRequired;
        string? reconciliationReason;
        string? lastError;
        lock ( _statusLock )
        {
            reconciliationRequired = _reconciliationRequired;
            reconciliationReason = _reconciliationReason;
            lastError = _lastError;
        }

        var pending = _journal.ReadPending( 257 );
        var state = lastError is not null
            ? IndexWorkStates.Failed
            : reconciliationRequired
                ? IndexWorkStates.Stale
                : IndexWorkStates.Pending;
        var reason = lastError is not null
            ? "incremental-apply-failed"
            : reconciliationReason ?? "filesystem-event-pending";
        List<IncrementalWorkItem> items = [];
        foreach ( var entry in pending )
        {
            items.Add( new IncrementalWorkItem( entry.Path, state, reason ) );
            if ( entry.OldPath is not null )
            {
                items.Add( new IncrementalWorkItem( entry.OldPath, state, reason ) );
            }
        }

        if ( items.Count == 0 && reconciliationRequired )
        {
            foreach ( var root in _options.Roots )
            {
                items.Add( new IncrementalWorkItem(
                    PathExpansion.Expand( root.Path ),
                    IndexWorkStates.Stale,
                    reconciliationReason ?? "reconciliation-required" ) );
            }
        }

        var distinct = items.DistinctBy( static value => (value.Path, value.State) ).ToArray( );
        return (distinct.Take( 256 ).ToArray( ), pending.Count <= 256 && distinct.Length <= 256);
    }

    public async ValueTask DisposeAsync( )
    {
        lock ( _watcherLock )
        {
            _watchersDisposed = true;
            DisposeWatchers( );
        }
        DisposePolicyWatchers( );
        await _lifetime.CancelAsync( ).ConfigureAwait( false );
        Signal( );
        if ( _worker is not null )
        {
            try
            {
                await _worker.ConfigureAwait( false );
            }
            catch ( OperationCanceledException )
            {
                // Normal shutdown.
            }
        }

        _signal.Dispose( );
        _journal.Dispose( );
        _lifetime.Dispose( );
    }

    private void RequireReconciliation( string reason )
    {
        lock ( _statusLock )
        {
            _reconciliationRequired = true;
            if ( !_pausedUntilFullScan )
            {
                _reconciliationReason = reason;
            }
        }

        Signal( );
    }

    private void SetError( string message )
    {
        lock ( _statusLock )
        {
            _lastError = message;
            foreach ( var root in _roots.Values.Where( static root => !root.Watched ) )
            {
                root.LastError ??= message;
            }
            PersistRootStatesUnderLock( );
        }
    }
}

internal sealed record IncrementalPathRequest( string Path, bool RemoveDescendants );

internal sealed record IncrementalWorkItem( string Path, string State, string Reason );
