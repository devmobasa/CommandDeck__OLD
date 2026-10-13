using System.Runtime.InteropServices;

namespace CommandDeck;

internal static unsafe class WaylandDisplayPump
{
    // A negative timeout waits until the display, the receive descriptor or the wake signal is ready.
    internal static void Dispatch( nint display, int receiveDescriptor = -1, int timeoutMilliseconds = 20, WaylandWakeSignal? wake = null )
    {
        if ( WaylandNative.PrepareRead( display ) != 0 )
        {
            DispatchPending( display );
            return;
        }

        var prepared = true;
        try
        {
            var flush = WaylandNative.Flush( display );
            if ( flush < 0 && Marshal.GetLastPInvokeError( ) != 11 )
            {
                throw new IOException( "Clipboard Wayland request flush failed." );
            }

            PollDescriptor* descriptors = stackalloc PollDescriptor[3];
            descriptors[0] = new( )
            {
                Fd = WaylandNative.GetFd( display ),
                Events = ( short ) (1 | (flush < 0 ? 4 : 0))
            };
            descriptors[1] = new( )
            {
                Fd = receiveDescriptor,
                Events = 1
            };
            descriptors[2] = new( )
            {
                Fd = wake?.Descriptor ?? -1,
                Events = 1
            };
            var polled = WaylandNative.Poll( descriptors, 3, timeoutMilliseconds );
            var descriptor = descriptors[0];
            if ( polled < 0 && Marshal.GetLastPInvokeError( ) != 4 || (descriptor.ReturnedEvents & (8 | 16 | 32)) != 0 )
            {
                throw new IOException( "Clipboard Wayland connection closed." );
            }

            // Drained before the caller's next pass reads its queues, so a later signal is never lost.
            if ( polled > 0 && (descriptors[2].ReturnedEvents & 1) != 0 )
            {
                wake!.Drain( );
            }

            if ( polled > 0 && (descriptor.ReturnedEvents & 1) != 0 )
            {
                prepared = false;
                if ( WaylandNative.ReadEvents( display ) < 0 )
                {
                    throw new IOException( "Clipboard Wayland event read failed." );
                }
            }
        }
        finally
        {
            if ( prepared )
            {
                WaylandNative.CancelRead( display );
            }
        }

        DispatchPending( display );
    }

    private static void DispatchPending( nint display )
    {
        if ( WaylandNative.DispatchPending( display ) < 0 )
        {
            throw new IOException( "Clipboard Wayland event dispatch failed." );
        }
    }
}
