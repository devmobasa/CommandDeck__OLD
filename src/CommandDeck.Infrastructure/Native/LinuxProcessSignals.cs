using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CommandDeck;

// A process pinned through a pidfd. A signal sent through it reaches the
// process that held the PID when it was pinned, or nothing once that process
// has exited; a later process that reuses the PID is never signalled.
public interface IPinnedProcess : IDisposable
{
    // False when the pinned process has already exited.
    bool Signal( int signal );
}

// Pins the process that holds a PID now; null when no process holds it.
public delegate IPinnedProcess? ProcessPinner( int pid );

internal static partial class LinuxProcessSignals
{
    public const int Terminate = 15;
    public const int Kill = 9;
    private const int NoSuchProcess = 3;

    public static IPinnedProcess? Pin( int pid )
    {
        var descriptor = PidfdOpen( LinuxConstants.PidfdOpen, pid, 0 );
        if ( descriptor < 0 )
        {
            var error = Marshal.GetLastPInvokeError( );
            return error == NoSuchProcess
                ? null
                : throw new InvalidOperationException( $"Process {pid} could not be pinned for a signal (errno {error})." );
        }

        return new Pidfd( new SafeFileHandle( checked(( IntPtr ) descriptor), ownsHandle: true ) );
    }

    public static uint CurrentUserId( ) => GetUserId( );

    private sealed class Pidfd( SafeFileHandle handle ) : IPinnedProcess
    {
        public bool Signal( int signal )
        {
            var added = false;
            long result;
            try
            {
                handle.DangerousAddRef( ref added );
                result = PidfdSendSignal( LinuxConstants.PidfdSendSignal, handle.DangerousGetHandle( ), signal, 0, 0 );
            }
            finally
            {
                if ( added )
                {
                    handle.DangerousRelease( );
                }
            }

            if ( result == 0 )
            {
                return true;
            }

            var error = Marshal.GetLastPInvokeError( );
            return error == NoSuchProcess
                ? false
                : throw new InvalidOperationException( $"The signal could not be sent (errno {error})." );
        }

        public void Dispose( ) => handle.Dispose( );
    }

    // syscall(2) is variadic; every argument is passed as a full register.
    [LibraryImport( "libc", EntryPoint = "syscall", SetLastError = true )]
    private static partial long PidfdOpen( long number, long pid, long flags );

    [LibraryImport( "libc", EntryPoint = "syscall", SetLastError = true )]
    private static partial long PidfdSendSignal( long number, nint descriptor, long signal, nint info, long flags );

    [LibraryImport( "libc", EntryPoint = "getuid" )]
    private static partial uint GetUserId( );
}
