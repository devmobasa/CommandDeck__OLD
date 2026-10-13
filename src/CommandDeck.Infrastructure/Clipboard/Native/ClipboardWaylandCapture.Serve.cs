using System.Runtime.InteropServices;

namespace CommandDeck;

internal sealed unsafe partial class ClipboardWaylandCapture
{
    private const int MaximumConcurrentSends = 8;
    private Write? _write;
    private readonly Queue<Guid> _cancelledWrites = new( );
    private Served? _served;
    private int _activeSends;

    private bool TryHandleWrite( ClipboardHelperFrame frame )
    {
        var type = frame.Header.Type;
        if ( type is not (ClipboardHelperFrameType.WriteStart or ClipboardHelperFrameType.WriteChunk or ClipboardHelperFrameType.WriteEnd) )
        {
            return false;
        }

        if ( !_ready )
        {
            throw new InvalidDataException( "Clipboard serve command arrived before the host was ready." );
        }

        // A cancellation on the independent control pipe may overtake queued data frames of the same write.
        if ( _cancelledWrites.Contains( frame.Header.SelectionId ) )
        {
            return true;
        }

        if ( type == ClipboardHelperFrameType.WriteStart )
        {
            if ( _write is not null )
            {
                throw new InvalidDataException( "Clipboard serve manifest arrived during another write." );
            }

            _write = new( frame.Header.SelectionId, ClipboardServeProtocol.ReadWriteStart( frame ) );
            return true;
        }

        var write = _write;
        if ( write is null || write.Id != frame.Header.SelectionId || frame.Header.Sequence != write.NextSequence )
        {
            throw new InvalidDataException( "Clipboard serve frame does not continue the pending write." );
        }

        write.NextSequence++;
        if ( type == ClipboardHelperFrameType.WriteChunk )
        {
            write.Accept( frame.Header.OfferIndex, frame.Payload.Span );
            return true;
        }

        if ( ClipboardServeProtocol.ReadWriteEnd( frame ) != write.Manifest.Count || !write.Complete )
        {
            throw new InvalidDataException( "Clipboard serve write ended before every offer completed." );
        }

        _write = null;
        Serve( write );
        return true;
    }

    private void Serve( Write write )
    {
        WaylandArgument argument = default;
        var source = WaylandNative.MarshalArray( _manager, 0, ClipboardWaylandInterfaces.Get( "ext_data_control_source_v1" ), 1, 0, &argument );
        Attach( source );
        Served served = new( write.Id, source, write.Manifest, write.Buffers );
        try
        {
            foreach ( var mime in write.Manifest.Select( static entry => entry.Mime ).Append( ClipboardOfferPlan.OwnerMime ) )
            {
                var name = Marshal.StringToCoTaskMemUTF8( mime );
                try
                {
                    argument.Pointer = name;
                    WaylandNative.MarshalArray( source, 0, 0, 1, 0, &argument );
                }
                finally
                {
                    Marshal.FreeCoTaskMem( name );
                }
            }

            argument.Pointer = source;
            WaylandNative.MarshalArray( _device, 0, 0, 1, 0, &argument );
        }
        catch
        {
            WaylandNative.MarshalArray( source, 1, 0, 1, 1, null );
            throw;
        }

        // The compositor cancels the previous source after the new selection is set; destroying it now is idempotent.
        var previous = _served;
        _served = served;
        if ( previous is not null )
        {
            WaylandNative.MarshalArray( previous.Source, 1, 0, 1, 1, null );
        }

    }

    // Ownership is acknowledged only after the compositor has processed set_selection, proven by a display roundtrip.
    private void BeginServingAcknowledgement( )
    {
        if ( _served is { Acknowledged: false, SyncPending: false } served && _callback == 0 )
        {
            served.SyncPending = true;
            Sync( SyncPhase.Serve );
        }
    }

    private void AcknowledgeServing( )
    {
        if ( _served is { Acknowledged: false } served )
        {
            served.SyncPending = false;
            served.Acknowledged = true;
            QueueStatus( ClipboardCaptureStatus.Serving, selection: served.Id );
        }
    }

