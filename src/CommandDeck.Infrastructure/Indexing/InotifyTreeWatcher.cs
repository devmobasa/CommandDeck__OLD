namespace CommandDeck;

internal interface IDirectoryWatchSink
{
    void OnChange( IncrementalEventKind kind, string path, string? oldPath );

    // Events were lost; the root must be reconciled.
    void OnOverflow( string rootPath );

    void OnRootUnavailable( string rootPath, string message );
}

// Watches, in one inotify instance, only the directories that indexing
// traversal admits below one root, so excluded, hidden, generated, ignored,
// and privacy-denied trees and nested roots cost no watches. Changes are
// reported with paths under the configured root. When the kernel refuses a
// watch because the user's watch limit is reached, that directory and its
// subtree are counted as unwatched: nothing was lost from the event queue, so
// it is not an overflow and starts no reconcile.
internal sealed partial class InotifyTreeWatcher
{
    private const int NoSpace = InotifyNative.NoSpaceError;
    private const int TooManyInstances = 24;

    private readonly IInotifySystem _system;
    private readonly IndexExclusionPolicy _policy;
    private readonly CanonicalPathBoundary _boundary;
    private readonly IndexRootOptions _options;
    private readonly IReadOnlySet<string> _nestedRoots;
    private readonly IDirectoryWatchSink _sink;
    private readonly object _gate = new( );
    private readonly Dictionary<int, WatchNode> _nodes = [];
    private AuthorizedDirectory? _rootDirectory;
    private WatchNode? _root;
    private long _unwatchedDirectories;

    public InotifyTreeWatcher(
        IInotifySystem system,
        IndexExclusionPolicy policy,
        CanonicalPathBoundary boundary,
        IndexRootOptions options,
        string rootPath,
        IReadOnlySet<string> nestedRoots,
        IDirectoryWatchSink sink )
    {
        _system = system;
        _policy = policy;
        _boundary = boundary;
        _options = options;
        _nestedRoots = nestedRoots;
        _sink = sink;
        RootPath = rootPath;
    }

    // The configured root path that reported changes start with.
    public string RootPath
    {
        get;
    }
    public string CanonicalRoot => _boundary.CanonicalRoot;
    public int Descriptor { get; private set; } = -1;

    // Why the root itself is not watched, when it is not.
    public string? Error
    {
        get; private set;
    }

    public bool IsWatching
    {
        get
        {
            lock ( _gate )
            {
                return _root is not null;
            }
        }
    }

    public int WatchedDirectoryCount
    {
        get
        {
            lock ( _gate )
            {
                return _nodes.Count;
            }
        }
    }

    // Directories the kernel refused to watch; their subtrees are unwatched.
    public long UnwatchedDirectoryCount => Interlocked.Read( ref _unwatchedDirectories );

    // Watches the root directory itself; WatchTree adds the rest.
    public bool Start( )
    {
        var descriptor = _system.CreateInstance( );
        if ( descriptor < 0 )
        {
            Error = descriptor == -TooManyInstances
                ? "The inotify instance limit (fs.inotify.max_user_instances) is reached."
                : $"Could not create an inotify instance (errno {-descriptor}).";
            return false;
        }

        Descriptor = descriptor;
        _rootDirectory = _policy.EnterRoot( _boundary, _options, out _ );
        if ( _rootDirectory is null )
        {
            Error = "A privacy rule excludes the root, so nothing below it is watched.";
            return false;
        }

        lock ( _gate )
        {
            var watch = _system.AddWatch( Descriptor, _boundary.CanonicalRoot, WatchMask );
            if ( watch < 0 )
            {
                Error = watch == -NoSpace
                    ? "The inotify watch limit (fs.inotify.max_user_watches) is reached."
                    : "The root directory could not be watched.";
                return false;
            }

            _root = new WatchNode( watch, null, RootPath, _boundary.CanonicalRoot );
            _nodes.Add( watch, _root );
        }

        return true;
    }

    // Adds every admitted directory below the root. Event handling continues
    // between directories, so a large tree can be walked in the background.
    public void WatchTree( CancellationToken cancellationToken )
    {
        if ( _rootDirectory is not null )
        {
            AddTree( _rootDirectory, parent: null, RootPath, _boundary.CanonicalRoot, cancellationToken );
        }
    }

    // Re-walks the root to add watches the kernel refused earlier, for example
    // after the limit was raised.
    public void RetryUnwatched( )
    {
        if ( !IsWatching || Interlocked.Exchange( ref _unwatchedDirectories, 0 ) == 0 )
        {
            return;
        }

        WatchTree( CancellationToken.None );
    }

