namespace CommandDeck;

internal sealed partial class IncrementalIndexEngine : IDirectoryWatchSink
{
    private const string WatchLimitMessage =
        "Some indexed directories are not watched because the inotify watch limit " +
        "(fs.inotify.max_user_watches) is reached.";

    private readonly IInotifySystem _inotify;
    private readonly List<FileSystemWatcher> _portableWatchers = [];
    private InotifyTreeWatcher[] _treeWatchers = [];
    private InotifyWatchLoop? _watchLoop;

    void IDirectoryWatchSink.OnChange( IncrementalEventKind kind, string path, string? oldPath ) =>
        Record( kind, path, oldPath );

    void IDirectoryWatchSink.OnOverflow( string rootPath ) => RecordOverflow( rootPath );

    void IDirectoryWatchSink.OnRootUnavailable( string rootPath, string message )
    {
        MarkRootUnwatched( rootPath, message );
        PersistRootStates( );
    }

    internal IReadOnlyList<string> SnapshotWatchedDirectories( ) =>
        [.. _treeWatchers.SelectMany( static watcher => watcher.SnapshotWatchedPaths( ) )];

    // Each root has its own inotify instance, so a queue overflow reconciles
    // only that root. A root nested in another is skipped by the outer walk
    // and watched once, by its own watcher.
    private void StartTreeWatchers(
        List<(string Path, IndexRootOptions Options)> roots,
        IReadOnlyList<InotifyTreeWatcher>? retained = null,
        HashSet<string>? selected = null,
        bool coveredByReconciliation = false,
        CancellationToken cancellationToken = default )
    {
        List<(string Path, CanonicalPathBoundary Boundary, IndexRootOptions Options)> resolved = [];
        foreach ( (var path, var options) in roots )
        {
            if ( !CanonicalPathBoundary.TryCreate( options.Path, out var boundary ) || boundary is null )
            {
                MarkRootUnwatched( path, "Configured root could not be resolved." );
                continue;
            }

            resolved.Add( (path, boundary, options) );
        }

        var distinct = resolved
            .Where( root => resolved.Count( other => string.Equals(
                other.Boundary.CanonicalRoot, root.Boundary.CanonicalRoot, StringComparison.Ordinal ) ) == 1 )
            .ToList( );
        foreach ( var root in resolved.Except( distinct ) )
        {
            MarkRootUnwatched( root.Path, "Another configured root names the same directory." );
        }

        List<InotifyTreeWatcher> watchers = [];
        foreach ( var root in distinct )
        {
            var existing = retained?.SingleOrDefault( watcher =>
                string.Equals( watcher.RootPath, root.Path, StringComparison.Ordinal ) &&
                string.Equals( watcher.CanonicalRoot, root.Boundary.CanonicalRoot, StringComparison.Ordinal ) );
            if ( existing is not null )
            {
                watchers.Add( existing );
                continue;
            }
            if ( selected is not null && !selected.Contains( root.Path ) )
            {
                continue;
            }

            var nested = distinct
                .Where( other =>
                    other.Boundary.CanonicalRoot.Length > root.Boundary.CanonicalRoot.Length &&
                    root.Boundary.Contains( other.Boundary.CanonicalRoot ) )
                .Select( static other => other.Boundary.CanonicalRoot )
                .ToHashSet( StringComparer.Ordinal );
            InotifyTreeWatcher watcher = new( _inotify, _exclusions, root.Boundary, root.Options, root.Path, nested, this );
            if ( !watcher.Start( ) )
            {
                watcher.Close( );
                SetError( watcher.Error! );
                MarkRootUnwatched( root.Path, watcher.Error! );
                continue;
            }

            watchers.Add( watcher );
            MarkWatched( root.Path );
        }

        if ( retained is not null )
        {
            foreach ( var watcher in retained.Except( watchers ) )
            {
                watcher.Close( );
                selected?.Add( watcher.RootPath );
                if ( !coveredByReconciliation )
                {
                    RecordOverflow( watcher.RootPath );
                }
            }
        }
        StartWatchLoop( watchers, retained, cancellationToken );
    }

