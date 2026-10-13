using System.Runtime.InteropServices;
using System.Text;

namespace CommandDeck;

internal sealed partial class InotifyTreeWatcher
{
    // Linux inotify(7) flags.
    private const uint Modified = 0x00000002;
    private const uint AttributesChanged = 0x00000004;
    private const uint MovedFrom = 0x00000040;
    private const uint MovedTo = 0x00000080;
    private const uint Created = 0x00000100;
    private const uint Deleted = 0x00000200;
    private const uint SelfDeleted = 0x00000400;
    private const uint SelfMoved = 0x00000800;
    private const uint QueueOverflow = 0x00004000;
    private const uint Ignored = 0x00008000;
    private const uint OnlyDirectory = 0x01000000;
    private const uint DoNotFollow = 0x02000000;
    private const uint ExcludeUnlinked = 0x04000000;
    private const uint IsDirectory = 0x40000000;
    private const int EventHeaderBytes = 16;

    internal const uint WatchMask = Modified | AttributesChanged | MovedFrom | MovedTo | Created |
        Deleted | SelfDeleted | SelfMoved | OnlyDirectory | DoNotFollow | ExcludeUnlinked;

    // Applies one read of events: keeps the watched tree in step with created,
    // moved, and deleted directories, then reports the changes. A move is
    // reported as a rename when both halves arrive together.
    public void ProcessEvents( ReadOnlySpan<byte> buffer )
    {
        List<WatchNotification> notifications = [];
        lock ( _gate )
        {
            var offset = 0;
            while ( TryReadEvent( buffer, ref offset, out var current ) )
            {
                if ( (current.Mask & MovedFrom) != 0 )
                {
                    var following = offset;
                    if ( TryReadEvent( buffer, ref following, out var next ) &&
                        (next.Mask & MovedTo) != 0 &&
                        next.Cookie == current.Cookie )
                    {
                        HandleRename( current, next, notifications );
                        offset = following;
                        continue;
                    }
                }

                Handle( current, notifications );
            }
        }

        foreach ( var notification in notifications )
        {
            Notify( notification );
        }
    }

    private void Handle( RawEvent current, List<WatchNotification> notifications )
    {
        if ( (current.Mask & QueueOverflow) != 0 )
        {
            notifications.Add( WatchNotification.Overflow );
            return;
        }

        if ( !_nodes.TryGetValue( current.Watch, out var node ) )
        {
            return;
        }

        if ( (current.Mask & Ignored) != 0 )
        {
            ForgetWatch( node, notifications );
            return;
        }

        if ( current.Name.Length == 0 )
        {
            // Other directories are reported by their parent's watch.
            if ( ReferenceEquals( node, _root ) && (current.Mask & (SelfDeleted | SelfMoved)) != 0 )
            {
                RemoveSubtree( node, removeWatches: true );
                notifications.Add( WatchNotification.RootLost );
            }

            return;
        }

        var path = Path.Join( ReportedPath( node ), current.Name );
        var directory = (current.Mask & IsDirectory) != 0;
        if ( (current.Mask & MovedFrom) != 0 )
        {
            RemoveChildTree( node, current.Name, directory, removeWatches: true );
            notifications.Add( WatchNotification.Change( IncrementalEventKind.Delete, path, null ) );
        }
        else if ( (current.Mask & Deleted) != 0 )
        {
            RemoveChildTree( node, current.Name, directory, removeWatches: false );
            notifications.Add( WatchNotification.Change( IncrementalEventKind.Delete, path, null ) );
        }
        else if ( (current.Mask & (Created | MovedTo)) != 0 )
        {
            if ( directory )
            {
                WatchNewDirectory( node, current.Name );
            }

            notifications.Add( WatchNotification.Change( IncrementalEventKind.Upsert, path, null ) );
        }
        else if ( (current.Mask & (Modified | AttributesChanged)) != 0 )
        {
            notifications.Add( WatchNotification.Change( IncrementalEventKind.Upsert, path, null ) );
        }
    }

