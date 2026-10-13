using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommandDeck.ExtensionProtocol;
using Microsoft.Win32.SafeHandles;

namespace CommandDeck;

/// <summary>A user-selected inode, never a directory mount in the child. All accesses are descriptor-relative.</summary>
internal sealed partial class ExtensionFileRoot : IDisposable
{
    private readonly SafeFileHandle _root;
    private readonly string _path;
    private readonly string _identity;
    private readonly bool _directory;
    private readonly Dictionary<string, Entry> _references = new( StringComparer.Ordinal );
    private readonly object _gate = new( );
    private sealed record Entry( string Path, string Identity, bool Directory );
    private static readonly string Home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
    private static readonly string[] ProtectedRoots = ["/etc", "/proc", "/sys", "/dev", "/run", "/boot"];

    public ExtensionFileRoot( string path )
    {
        if ( !Path.IsPathFullyQualified( path ) || path.Length > 4096 || path.Contains( '\0' ) )
        {
            throw new InvalidOperationException( "Select an absolute file or folder path." );
        }
        _path = Path.TrimEndingDirectorySeparator( Path.GetFullPath( path ) );
        if ( _path == Home || _path == "/" || Protected( _path ) )
        {
            throw new InvalidOperationException( "Select a non-protected file or a narrower folder." );
        }
        _root = Open( LinuxConstants.AtCurrentDirectory, _path, LinuxConstants.OPath, LinuxConstants.ResolveNoSymlinks );
        try
        {
            if ( !LinuxMountIdentity.TryGetNode( _root, out _identity, out _directory, out var links ) || !_directory && links != 1 )
            {
                throw new InvalidOperationException( "Select a regular file or folder without hard links." );
            }
        }
        catch
        {
            _root.Dispose( );
            throw;
        }
    }