    private void OnSource( Served served, uint opcode, WaylandArgument* arguments )
    {
        if ( opcode == 1 )
        {
            if ( _served == served )
            {
                _served = null;
                QueueStatus( ClipboardCaptureStatus.ServingEnded, selection: served.Id );
            }

            WaylandNative.MarshalArray( served.Source, 1, 0, 1, 1, null );
            return;
        }

        if ( opcode != 0 )
        {
            throw new InvalidDataException( "Unknown clipboard source event." );
        }

        var fd = arguments[1].Int;
        var mime = ReadAscii( arguments[0].Pointer );
        var index = mime is null ? -1 : served.IndexOf( mime );
        if ( index < 0 || _served != served || Volatile.Read( ref _activeSends ) >= MaximumConcurrentSends ||
            WaylandNative.SetFlags( fd, 4, 0x800 ) != 0 )
        {
            _ = WaylandNative.Close( fd );
            return;
        }

        Interlocked.Increment( ref _activeSends );
        var bytes = served.Buffers[index];
        Thread sender = new( ( ) => SendBytes( fd, bytes ) )
        {
            IsBackground = true,
            Name = "clipboard-serve"
        };
        sender.Start( );
    }

    private void SendBytes( int fd, byte[] bytes )
    {
        try
        {
            var deadline = Environment.TickCount64 + 30_000;
            var offset = 0;
            while ( offset < bytes.Length && Environment.TickCount64 < deadline )
            {
                PollDescriptor descriptor = new( )
                {
                    Fd = fd,
                    Events = 4
                };
                var ready = WaylandNative.Poll( &descriptor, 1, 1000 );
                if ( ready < 0 && Marshal.GetLastPInvokeError( ) == 4 || ready == 0 )
                {
                    continue;
                }

                if ( ready < 0 || (descriptor.ReturnedEvents & (8 | 32)) != 0 )
                {
                    return;
                }

                nint written;
                fixed ( byte* pointer = bytes )
                {
                    written = WaylandNative.Write( fd, pointer + offset, ( nuint ) Math.Min( bytes.Length - offset, 65536 ) );
                }

                if ( written < 0 )
                {
                    if ( Marshal.GetLastPInvokeError( ) is not (4 or 11) )
                    {
                        return;
                    }

                    continue;
                }

                offset += ( int ) written;
            }
        }
        finally
        {
            _ = WaylandNative.Close( fd );
            Interlocked.Decrement( ref _activeSends );
        }
    }

    private void CancelWrite( Guid writeId )
    {
        if ( _write?.Id == writeId )
        {
            _write = null;
        }

        if ( _cancelledWrites.Count == 8 )
        {
            _cancelledWrites.Dequeue( );
        }

        _cancelledWrites.Enqueue( writeId );
    }

    private void DisposeServing( )
    {
        if ( _served is not null )
        {
            WaylandNative.MarshalArray( _served.Source, 1, 0, 1, 1, null );
            _served = null;
        }

        _write = null;
    }

    private sealed class Write( Guid id, IReadOnlyList<ClipboardServeManifestEntry> manifest )
    {
        internal Guid Id { get; } = id;
        internal IReadOnlyList<ClipboardServeManifestEntry> Manifest { get; } = manifest;
        internal byte[][] Buffers { get; } = manifest.Select( static entry => new byte[entry.Length] ).ToArray( );
        private readonly long[] _received = new long[manifest.Count];
        internal uint NextSequence { get; set; } = 1;
        internal bool Complete => _received.Select( ( count, index ) => count == Manifest[index].Length ).All( static complete => complete );

        internal void Accept( int offerIndex, ReadOnlySpan<byte> bytes )
        {
            if ( offerIndex < 0 || offerIndex >= Manifest.Count || bytes.Length == 0 || _received[offerIndex] + bytes.Length > Manifest[offerIndex].Length )
            {
                throw new InvalidDataException( "Clipboard serve chunk exceeds its declared offer." );
            }

            bytes.CopyTo( Buffers[offerIndex].AsSpan( ( int ) _received[offerIndex] ) );
            _received[offerIndex] += bytes.Length;
        }
    }

    private sealed class Served( Guid id, nint source, IReadOnlyList<ClipboardServeManifestEntry> manifest, byte[][] buffers )
    {
        internal Guid Id { get; } = id;
        internal nint Source { get; } = source;
        internal byte[][] Buffers { get; } = buffers;
        internal bool Acknowledged { get; set; }
        internal bool SyncPending { get; set; }
        private readonly IReadOnlyList<ClipboardServeManifestEntry> _manifest = manifest;

        internal int IndexOf( string mime )
        {
            for ( var index = 0; index < _manifest.Count; index++ )
            {
                if ( string.Equals( _manifest[index].Mime, mime, StringComparison.Ordinal ) )
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
