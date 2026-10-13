using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CommandDeck;

public sealed partial class ClipboardPayloadDirectory
{
    private const int DirectoryFlags = 0x10000 | 0x20000 | 0x80000;
    private const int FileFlags = 0x800 | 0x20000 | 0x80000;

    [LibraryImport( "libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true )]
    private static partial int OpenNative( string path, int flags );
    [LibraryImport( "libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true )]
    private static partial int OpenAt( SafeFileHandle directory, string name, int flags, uint mode );
    [LibraryImport( "libc", EntryPoint = "mkdirat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true )]
    private static partial int MakeDirectoryAt( SafeFileHandle directory, string name, uint mode );
    [LibraryImport( "libc", EntryPoint = "renameat2", StringMarshalling = StringMarshalling.Utf8, SetLastError = true )]
    private static partial int RenameAt( SafeFileHandle source, string sourceName, SafeFileHandle destination, string destinationName, uint flags );
    [LibraryImport( "libc", EntryPoint = "unlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true )]
    private static partial int UnlinkAt( SafeFileHandle directory, string name, int flags );
    [LibraryImport( "libc", EntryPoint = "fsync", SetLastError = true )]
    private static partial int FlushNative( SafeFileHandle descriptor );

    private static SafeFileHandle OpenDirectory( string path )
    {
        var components = System.IO.Path.GetFullPath( path ).Split( '/', StringSplitOptions.RemoveEmptyEntries );
        if ( components.Length == 0 )
        {
            throw new IOException( "Clipboard payload directory cannot be the filesystem root." );
        }

        SafeFileHandle current = new( OpenNative( "/", DirectoryFlags ), true );
        try
        {
            for ( var index = 0; index < components.Length; index++ )
            {
                var final = index == components.Length - 1;
                var created = false;
                if ( final )
                {
                    created = MakeDirectoryAt( current, components[index], 0x1c0 ) == 0;
                    if ( !created && Marshal.GetLastPInvokeError( ) != 17 )
                    {
                        throw new IOException( "Clipboard payload directory could not be created." );
                    }
                }

                var descriptor = OpenAt( current, components[index], DirectoryFlags, 0 );
                if ( descriptor < 0 )
                {
                    throw new IOException( "Clipboard payload directory cannot contain symbolic links or missing parents." );
                }

                SafeFileHandle next = new( descriptor, true );
                if ( created && FlushNative( current ) != 0 )
                {
                    next.Dispose( );
                    throw new IOException( "Clipboard payload directory creation could not be made durable." );
                }

                current.Dispose( );
                current = next;
            }

            RequirePrivateNode( current, true );
            return current;
        }
        catch
        {
            current.Dispose( );
            throw;
        }
    }

    private static string RequirePrivateNode( SafeFileHandle handle, bool expectedDirectory )
    {
        if ( !LinuxMountIdentity.TryGetNode( handle, out var identity, out var directory, out var links ) ||
            directory != expectedDirectory || !directory && links != 1 ||
            !LinuxMountIdentity.TryGetOwnership( handle, out var user, out var mode ) ||
            user != LinuxMountIdentity.CurrentEffectiveUserId( ) || mode != (expectedDirectory ? 0x1c0 : 0x180) )
        {
            throw new IOException( "Clipboard payload node is not an owned private directory or single-link regular file." );
        }

        return identity;
    }
}