    private void StartWatchLoop( List<InotifyTreeWatcher> watchers, IReadOnlyList<InotifyTreeWatcher>? retained, CancellationToken cancellationToken )
    {
        if ( watchers.Count == 0 )
        {
            return;
        }

        try
        {
            _watchLoop = new InotifyWatchLoop( _inotify, watchers );
            _treeWatchers = [.. watchers];
        }
        catch ( IOException exception )
        {
            SetError( exception.Message );
            foreach ( var watcher in watchers )
            {
                watcher.Close( );
                MarkRootUnwatched( watcher.RootPath, exception.Message );
            }

            return;
        }

        // The loop already drains events while the admitted directories are
        // walked, so a large tree does not fill the kernel's queue. Start
        // returns once every admitted directory is watched or refused.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource( _lifetime.Token, cancellationToken );
        WatchTrees( watchers.Where( watcher => retained?.Contains( watcher ) != true ).ToList( ), linked.Token );
    }

    private void WatchTrees( List<InotifyTreeWatcher> watchers, CancellationToken cancellationToken )
    {
        foreach ( var watcher in watchers )
        {
            try
            {
                watcher.WatchTree( cancellationToken );
            }
            catch ( Exception exception ) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException )
            {
                SetError( $"Could not watch every directory below {watcher.RootPath}: {exception.Message}" );
            }
        }
    }

    // Other platforms keep the runtime's recursive watcher.
    private void StartPortableWatchers( List<(string Path, IndexRootOptions Options)> roots )
    {
        foreach ( (var root, _) in roots )
        {
            try
            {
                FileSystemWatcher watcher = new( root )
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName |
                        NotifyFilters.DirectoryName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size |
                        NotifyFilters.CreationTime
                };
                watcher.Created += OnUpsert;
                watcher.Changed += OnUpsert;
                watcher.Deleted += OnDelete;
                watcher.Renamed += OnRename;
                watcher.Error += ( _, _ ) => RecordOverflow( root );
                watcher.EnableRaisingEvents = true;
                _portableWatchers.Add( watcher );
                MarkWatched( root );
            }
            catch ( Exception exception ) when (
                exception is ArgumentException or IOException or UnauthorizedAccessException )
            {
                SetError( exception.Message );
                MarkRootUnwatched( root, exception.Message );
            }
        }
    }

    private void MarkRootUnwatched( string rootPath, string message )
    {
        lock ( _statusLock )
        {
            if ( !_unwatchedRoots.Contains( rootPath, StringComparer.Ordinal ) )
            {
                _unwatchedRoots.Add( rootPath );
            }
        }

        MarkUnwatched( rootPath, message );
    }

    private int WatchingRootCount( ) =>
        _portableWatchers.Count + _treeWatchers.Count( static watcher => watcher.IsWatching );

    private Dictionary<string, (int Watched, long Unwatched)> SnapshotWatchCoverage( )
    {
        Dictionary<string, (int Watched, long Unwatched)> coverage = new( StringComparer.Ordinal );
        foreach ( var watcher in _treeWatchers )
        {
            coverage[watcher.RootPath] = (watcher.WatchedDirectoryCount, watcher.UnwatchedDirectoryCount);
        }

        return coverage;
    }

    public void RetryUnwatchedDirectories( string rootPath, CancellationToken cancellationToken )
    {
        foreach ( var watcher in _treeWatchers )
        {
            if ( watcher.UnwatchedDirectoryCount > 0 &&
                (string.Equals( watcher.RootPath, rootPath, StringComparison.Ordinal ) ||
                string.Equals( watcher.CanonicalRoot, rootPath, StringComparison.Ordinal )) )
            {
                watcher.RetryUnwatched( cancellationToken );
            }
        }
    }

    // A policy change can admit directories that were never watched.
    private void RefreshWatchedScope( string scope )
    {
        foreach ( var watcher in _treeWatchers )
        {
            if ( IsWithin( scope, watcher.RootPath ) )
            {
                watcher.Refresh( scope );
            }
        }
    }

    private void DisposeWatchers( )
    {
        _watchLoop?.Dispose( );
        _watchLoop = null;
        foreach ( var watcher in _treeWatchers )
        {
            watcher.Close( );
        }

        _treeWatchers = [];

        foreach ( var watcher in _portableWatchers )
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose( );
        }

        _portableWatchers.Clear( );
    }
}
