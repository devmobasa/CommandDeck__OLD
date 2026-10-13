using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CommandDeck;

internal static class LinuxFileLease
{
    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;

    public static bool TryAcquire( SafeFileHandle handle )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return true;
        }

        return Flock(
            handle.DangerousGetHandle( ).ToInt32( ),
            LockExclusive | LockNonBlocking ) == 0;
    }

    [DllImport( "libc", EntryPoint = "flock", SetLastError = true )]
    private static extern int Flock( int descriptor, int operation );
}
