using System.Runtime.InteropServices;

namespace CommandDeck;

/// <summary>An eventfd that other threads signal to end the Wayland thread's poll early.</summary>
/// <remarks>Dispose it only after every signalling thread has stopped.</remarks>
internal sealed unsafe class WaylandWakeSignal : IDisposable
{
    private const int CloseOnExec = 0x80000;
    private const int NonBlocking = 0x800;
    private int _descriptor;

    internal WaylandWakeSignal( )
    {
        _descriptor = WaylandNative.EventFd( 0, CloseOnExec | NonBlocking );
        if ( _descriptor < 0 )
        {
            throw new IOException( "Clipboard wake descriptor could not be created." );
        }
    }

    internal int Descriptor => _descriptor;

    // EAGAIN means the counter is already signalled; an interrupted write must be retried.
    internal void Signal( )
    {
        ulong increment = 1;
        while ( WaylandNative.Write( _descriptor, ( byte* ) &increment, sizeof( ulong ) ) < 0 )
        {
            var error = Marshal.GetLastPInvokeError( );
            if ( error == 11 )
            {
                return;
            }

            if ( error != 4 )
            {
                throw new IOException( "Clipboard wake descriptor could not be signalled." );
            }
        }
    }

    // Resets the counter; reading an unsignalled descriptor fails with EAGAIN and changes nothing.
    internal void Drain( )
    {
        ulong count;
        while ( WaylandNative.Read( _descriptor, ( byte* ) &count, sizeof( ulong ) ) < 0 )
        {
            var error = Marshal.GetLastPInvokeError( );
            if ( error == 11 )
            {
                return;
            }

            if ( error != 4 )
            {
                throw new IOException( "Clipboard wake descriptor could not be drained." );
            }
        }
    }

    public void Dispose( )
    {
        var descriptor = Interlocked.Exchange( ref _descriptor, -1 );
        if ( descriptor >= 0 )
        {
            _ = WaylandNative.Close( descriptor );
        }
    }
}
