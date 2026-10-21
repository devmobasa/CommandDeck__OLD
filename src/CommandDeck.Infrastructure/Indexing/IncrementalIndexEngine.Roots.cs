
namespace CommandDeck;

internal sealed partial class IncrementalIndexEngine
{

    private void InitializeRootStates( )
    {
        var persisted = _rootStateStore.Read( );
        foreach ( var configured in _options.Roots )
        {
            string path;
            try
            {
                path = IndexRootPaths.Normalize( configured.Path );
            }
            catch ( ArgumentException )
            {
                path = configured.Path;
            }

            var id = StableId.Create( "index-root", path );
            persisted.TryGetValue( id, out var previous );
            _roots[id] = new RootRuntimeState( id, path )
            {
                Paused = previous?.Paused == true,
                PendingEventCount = previous?.PendingEventCount ?? 0,
                IndexedItemCount = previous?.IndexedItemCount ?? 0,
                LastSuccessAt = previous?.LastSuccessUnixMilliseconds is long unix
                    ? DateTimeOffset.FromUnixTimeMilliseconds( unix )
                    : null,
                LastError = previous?.LastError
            };
        }
    }

#pragma warning disable CA1859 // Returns dictionary values or a single-root collection; neither concrete type covers both.
    private IEnumerable<RootRuntimeState> SelectRoots( string? rootId )
#pragma warning restore CA1859
    {
        if ( rootId is null )
        {
            return _roots.Values;
        }

        return _roots.TryGetValue( rootId, out var root )
            ? [root]
            : throw new InvalidOperationException( $"Unknown index root '{rootId}'." );
    }

    private RootRuntimeState? FindRoot( string path )
    {
        lock ( _statusLock )
        {
            return _roots.Values.Where( root => IsWithin( path, root.Path ) )
                .OrderByDescending( static root => root.Path.Length ).FirstOrDefault( );
        }
    }

    private static bool IsWithin( string path, string root ) => IndexExclusionPolicy.IsWithin( path, root );

    // A scoped retry replaces only its roots and overlapping trees. Other
    // descriptors remain open, buffering events while the reader restarts.
    // A reconciliation set is expanded to include every interrupted tree so
    // the current scan covers their restart gaps without another overflow.
    internal bool RestoreWatchers( string? rootId, HashSet<string>? reconciliationRoots = null, CancellationToken cancellationToken = default )
    {
        if ( !_started || !_options.EnableIncrementalIndexing || !_journal.IsOwner )
        {
            return false;
        }

        var targetsChanged = false;
        List<string> interruptedRoots = [];
        lock ( _watcherLock )
        {
            if ( _watchersDisposed )
            {
                return false;
            }

            HashSet<string> selected;
            lock ( _statusLock )
            {
                selected = reconciliationRoots is null
                    ? SelectRoots( rootId ).Select( static root => root.Path ).ToHashSet( StringComparer.Ordinal )
                    : reconciliationRoots;
            }
            // Healthy trees already buffer every event. Rebuild only missing or
            // retargeted roots; new overlapping roots add their parent below.
            if ( reconciliationRoots is null )
            {
                selected.RemoveWhere( IsHealthyTreeRoot );
            }
            var roots = CollectRestoreRoots( selected, out targetsChanged );
            if ( OperatingSystem.IsLinux( ) && selected.Count == 0 )
            {
                return targetsChanged;
            }

            if ( OperatingSystem.IsLinux( ) )
            {
                _watchLoop?.Dispose( );
                _watchLoop = null;
                List<InotifyTreeWatcher> retained = [];
                foreach ( var watcher in _treeWatchers )
                {
                    if ( selected.Contains( watcher.RootPath ) )
                    {
                        interruptedRoots.Add( watcher.RootPath );
                        watcher.Close( );
                    }
                    else
                    {
                        retained.Add( watcher );
                    }
                }
                _treeWatchers = [.. retained];
                lock ( _statusLock )
                {
                    foreach ( var root in _roots.Values.Where( root => selected.Contains( root.Path ) ) )
                    {
                        root.Watched = false;
                    }
                }
                StartTreeWatchers( roots, retained, selected, reconciliationRoots is not null, cancellationToken );
            }
            else
            {
                // Portable recursive watchers do not expose separate tree handles.
                DisposeWatchers( );
                lock ( _statusLock )
                {
                    interruptedRoots.AddRange( _roots.Values.Where( static root => root.Watched ).Select( static root => root.Path ) );
                    reconciliationRoots?.UnionWith( interruptedRoots );
                    foreach ( var root in _roots.Values )
                    {
                        root.Watched = false;
                    }
                }
                StartPortableWatchers( roots );
            }
        }

        foreach ( var root in interruptedRoots )
        {
            if ( reconciliationRoots?.Contains( root ) != true )
            {
                RecordOverflow( root );
            }
        }
        RefreshPolicyWatchers( );
        PersistRootStates( );
        return targetsChanged;
    }

    private bool IsHealthyTreeRoot( string path ) => _treeWatchers.Any( watcher =>
        watcher.RootPath == path && watcher.IsWatching && watcher.InitialTraversalComplete &&
        Directory.Exists( path ) &&
        CanonicalPathBoundary.TryCreate( path, out var boundary ) && boundary is not null &&
        watcher.CanonicalRoot == boundary.CanonicalRoot );