    // Adds watches for directories a policy change admitted below a path.
    public void Refresh( string path )
    {
        WatchNode? node;
        string canonicalPath;
        lock ( _gate )
        {
            node = FindDeepestNode( path );
            if ( node is null )
            {
                return;
            }

            canonicalPath = CanonicalPath( node );
        }

        var directory = _policy.EnterDirectory( _boundary, _options, canonicalPath, out _ );
        if ( directory is not null )
        {
            AddTree( directory, node.Parent, node.Name, node.CanonicalOverride, CancellationToken.None );
        }
    }

    public void Close( )
    {
        lock ( _gate )
        {
            if ( Descriptor >= 0 )
            {
                _system.Close( Descriptor );
                Descriptor = -1;
            }

            _nodes.Clear( );
            _root = null;
        }
    }

    // Events were read but could not be applied; the root must be reconciled.
    public void ReportLostEvents( ) => _sink.OnOverflow( RootPath );

    internal IReadOnlyList<string> SnapshotWatchedPaths( )
    {
        lock ( _gate )
        {
            return [.. _nodes.Values.Select( ReportedPath ).Order( StringComparer.Ordinal )];
        }
    }

    // Watches a directory and every admitted directory below it, one
    // directory per lock so events keep flowing. A directory that is already
    // watched is walked again, so a retry or refresh finds what is missing
    // below it. A refused watch leaves that subtree unwalked.
    private void AddTree(
        AuthorizedDirectory start,
        WatchNode? parent,
        string name,
        string? canonicalOverride,
        CancellationToken cancellationToken )
    {
        HashSet<int> visited = [];
        Stack<PendingWatch> pending = new( );
        pending.Push( new PendingWatch( start, parent, name, canonicalOverride ) );

        while ( pending.TryPop( out var next ) && !cancellationToken.IsCancellationRequested )
        {
            var node = WatchDirectory( next, visited );
            if ( node is null )
            {
                continue;
            }

            WatchNode[] previousChildren;
            lock ( _gate )
            {
                previousChildren = [.. node.Children];
            }

            var children = AdmittedChildren( next.Directory );
            if ( children is null )
            {
                continue;
            }

            RemoveExcludedChildren( previousChildren, children );
            foreach ( var child in children )
            {
                pending.Push( new PendingWatch( child.Directory, node, child.Name, child.CanonicalOverride ) );
            }
        }
    }

    private WatchNode? WatchDirectory( PendingWatch next, HashSet<int> visited )
    {
        lock ( _gate )
        {
            if ( Descriptor < 0 || (next.Parent is not null && !IsRegistered( next.Parent )) )
            {
                return null;
            }

            // Pending children were authorized before they reached this walk.
            // A parent replaced in the meantime must not redirect a kernel watch.
            if ( !IsCurrentDirectory( next.Directory.Path ) )
            {
                return null;
            }

            var watch = _system.AddWatch( Descriptor, next.Directory.Path, WatchMask );
            if ( watch == -NoSpace )
            {
                Interlocked.Increment( ref _unwatchedDirectories );
                return null;
            }

            if ( watch < 0 || !visited.Add( watch ) )
            {
                return null;
            }

            if ( _nodes.TryGetValue( watch, out var existing ) )
            {
                return existing;
            }

            WatchNode node = new( watch, next.Parent, next.Name, next.CanonicalOverride );
            _nodes.Add( watch, node );
            if ( next.Parent is null )
            {
                _root = node;
            }
            else
            {
                next.Parent.SetChild( node );
            }

            return node;
        }
    }

    // A parent removed while the walk was below it (moved or deleted) takes
    // its unwalked children with it.
    private bool IsRegistered( WatchNode node ) =>
        _nodes.TryGetValue( node.Watch, out var registered ) && ReferenceEquals( registered, node );

    private List<AdmittedChild>? AdmittedChildren( AuthorizedDirectory directory )
    {
        if ( !IsCurrentDirectory( directory.Path ) )
        {
            return null;
        }

        List<AdmittedChild> children = [];
        try
        {
            foreach ( var entry in Directory.EnumerateDirectories( directory.Path ) )
            {
                var child = AdmitChild( directory, entry );
                if ( child is not null )
                {
                    children.Add( child.Value );
                }
            }
        }
        catch ( Exception exception ) when (
            exception is IOException or UnauthorizedAccessException )
        {
            // An unavailable enumeration cannot show which existing children
            // became excluded, so keep their watches until a later refresh.
            return null;
        }

        return children;
    }

    private bool IsCurrentDirectory( string path ) =>
        _boundary.IsCurrent( ) &&
        _boundary.TryResolve( path, _options.FollowSymbolicLinks, out var verifiedPath ) &&
        string.Equals( path, verifiedPath, StringComparison.Ordinal );