    public BrokerResult Access( BrokerOperation request, CancellationToken cancellationToken )
    {
        lock ( _gate )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            ValidateRoot( );
            if ( request is FileReadOperation read )
            {
                return Read( read.Reference, cancellationToken );
            }
            if ( request is not FileListOperation listing || listing.Offset < 0 || listing.Offset > 4096 )
            {
                throw new InvalidOperationException( "Invalid file operation or page." );
            }
            var relative = listing.Reference is null ? "." : Resolve( listing.Reference, true ).Path;
            if ( !_directory )
            {
                return new( ExtensionBrokerOutcomes.Completed, [Reference( ".", Path.GetFileName( _path ) )] );
            }
            using var directory = OpenRelative( relative, checked(( ulong ) (LinuxConstants.OPath | LinuxConstants.ODirectory)) );
            List<BrokerFileEntry> entries = [];
            var names = Directory.EnumerateFileSystemEntries( DescriptorPath( directory ) ).Take( 4097 ).Select( Path.GetFileName ).ToArray( );
            if ( names.Length > 4096 )
            {
                throw new InvalidOperationException( "This folder exceeds the 4096-entry limit. Select a narrower folder." );
            }
            foreach ( var name in names.Order( StringComparer.Ordinal ).Skip( listing.Offset ).Take( 32 ) )
            {
                cancellationToken.ThrowIfCancellationRequested( );
                var child = relative == "." ? name! : relative + "/" + name;
                if ( Protected( Path.Combine( _path, child ) ) )
                {
                    continue;
                }
                try
                {
                    entries.Add( Reference( child, name! ) );
                }
                catch ( InvalidOperationException ) { /* Links, devices and protected objects are not offered. */ }
            }
            ValidateRoot( );
            return new( ExtensionBrokerOutcomes.Completed, entries.ToArray( ), listing.Offset + 32 < names.Length ? listing.Offset + 32 : null );
        }
    }

    private BrokerFileEntry Reference( string relative, string name )
    {
        using var handle = OpenRelative( relative, LinuxConstants.OPath );
        if ( !LinuxMountIdentity.TryGetNode( handle, out var node, out var directory, out var links ) || !directory && links != 1 )
        {
            throw new InvalidOperationException( "Unsupported file type." );
        }
        long size = 0;
        if ( !directory )
        {
            if ( !LinuxMountIdentity.TryGet( handle, out var identity ) )
            {
                throw new InvalidOperationException( "Invalid file." );
            }
            node = identity.ToString( );
            size = checked(( long ) identity.Size);
        }
        var existing = _references.FirstOrDefault( pair => pair.Value == new Entry( relative, node, directory ) );
        var id = existing.Key;
        if ( id is null )
        {
            if ( _references.Count >= 512 )
            {
                throw new InvalidDataException( "Reselect the root to refresh file references." );
            }
            id = Guid.NewGuid( ).ToString( "N" );
            _references.Add( id, new( relative, node, directory ) );
        }
        return new( name, id, directory, size );
    }

    private Entry Resolve( string? reference, bool directory )
    {
        if ( reference is null || !_references.TryGetValue( reference, out var entry ) || entry.Directory != directory )
        {
            throw new InvalidOperationException( "The file reference is unavailable. List the selected folder again." );
        }
        using var handle = OpenRelative( entry.Path, LinuxConstants.OPath );
        if ( !LinuxMountIdentity.TryGetNode( handle, out var node, out var isDirectory, out var links ) || isDirectory != directory || !directory && links != 1 )
        {
            throw new InvalidOperationException( "The selected file changed." );
        }
        if ( !directory )
        {
            node = LinuxMountIdentity.TryGet( handle, out var identity ) ? identity.ToString( ) : "";
        }
        if ( node != entry.Identity )
        {
            throw new InvalidOperationException( "The selected file changed. List the folder again." );
        }
        return entry;
    }

    private BrokerResult Read( string? reference, CancellationToken cancellationToken )
    {
        var entry = Resolve( reference, false );
        using var handle = OpenRelative( entry.Path, LinuxConstants.ONonBlocking );
        if ( !LinuxMountIdentity.TryGet( handle, out var before ) || before.ToString( ) != entry.Identity || before.Size > 128 * 1024 )
        {
            throw new InvalidOperationException( "The file changed or exceeds 128 KiB." );
        }
        var bytes = new byte[checked(( int ) before.Size)];
        var offset = 0;
        while ( offset < bytes.Length )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            var count = RandomAccess.Read( handle, bytes.AsSpan( offset ), offset );
            if ( count == 0 )
            {
                break;
            }
            offset += count;
        }
        ValidateRoot( );
        Resolve( reference, false );
        if ( offset != bytes.Length || !LinuxMountIdentity.TryGet( handle, out var after ) || before != after )
        {
            throw new InvalidOperationException( "The file changed while reading." );
        }
        return new( ExtensionBrokerOutcomes.Completed, Body: Convert.ToBase64String( bytes ) );
    }

    private void ValidateRoot( )
    {
        using var current = Open( LinuxConstants.AtCurrentDirectory, _path, LinuxConstants.OPath, LinuxConstants.ResolveNoSymlinks );
        if ( !LinuxMountIdentity.TryGetNode( current, out var identity, out _, out _ ) || identity != _identity ||
            File.ResolveLinkTarget( DescriptorPath( _root ), true )?.FullName != _path )
        {
            throw new InvalidOperationException( "The selected root moved or changed. Select it again." );
        }
    }
    private SafeFileHandle OpenRelative( string path, ulong flags )
    {
        if ( !_directory )
        {
            return Open( LinuxConstants.AtCurrentDirectory, _path, flags, LinuxConstants.ResolveNoSymlinks );
        }
        return Open( checked(( int ) _root.DangerousGetHandle( )), path, flags, LinuxConstants.ResolveNoMountTraversal | LinuxConstants.ResolveNoSymlinks | LinuxConstants.ResolveBeneath );
    }
    private static bool Protected( string path ) => path.Split( '/' ).Any( part => part.StartsWith( '.' ) ) ||
        ProtectedRoots.Any( root => path == root || path.StartsWith( root + "/", StringComparison.Ordinal ) ) ||
        AgentContextBroker.IsProtectedPath( path, Home ) || AgentContextBroker.IsProtectedPath( path, "/" );
    private static string DescriptorPath( SafeFileHandle handle ) => "/proc/self/fd/" + checked(( int ) handle.DangerousGetHandle( ));
    private static SafeFileHandle Open( int directory, string path, ulong flags, ulong resolve )
    {
        LinuxOpenHow how = new( )
        {
            Flags = flags | LinuxConstants.OCloseOnExec,
            Resolve = resolve
        };
        var fd = OpenAt2( LinuxConstants.OpenAt2, directory, path, ref how, ( nuint ) Unsafe.SizeOf<LinuxOpenHow>( ) );
        if ( fd < 0 )
        {
            throw new InvalidOperationException( "The selected path cannot be opened without links or mount traversal." );
        }
        return new SafeFileHandle( checked(( IntPtr ) fd), true );
    }
    public void Dispose( )
    {
        lock ( _gate )
        {
            _references.Clear( );
            _root.Dispose( );
        }
    }
    [LibraryImport( "libc", EntryPoint = "syscall", StringMarshalling = StringMarshalling.Utf8, SetLastError = true )]
    private static partial long OpenAt2( long number, int directory, string path, ref LinuxOpenHow how, nuint size );
}