    private List<(string Path, IndexRootOptions Options)> CollectRestoreRoots(
        HashSet<string> selected, out bool targetsChanged )
    {
        targetsChanged = false;
        var previousTargets = _treeWatchers.ToDictionary( static watcher => watcher.RootPath,
            static watcher => watcher.CanonicalRoot, StringComparer.Ordinal );
        List<(string Path, IndexRootOptions Options)> roots = [];
        foreach ( var options in _options.Roots )
        {
            string path;
            try
            {
                path = IndexRootPaths.Normalize( options.Path );
            }
            catch ( ArgumentException )
            {
                MarkRootUnwatched( options.Path, "Configured root path is invalid." );
                continue;
            }

            if ( !Directory.Exists( path ) )
            {
                MarkRootUnwatched( path, "Configured root does not exist." );
                continue;
            }
            roots.Add( (path, options) );
            if ( !CanonicalPathBoundary.TryCreate( path, out var boundary ) || boundary is null )
            {
                continue;
            }
            if ( previousTargets.TryGetValue( path, out var previous ) &&
                !string.Equals( previous, boundary.CanonicalRoot, StringComparison.Ordinal ) )
            {
                targetsChanged = true;
                selected.Add( path );
            }
            if ( !selected.Contains( path ) )
            {
                continue;
            }

            // New nested/aliased roots change the parent's admitted tree.
            foreach ( var watcher in _treeWatchers )
            {
                if ( IsWithin( watcher.CanonicalRoot, boundary.CanonicalRoot ) ||
                    IsWithin( boundary.CanonicalRoot, watcher.CanonicalRoot ) )
                {
                    selected.Add( watcher.RootPath );
                }
            }
        }

        return roots;
    }

    private void MarkWatched( string path )
    {
        lock ( _statusLock )
        {
            var root = FindRoot( path );
            if ( root is not null )
            {
                root.Watched = true;
                root.LastError = null;
                _unwatchedRoots.Remove( path );
            }
        }
    }

    private void MarkUnwatched( string path, string error )
    {
        lock ( _statusLock )
        {
            var root = FindRoot( path );
            if ( root is not null )
            {
                root.Watched = false;
                root.LastError = error;
            }
        }
    }

    private void PersistRootStates( )
    {
        lock ( _statusLock )
        {
            PersistRootStatesUnderLock( );
        }
    }

    private void PersistRootStatesUnderLock( ) => _rootStateStore.Write( _roots.Values.Select(
        static root => new PersistedIndexRootState(
            root.Id,
            root.Path,
            root.Paused,
            root.PendingEventCount,
            root.IndexedItemCount,
            root.LastSuccessAt?.ToUnixTimeMilliseconds( ),
            root.LastError ) ) );

    private void ScheduleRetry( CancellationToken cancellationToken )
    {
        _ = Task.Run( async ( ) =>
        {
            try
            {
                await Task.Delay( 250, cancellationToken ).ConfigureAwait( false );
                Signal( );
            }
            catch ( OperationCanceledException )
            {
                // Normal shutdown.
            }
        }, CancellationToken.None );
    }

    private void Signal( )
    {
        try
        {
            _signal.Release( );
        }
        catch ( SemaphoreFullException )
        {
            // A pending wake-up already covers the newly recorded work.
        }
        catch ( ObjectDisposedException )
        {
            // A late filesystem notification raced with shutdown.
        }
    }

    private sealed class RootRuntimeState( string id, string path )
    {
        public string Id { get; } = id;
        public string Path { get; } = path;
        public bool Watched
        {
            get; set;
        }
        public bool Paused
        {
            get; set;
        }
        public long PendingEventCount
        {
            get; set;
        }
        public long IndexedItemCount
        {
            get; set;
        }
        public DateTimeOffset? LastSuccessAt
        {
            get; set;
        }
        public string? LastError
        {
            get; set;
        }
        public int WatchedDirectoryCount
        {
            get; set;
        }
        public long UnwatchedDirectoryCount
        {
            get; set;
        }

        // A root that is watched except for directories the watch limit left
        // out reports that, with the same remedy, until a retry succeeds.
        public IndexRootStatus Snapshot( )
        {
            var partiallyWatched = Watched && UnwatchedDirectoryCount > 0;
            var error = LastError ?? (partiallyWatched
                ? $"The inotify watch limit left {UnwatchedDirectoryCount} directories, and everything below them, unwatched."
                : null);
            return new IndexRootStatus
            {
                Id = Id,
                Path = Path,
                Watched = Watched,
                Paused = Paused,
                PendingEventCount = PendingEventCount,
                IndexedItemCount = IndexedItemCount,
                LastSuccessAt = LastSuccessAt,
                LastError = error,
                WatchedDirectoryCount = WatchedDirectoryCount,
                UnwatchedDirectoryCount = UnwatchedDirectoryCount,
                Remediation = partiallyWatched
                    ? "Raise fs.inotify.max_user_watches or exclude large directories, then resume or reindex this root."
                    : Watched || error is null
                        ? null
                        : "Check the root path and inotify limits, then resume or reindex this root."
            };
        }
    }
}
