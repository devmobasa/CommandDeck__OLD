using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CommandDeck;

internal sealed unsafe partial class ClipboardWaylandCapture
{
    private Receive? _receive;

    private void BeginReceive( ClipboardHelperFrame frame )
    {
        var offer = _pending.FirstOrDefault( offer => offer.Id == frame.Header.SelectionId );
        if ( !_armed || _callback != 0 || _receive is not null || offer is null || !offer.InventoryEnded ||
            frame.Payload.Length != 0 || frame.Header.Sequence != offer.ReceivedOffers )
        {
            throw new InvalidDataException( "Clipboard receive is unavailable in this state." );
        }

        var plan = ClipboardOfferPlan.Create( offer.Names, _consent );
        if ( !plan.Accepted || offer.ReceivedOffers >= plan.Offers.Count ||
            plan.Offers[offer.ReceivedOffers].InventoryIndex != frame.Header.OfferIndex )
        {
            throw new InvalidDataException( "Clipboard receive does not match the admitted inventory order." );
        }

        var planned = plan.Offers[offer.ReceivedOffers];
        int* pipes = stackalloc int[2];
        if ( WaylandNative.Pipe( pipes, 0x80000 ) != 0 )
        {
            throw new IOException( "Clipboard receive pipe could not be created." );
        }

        if ( WaylandNative.SetFlags( pipes[0], 4, 0x800 ) != 0 )
        {
            _ = WaylandNative.Close( pipes[0] );
            _ = WaylandNative.Close( pipes[1] );
            throw new IOException( "Clipboard receive pipe could not be made nonblocking." );
        }

        var mime = Marshal.StringToCoTaskMemUTF8( planned.RawName );
        try
        {
            WaylandArgument* arguments = stackalloc WaylandArgument[2];
            arguments[0].Pointer = mime;
            arguments[1].Int = pipes[1];
            WaylandNative.MarshalArray( offer.Proxy, 0, 0, 1, 0, arguments );
            _receive = new( offer, planned, pipes[0], plan.Offers.Count );
        }
        catch
        {
            _ = WaylandNative.Close( pipes[0] );
            throw;
        }
        finally
        {
            Marshal.FreeCoTaskMem( mime );
            _ = WaylandNative.Close( pipes[1] );
        }
    }

    private void ReceiveContent( )
    {
        var receive = _receive;
        if ( receive is null )
        {
            return;
        }

        if ( events.Reader.Count >= ClipboardHelperFraming.MaximumQueuedFrames )
        {
            return;
        }

        if ( receive.Ended )
        {
            EmitReceiveEnd( receive );
            return;
        }

        PollDescriptor descriptor = new( )
        {
            Fd = receive.Fd,
            Events = 1
        };
        var result = WaylandNative.Poll( &descriptor, 1, 0 );
        if ( result < 0 && Marshal.GetLastPInvokeError( ) == 4 )
        {
            return;
        }

        // Expire a stalled source, not a progressing transfer or our own output backpressure.
        // Check readiness first so scheduling delays cannot discard bytes already waiting.
        if ( result == 0 )
        {
            if ( Stopwatch.GetElapsedTime( receive.LastProgress ) >= ReceiveIdleTimeout )
            {
                RemoveOffer( receive.Owner, notify: true );
            }

            return;
        }

        if ( result < 0 || (descriptor.ReturnedEvents & (8 | 32)) != 0 )
        {
            RemoveOffer( receive.Owner, notify: true );
            return;
        }

        var remaining = receive.Plan.MaximumBytes - receive.Bytes;
        if ( remaining == 0 )
        {
            if ( (descriptor.ReturnedEvents & 1) != 0 )
            {
                RemoveOffer( receive.Owner, notify: true );
            }
            else if ( (descriptor.ReturnedEvents & 16) != 0 )
            {
                FinishRead( receive );
            }

            return;
        }

        var count = receive.HeaderValidated ? Math.Min( remaining, ClipboardHelperHeader.MaximumPayload ) : 1;
        var bytes = new byte[count];
        nint read;
        fixed ( byte* pointer = bytes )
        {
            read = WaylandNative.Read( receive.Fd, pointer, ( nuint ) count );
        }

        if ( read < 0 )
        {
            if ( Marshal.GetLastPInvokeError( ) is not (4 or 11) )
            {
                RemoveOffer( receive.Owner, notify: true );
            }

            return;
        }

        if ( read == 0 )
        {
            if ( !receive.HeaderValidated )
            {
                RemoveOffer( receive.Owner, notify: true );
            }
            else
            {
                FinishRead( receive );
            }

            return;
        }

        receive.LastProgress = Stopwatch.GetTimestamp( );
        receive.Bytes += ( int ) read;
        if ( !receive.HeaderValidated )
        {
            ReadGnomeHeader( receive, bytes[0] );
            return;
        }

        EmitPayload( receive, bytes.AsMemory( 0, ( int ) read ) );
    }