    private void HandleRename( RawEvent from, RawEvent to, List<WatchNotification> notifications )
    {
        _nodes.TryGetValue( from.Watch, out var fromNode );
        _nodes.TryGetValue( to.Watch, out var toNode );
        var directory = (from.Mask & IsDirectory) != 0;
        var oldPath = fromNode is null ? null : Path.Join( ReportedPath( fromNode ), from.Name );
        var newPath = toNode is null ? null : Path.Join( ReportedPath( toNode ), to.Name );

        // The moved tree is watched afresh under its new name and policy.
        if ( fromNode is not null )
        {
            RemoveChildTree( fromNode, from.Name, directory, removeWatches: true );
        }

        if ( toNode is not null && directory )
        {
            WatchNewDirectory( toNode, to.Name );
        }

        if ( oldPath is not null && newPath is not null )
        {
            notifications.Add( WatchNotification.Change( IncrementalEventKind.Rename, newPath, oldPath ) );
        }
        else if ( oldPath is not null )
        {
            notifications.Add( WatchNotification.Change( IncrementalEventKind.Delete, oldPath, null ) );
        }
        else if ( newPath is not null )
        {
            notifications.Add( WatchNotification.Change( IncrementalEventKind.Upsert, newPath, null ) );
        }
    }

    // The traversal decides whether a new directory is watched; contents
    // created before its watch are covered by the directory's own rescan.
    private void WatchNewDirectory( WatchNode parent, string name )
    {
        var parentPath = CanonicalPath( parent );
        var directory = _policy.EnterDirectory( _boundary, _options, parentPath, out _ );
        var child = directory is null ? null : AdmitChild( directory, Path.Join( parentPath, name ) );
        if ( child is not null )
        {
            AddTree( child.Value.Directory, parent, child.Value.Name, child.Value.CanonicalOverride, CancellationToken.None );
        }
    }

    private void RemoveChildTree( WatchNode parent, string name, bool directory, bool removeWatches )
    {
        var child = directory ? parent.GetChild( name ) : null;
        if ( child is not null )
        {
            RemoveSubtree( child, removeWatches );
        }
    }

    // The kernel dropped a watch: its directory was deleted or unmounted.
    private void ForgetWatch( WatchNode node, List<WatchNotification> notifications )
    {
        var root = ReferenceEquals( node, _root );
        RemoveSubtree( node, removeWatches: false );
        if ( root )
        {
            notifications.Add( WatchNotification.RootLost );
        }
    }

    private void Notify( WatchNotification notification )
    {
        switch ( notification.Type )
        {
            case WatchNotificationType.Overflow:
                _sink.OnOverflow( RootPath );
                break;
            case WatchNotificationType.RootLost:
                _sink.OnRootUnavailable( RootPath, "The root directory was moved, deleted, or unmounted." );
                break;
            default:
                _sink.OnChange( notification.Kind, notification.Path!, notification.OldPath );
                break;
        }
    }

    private static bool TryReadEvent( ReadOnlySpan<byte> buffer, ref int offset, out RawEvent value )
    {
        value = default;
        if ( buffer.Length - offset < EventHeaderBytes )
        {
            return false;
        }

        var header = buffer[offset..];
        var watch = MemoryMarshal.Read<int>( header );
        var mask = MemoryMarshal.Read<uint>( header[4..] );
        var cookie = MemoryMarshal.Read<uint>( header[8..] );
        var nameBytes = MemoryMarshal.Read<uint>( header[12..] );
        if ( nameBytes > ( uint ) (buffer.Length - offset - EventHeaderBytes) )
        {
            return false;
        }

        var name = header.Slice( EventHeaderBytes, ( int ) nameBytes );
        var terminator = name.IndexOf( ( byte ) 0 );
        if ( terminator >= 0 )
        {
            name = name[..terminator];
        }

        value = new RawEvent( watch, mask, cookie, Encoding.UTF8.GetString( name ) );
        offset += EventHeaderBytes + ( int ) nameBytes;
        return true;
    }

    private readonly record struct RawEvent( int Watch, uint Mask, uint Cookie, string Name );

    private enum WatchNotificationType
    {
        Change,
        Overflow,
        RootLost
    }

    private readonly record struct WatchNotification(
        WatchNotificationType Type,
        IncrementalEventKind Kind,
        string? Path,
        string? OldPath )
    {
        public static WatchNotification Overflow => new( WatchNotificationType.Overflow, default, null, null );

        public static WatchNotification RootLost => new( WatchNotificationType.RootLost, default, null, null );

        public static WatchNotification Change( IncrementalEventKind kind, string path, string? oldPath ) =>
            new( WatchNotificationType.Change, kind, path, oldPath );
    }
}
