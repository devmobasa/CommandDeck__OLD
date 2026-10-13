using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CommandDeck;

internal sealed unsafe partial class ClipboardWaylandCapture
{
    [UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
    private static int Dispatch( nint implementation, nint target, uint opcode, nint message, WaylandArgument* arguments )
    {
        var host = ( ClipboardWaylandCapture? ) GCHandle.FromIntPtr( implementation ).Target;
        if ( host is null )
        {
            return -1;
        }

        try
        {
            host.OnEvent( target, opcode, arguments );
            return 0;
        }
        catch
        {
            host._failed = true;
            return -1;
        }
    }

    private void OnEvent( nint target, uint opcode, WaylandArgument* arguments )
    {
        if ( target == _registry )
        {
            OnRegistry( opcode, arguments );
        }
        else if ( target == _callback && opcode == 0 )
        {
            _syncDone = true;
        }
        else if ( target == _seat && opcode == 0 )
        {
            // Seat capabilities do not authorize primary-selection capture.
        }
        else if ( target == _device )
        {
            OnDevice( opcode, arguments );
        }
        else if ( _served is not null && target == _served.Source )
        {
            OnSource( _served, opcode, arguments );
        }
        else if ( _offers.TryGetValue( target, out var offer ) && opcode == 0 )
        {
            if ( offer.Selected || offer.Names.Count == 512 )
            {
                offer.Invalid = true;
                return;
            }

            var name = ReadAscii( arguments[0].Pointer );
            if ( name is null )
            {
                offer.Invalid = true;
            }
            else
            {
                offer.Names.Add( name );
            }
        }
        else
        {
            throw new InvalidDataException( "Unexpected clipboard Wayland event." );
        }
    }

    private void OnRegistry( uint opcode, WaylandArgument* arguments )
    {
        if ( opcode == 1 )
        {
            if ( arguments[0].Uint == _managerName || arguments[0].Uint == _seatName )
            {
                _failed = true;
            }

            return;
        }

        if ( opcode != 0 || ++_globals > 4096 )
        {
            throw new InvalidDataException( "Clipboard registry exceeds its bound." );
        }

        var name = ReadAscii( arguments[1].Pointer );
        if ( name is null || arguments[2].Uint == 0 )
        {
            throw new InvalidDataException( "Clipboard registry declaration is invalid." );
        }

        if ( name == "ext_data_control_manager_v1" )
        {
            if ( _managerName != 0 )
            {
                throw new InvalidDataException( "Clipboard registry has multiple managers." );
            }

            _managerName = arguments[0].Uint;
        }

        if ( name == "wl_seat" )
        {
            _seats++;
            if ( _seats != 1 )
            {
                _failed = true;
            }

            _seatName = arguments[0].Uint;
        }
    }

    private void OnDevice( uint opcode, WaylandArgument* arguments )
    {
        if ( opcode == 2 )
        {
            _failed = true;
            return;
        }

        var proxy = arguments[0].Pointer;
        if ( opcode == 0 )
        {
            if ( _offers.Count >= 8 )
            {
                throw new InvalidDataException( "Clipboard native offer capacity was exceeded." );
            }

            Attach( proxy );
            _offers.Add( proxy, new Offer( proxy ) );
            return;
        }

        if ( opcode is not (1 or 3) )
        {
            throw new InvalidDataException( "Unknown clipboard device event." );
        }

        if ( proxy == 0 )
        {
            return;
        }

        if ( !_offers.TryGetValue( proxy, out var offer ) || offer.Selected )
        {
            throw new InvalidDataException( "Clipboard selection refers to an invalid offer." );
        }

        var plan = ClipboardOfferPlan.Create( offer.Names, _consent );
        if ( opcode == 3 || !_armed || offer.Invalid || !plan.Accepted )
        {
            if ( opcode == 1 && _armed )
            {
                // Category only: the daemon can show why a selection was skipped without any content or names.
                QueueStatus( ClipboardCaptureStatus.SelectionRejected,
                    ( uint ) (offer.Invalid ? ClipboardInventoryRejection.InvalidInventory : plan.Rejection) );
            }

            RemoveOffer( offer, notify: false );
            return;
        }

        if ( _pending.Count == 4 )
        {
            RemoveOffer( _pending[0], notify: true );
        }

        offer.Selected = true;
        offer.Id = Guid.NewGuid( );
        offer.Started = Stopwatch.GetTimestamp( );
        _pending.Add( offer );
    }

    private static string? ReadAscii( nint pointer )
    {
        if ( pointer == 0 )
        {
            return null;
        }

        var bytes = ( byte* ) pointer;
        for ( var length = 0; length <= 256; length++ )
        {
            if ( bytes[length] == 0 )
            {
                return length == 0 ? null : Encoding.ASCII.GetString( new ReadOnlySpan<byte>( bytes, length ) );
            }

            if ( bytes[length] is < 32 or > 126 )
            {
                return null;
            }
        }

        return null;
    }

    private void EmitInventories( )
    {
        while ( events.Reader.Count < 8 )
        {
            ClipboardHelperFrame frame;
            if ( _statuses.TryDequeue( out var status ) )
            {
                frame = status;
            }
            else
            {
                var offer = _pending.FirstOrDefault( static offer => !offer.InventoryEnded );
                if ( offer is null )
                {
                    return;
                }

                if ( !offer.Emitted )
                {
                    frame = ClipboardCaptureProtocol.SelectionStart( epoch, offer.Id, offer.Names.Count );
                    offer.Emitted = true;
                }
                else if ( offer.NextMime < offer.Names.Count )
                {
                    frame = ClipboardCaptureProtocol.InventoryChunk( epoch, offer.Id, ++offer.Sequence, offer.Names, offer.NextMime );
                    offer.NextMime += Math.Min( 32, offer.Names.Count - offer.NextMime );
                }
                else
                {
                    frame = new( new( ClipboardHelperFrameType.InventoryEnd, epoch, offer.Id, -1, ++offer.Sequence, 0 ), ReadOnlyMemory<byte>.Empty );
                    offer.InventoryEnded = true;
                }
            }

            if ( !events.Writer.TryWrite( frame ) )
            {
                throw new IOException( "Clipboard event queue is closed." );
            }
        }
    }

    private void RemoveOffer( Offer offer, bool notify )
    {
        if ( _receive?.Owner == offer )
        {
            CloseReceive( );
        }

        _offers.Remove( offer.Proxy );
        _pending.Remove( offer );
        WaylandNative.MarshalArray( offer.Proxy, 1, 0, 1, 1, null );
        if ( notify && offer.Emitted )
        {
            QueueStatus( ClipboardCaptureStatus.SelectionCancelled, selection: offer.Id );
        }
    }

    private sealed class Offer( nint proxy )
    {
        internal nint Proxy { get; } = proxy;
        internal List<string> Names { get; } = [];
        internal bool Invalid { get; set; }
        internal bool Selected { get; set; }
        internal Guid Id { get; set; }
        internal long Started { get; set; }
        internal bool Emitted { get; set; }
        internal bool InventoryEnded { get; set; }
        internal int NextMime { get; set; }
        internal uint Sequence { get; set; }
        internal int ReceivedOffers { get; set; }
    }
}