    private void ReadGnomeHeader( Receive receive, byte value )
    {
        receive.Header[receive.Bytes - 1] = value;
        if ( value != ( byte ) '\n' )
        {
            if ( receive.Bytes == 8 )
            {
                RemoveOffer( receive.Owner, notify: true );
            }

            return;
        }

        var header = receive.Header.AsMemory( 0, receive.Bytes );
        if ( !header.Span.SequenceEqual( "copy\n"u8 ) && !header.Span.SequenceEqual( "copy\r\n"u8 ) )
        {
            RemoveOffer( receive.Owner, notify: true );
            return;
        }

        receive.HeaderValidated = true;
        EmitPayload( receive, header );
        if ( receive.Plan.HeaderOnly )
        {
            FinishRead( receive );
        }
    }

    private void EmitPayload( Receive receive, ReadOnlyMemory<byte> payload ) => EmitReceiveFrame(
        new( new( ClipboardHelperFrameType.OfferChunk, epoch, receive.Owner.Id, receive.Plan.InventoryIndex,
            ++receive.Owner.Sequence, payload.Length ), payload ) );

    private static void FinishRead( Receive receive )
    {
        _ = WaylandNative.Close( receive.Fd );
        receive.Fd = -1;
        receive.Ended = true;
    }

    private void EmitReceiveEnd( Receive receive )
    {
        if ( !receive.EndEmitted )
        {
            var bytes = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian( bytes, ( ulong ) receive.Bytes );
            EmitReceiveFrame( new( new( ClipboardHelperFrameType.OfferEnd, epoch, receive.Owner.Id,
                receive.Plan.InventoryIndex, ++receive.Owner.Sequence, 8 ), bytes ) );
            receive.EndEmitted = true;
            receive.Owner.ReceivedOffers++;
            if ( receive.Owner.ReceivedOffers < receive.OfferCount )
            {
                receive.Owner.Started = Stopwatch.GetTimestamp( );
                CloseReceive( );
            }

            return;
        }

        var count = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian( count, ( uint ) receive.OfferCount );
        EmitReceiveFrame( new( new( ClipboardHelperFrameType.SelectionEnd, epoch, receive.Owner.Id,
            -1, ++receive.Owner.Sequence, 4 ), count ) );
        RemoveOffer( receive.Owner, notify: false );
    }

    private void EmitReceiveFrame( ClipboardHelperFrame frame )
    {
        if ( !events.Writer.TryWrite( frame ) )
        {
            throw new IOException( "Clipboard receive event queue is closed." );
        }
    }

    private void CloseReceive( )
    {
        if ( _receive is { Fd: >= 0 } receive )
        {
            _ = WaylandNative.Close( receive.Fd );
        }

        _receive = null;
    }

    private sealed class Receive( Offer owner, ClipboardPlannedOffer plan, int fd, int offerCount )
    {
        internal Offer Owner { get; } = owner;
        internal ClipboardPlannedOffer Plan { get; } = plan;
        internal int Fd { get; set; } = fd;
        internal int OfferCount { get; } = offerCount;
        internal long Started { get; } = Stopwatch.GetTimestamp( );
        internal long LastProgress { get; set; } = Stopwatch.GetTimestamp( );
        internal int Bytes { get; set; }
        internal byte[] Header { get; } = new byte[8];
        internal bool HeaderValidated { get; set; } = plan.Kind != ClipboardOfferKind.GnomeCopiedFiles;
        internal bool Ended { get; set; }
        internal bool EndEmitted { get; set; }
    }
}
