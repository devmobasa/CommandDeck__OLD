using System.Runtime.InteropServices;

namespace CommandDeck.Specs;

public static partial class Program
{
    private const uint InotifyMovedFrom = 0x40;
    private const uint InotifyMovedTo = 0x80;
    private const uint InotifyCreated = 0x100;
    private const uint InotifyQueueOverflow = 0x4000;
    private const uint InotifyIgnored = 0x8000;
    private const uint InotifyIsDirectory = 0x40000000;

    private static void InotifyEventsKeepTheWatchedTreeInStep( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "root" );
        Directory.CreateDirectory( Path.Combine( root, "a", "deep" ) );
        Directory.CreateDirectory( Path.Combine( root, "b" ) );
        Directory.CreateDirectory( Path.Combine( root, "node_modules", "pkg" ) );

        try
        {
            IndexRootOptions options = new( )
            {
                Path = root
            };
            IndexExclusionPolicy policy = new( new IndexingOptions { Roots = [options] }, new PrivacyOptions( ) );
            True( CanonicalPathBoundary.TryCreate( root, out var boundary ) );
            RecordingWatchSink sink = new( );
            ScriptedInotifySystem system = new( );
            InotifyTreeWatcher watcher = new( system, policy, boundary!, options, root, new HashSet<string>( ), sink );
            True( watcher.Start( ) );
            watcher.WatchTree( CancellationToken.None );

            // The generated tree was never watched; everything else was.
            Equal( "a, a/deep, b, root", string.Join( ", ", system.WatchedRelativePaths( root ) ) );

            var rootWatch = system.WatchOf( root );
            watcher.ProcessEvents( InotifyEvents(
                (rootWatch, InotifyMovedFrom, 7u, "draft.txt"),
                (rootWatch, InotifyMovedTo, 7u, "final.txt"),
                (rootWatch, InotifyMovedFrom, 8u, "gone.txt"),
                (rootWatch, InotifyMovedTo, 9u, "came.txt"),
                (-1, InotifyQueueOverflow, 0u, "") ) );
            Equal(
                $"Rename {root}/final.txt <- {root}/draft.txt; Delete {root}/gone.txt; Upsert {root}/came.txt; Overflow {root}",
                string.Join( "; ", sink.Take( ) ) );

            // A directory moved away stops being watched; a new one is watched
            // only when the traversal would enter it.
            watcher.ProcessEvents( InotifyEvents( (rootWatch, InotifyMovedFrom | InotifyIsDirectory, 10u, "a") ) );
            Directory.CreateDirectory( Path.Combine( root, "c", "inner" ) );
            Directory.CreateDirectory( Path.Combine( root, "b", "node_modules" ) );
            watcher.ProcessEvents( InotifyEvents(
                (rootWatch, InotifyCreated | InotifyIsDirectory, 0u, "c"),
                (system.WatchOf( Path.Combine( root, "b" ) ), InotifyCreated | InotifyIsDirectory, 0u, "node_modules") ) );
            Equal( $"Delete {root}/a; Upsert {root}/c; Upsert {root}/b/node_modules", string.Join( "; ", sink.Take( ) ) );
            Equal( "b, c, c/inner, root", string.Join( ", ", system.WatchedRelativePaths( root ) ) );
            Equal( "b, c, c/inner, root", string.Join( ", ", watcher.SnapshotWatchedPaths( )
                .Select( path => path == root ? "root" : Path.GetRelativePath( root, path ) )
                .Order( StringComparer.Ordinal ) ) );

            // The kernel dropping the root's watch makes the root unavailable.
            watcher.ProcessEvents( InotifyEvents( (rootWatch, InotifyIgnored, 0u, "") ) );
            Equal( $"Unavailable {root}", string.Join( "; ", sink.Take( ) ) );
            True( !watcher.IsWatching );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static byte[] InotifyEvents( params (int Watch, uint Mask, uint Cookie, string Name)[] events )
    {
        List<byte> buffer = [];
        foreach ( var value in events )
        {
            var name = System.Text.Encoding.UTF8.GetBytes( value.Name );
            var padded = value.Name.Length == 0 ? 0 : (name.Length + 16) & ~15;
            var header = new byte[16].AsSpan( );
            MemoryMarshal.Write( header, in value.Watch );
            MemoryMarshal.Write( header[4..], in value.Mask );
            MemoryMarshal.Write( header[8..], in value.Cookie );
            var length = ( uint ) padded;
            MemoryMarshal.Write( header[12..], in length );
            buffer.AddRange( header.ToArray( ) );
            buffer.AddRange( name );
            buffer.AddRange( new byte[padded - name.Length] );
        }

        return [.. buffer];
    }

    private sealed class RecordingWatchSink : IDirectoryWatchSink
    {
        private readonly List<string> _notifications = [];

        public void OnChange( IncrementalEventKind kind, string path, string? oldPath ) =>
            _notifications.Add( oldPath is null ? $"{kind} {path}" : $"{kind} {path} <- {oldPath}" );

        public void OnOverflow( string rootPath ) => _notifications.Add( $"Overflow {rootPath}" );

        public void OnRootUnavailable( string rootPath, string message ) =>
            _notifications.Add( $"Unavailable {rootPath}" );

        public string[] Take( )
        {
            var taken = _notifications.ToArray( );
            _notifications.Clear( );
            return taken;
        }
    }

    // Assigns watch descriptors itself so a spec can address them in
    // synthetic event buffers. It never reads from the kernel.
    private sealed class ScriptedInotifySystem : IInotifySystem
    {
        private readonly Dictionary<string, int> _watches = new( StringComparer.Ordinal );
        private int _nextWatch = 1;

        public int WatchOf( string path ) => _watches[path];

        public string[] WatchedRelativePaths( string root ) =>
        [
            .. _watches.Keys
                .Select( path => path == root ? "root" : Path.GetRelativePath( root, path ) )
                .Order( StringComparer.Ordinal )
        ];

        public int CreateInstance( ) => 100;

        public int AddWatch( int instance, string path, uint mask )
        {
            if ( !Directory.Exists( path ) )
            {
                return -2;
            }

            if ( !_watches.TryGetValue( path, out var watch ) )
            {
                watch = _nextWatch++;
                _watches[path] = watch;
            }

            return watch;
        }

        public void RemoveWatch( int instance, int watch )
        {
            foreach ( var pair in _watches.Where( pair => pair.Value == watch ).ToArray( ) )
            {
                _watches.Remove( pair.Key );
            }
        }

        public int CreateWake( ) => throw new NotSupportedException( );

        public void Wake( int wake ) => throw new NotSupportedException( );

        public int Poll( ReadOnlySpan<int> descriptors, Span<bool> readable ) => throw new NotSupportedException( );

        public int Read( int descriptor, Span<byte> buffer ) => throw new NotSupportedException( );

        public void Close( int descriptor )
        {
        }
    }

    // The real kernel, with a watch limit of its own: a new watch beyond the
    // limit is removed again and refused with ENOSPC, as the kernel does.
    private sealed class LimitedInotifySystem( int limit ) : IInotifySystem
    {
        private readonly LinuxInotifySystem _inner = LinuxInotifySystem.Instance;
        private readonly HashSet<(int Instance, int Watch)> _watches = [];

        public int Limit { get; set; } = limit;

        public int CreateInstance( ) => _inner.CreateInstance( );

        public int AddWatch( int instance, string path, uint mask )
        {
            lock ( _watches )
            {
                var watch = _inner.AddWatch( instance, path, mask );
                if ( watch < 0 || _watches.Contains( (instance, watch) ) )
                {
                    return watch;
                }

                if ( _watches.Count >= Limit )
                {
                    _inner.RemoveWatch( instance, watch );
                    return -28;
                }

                _watches.Add( (instance, watch) );
                return watch;
            }
        }

        public void RemoveWatch( int instance, int watch )
        {
            lock ( _watches )
            {
                _watches.Remove( (instance, watch) );
            }

            _inner.RemoveWatch( instance, watch );
        }

        public int CreateWake( ) => _inner.CreateWake( );

        public void Wake( int wake ) => _inner.Wake( wake );

        public int Poll( ReadOnlySpan<int> descriptors, Span<bool> readable ) => _inner.Poll( descriptors, readable );

        public int Read( int descriptor, Span<byte> buffer ) => _inner.Read( descriptor, buffer );

        public void Close( int descriptor ) => _inner.Close( descriptor );
    }
}
