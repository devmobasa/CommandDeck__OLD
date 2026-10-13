using System.Runtime.InteropServices;

namespace CommandDeck;

internal static class LinuxDirectoryDurability
{
    private const int OpenReadOnly = 0;

    public static void FlushParent( string path )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var directory = Path.GetDirectoryName( path );
        if ( string.IsNullOrEmpty( directory ) )
        {
            return;
        }

        var descriptor = Open( directory, OpenReadOnly | LinuxConstants.ODirectory | LinuxConstants.OCloseOnExec );
        if ( descriptor < 0 )
        {
            throw new IOException(
                $"Could not open journal directory for durability: errno {Marshal.GetLastPInvokeError( )}." );
        }

        try
        {
            if ( Fsync( descriptor ) != 0 )
            {
                throw new IOException(
                    $"Could not flush journal directory: errno {Marshal.GetLastPInvokeError( )}." );
            }
        }
        finally
        {
            _ = Close( descriptor );
        }
    }

    [DllImport(
        "libc",
        EntryPoint = "open",
        CharSet = CharSet.Ansi,
        BestFitMapping = false,
        ThrowOnUnmappableChar = true,
        SetLastError = true )]
    private static extern int Open( string path, int flags );

    [DllImport( "libc", EntryPoint = "fsync", SetLastError = true )]
    private static extern int Fsync( int descriptor );

    [DllImport( "libc", EntryPoint = "close" )]
    private static extern int Close( int descriptor );
}