    // Policy refreshes both acquire newly admitted watches and release trees
    // that became ignored or generated. Keep the directory itself watched so
    // another rule change can admit those trees again.
    private void RemoveExcludedChildren( WatchNode[] previousChildren, List<AdmittedChild> admitted )
    {
        var names = admitted.Select( static child => child.Name ).ToHashSet( StringComparer.Ordinal );
        lock ( _gate )
        {
            // A child created while the directory was enumerated is handled by
            // its own event; only inspect watches present before enumeration.
            foreach ( var child in previousChildren )
            {
                if ( IsRegistered( child ) && !names.Contains( child.Name ) )
                {
                    RemoveSubtree( child, removeWatches: true );
                }
            }
        }
    }

    // The traversal's own decision for a child directory. Nested roots are
    // watched by their own root, once.
    private AdmittedChild? AdmitChild( AuthorizedDirectory parent, string path )
    {
        var name = Path.GetFileName( path );
        if ( !LinuxMountIdentity.TryGetEntry( path, out var status ) )
        {
            return null;
        }

        if ( status.SymbolicLink )
        {
            return AdmitLinkedChild( path, name );
        }

        if ( !status.Directory || !_boundary.IsOnRootMount( status.MountId ) ||
            _policy.MatchChild( parent, path, directory: true, out var child ) is not null ||
            child is null || _nestedRoots.Contains( path ) )
        {
            return null;
        }

        return new AdmittedChild( child, name, null );
    }

    private AdmittedChild? AdmitLinkedChild( string path, string name )
    {
        if ( !_options.FollowSymbolicLinks ||
            !_boundary.TryResolve( path, followSymbolicLinks: true, out var target ) ||
            !Directory.Exists( target ) ||
            _nestedRoots.Contains( target ) ||
            _policy.Match( _boundary, _options, path, target, directory: true ) is not null )
        {
            return null;
        }

        var linked = _policy.EnterDirectory( _boundary, _options, target, out _ );
        return linked is null ? null : new AdmittedChild( linked, name, target );
    }

    private string ReportedPath( WatchNode node ) =>
        node.Parent is null ? RootPath : Path.Join( ReportedPath( node.Parent ), node.Name );

    private static string CanonicalPath( WatchNode node ) =>
        node.CanonicalOverride ?? Path.Join( CanonicalPath( node.Parent! ), node.Name );

    private WatchNode? FindDeepestNode( string path )
    {
        var node = _root;
        if ( node is null || !IndexExclusionPolicy.IsWithin( path, RootPath ) )
        {
            return null;
        }

        var relative = Path.GetRelativePath( RootPath, path );
        if ( relative == "." )
        {
            return node;
        }

        foreach ( var segment in relative.Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries ) )
        {
            var child = node.GetChild( segment );
            if ( child is null )
            {
                break;
            }

            node = child;
        }

        return node;
    }

    private void RemoveSubtree( WatchNode node, bool removeWatches )
    {
        node.Parent?.RemoveChild( node );
        Stack<WatchNode> pending = new( );
        pending.Push( node );
        while ( pending.TryPop( out var current ) )
        {
            _nodes.Remove( current.Watch );
            if ( removeWatches )
            {
                _system.RemoveWatch( Descriptor, current.Watch );
            }

            foreach ( var child in current.Children )
            {
                pending.Push( child );
            }
        }

        if ( ReferenceEquals( node, _root ) )
        {
            _root = null;
        }
    }

    private readonly record struct PendingWatch(
        AuthorizedDirectory Directory,
        WatchNode? Parent,
        string Name,
        string? CanonicalOverride );

    private readonly record struct AdmittedChild(
        AuthorizedDirectory Directory,
        string Name,
        string? CanonicalOverride );

    // A watched directory. Only its name is stored; full paths are rebuilt
    // from the root, which keeps a large tree of watches compact.
    private sealed class WatchNode( int watch, WatchNode? parent, string name, string? canonicalOverride )
    {
        private Dictionary<string, WatchNode>? _children;

        public int Watch { get; } = watch;
        public WatchNode? Parent { get; } = parent;
        public string Name { get; } = name;
        public string? CanonicalOverride { get; } = canonicalOverride;

        public IEnumerable<WatchNode> Children => _children?.Values ?? Enumerable.Empty<WatchNode>( );

        public WatchNode? GetChild( string name ) =>
            _children is not null && _children.TryGetValue( name, out var child ) ? child : null;

        public void SetChild( WatchNode child ) =>
            (_children ??= new( StringComparer.Ordinal ))[child.Name] = child;

        public void RemoveChild( WatchNode child )
        {
            if ( _children is not null &&
                _children.TryGetValue( child.Name, out var existing ) &&
                ReferenceEquals( existing, child ) )
            {
                _children.Remove( child.Name );
            }
        }
    }
}
