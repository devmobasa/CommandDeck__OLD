using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace CommandDeck;

internal sealed unsafe partial class ClipboardWaylandCapture(
    ulong epoch, ChannelReader<ClipboardHelperFrame> commands, ChannelReader<ClipboardHelperFrame> controls,
    Channel<ClipboardHelperFrame> events, ChannelWriter<ClipboardHelperFrame> acknowledgements, WaylandWakeSignal wake ) : IDisposable
{
    private static readonly TimeSpan OfferLifetime = TimeSpan.FromSeconds( 5 );
    private static readonly TimeSpan ReceiveLifetime = TimeSpan.FromSeconds( 2 );
    private static readonly TimeSpan SyncLifetime = TimeSpan.FromSeconds( 2 );
    private nint _display;
    private nint _registry;
    private nint _manager;
    private nint _seat;
    private nint _device;
    private nint _callback;
    private GCHandle _self;
    private uint _managerName;
    private uint _seatName;
    private int _seats;
    private int _globals;
    private bool _failed;
    private bool _syncDone;
    private bool _ready;
    private bool _armed;
    private bool _stopped;
    private ClipboardConsent _consent;
    private uint _configurationSequence;
    private SyncPhase _syncPhase;
    private long _syncStarted;
    private readonly Dictionary<nint, Offer> _offers = [];
    private readonly List<Offer> _pending = [];
    private readonly Queue<ClipboardHelperFrame> _statuses = [];

    internal void Run( string? displayName, CancellationToken cancellationToken )
    {
        if ( epoch == 0 || _self.IsAllocated )
        {
            throw new InvalidOperationException( "Clipboard capture host cannot be reused or have a zero epoch." );
        }

        _self = GCHandle.Alloc( this );
        _display = WaylandNative.Connect( displayName );
        if ( _display == 0 )
        {
            throw new IOException( "Clipboard Wayland display is unavailable." );
        }

        WaylandArgument argument = default;
        _registry = WaylandNative.MarshalArray( _display, 1, ClipboardWaylandInterfaces.Get( "wl_registry" ), 1, 0, &argument );
        Attach( _registry );
        Sync( SyncPhase.Discover );
        while ( !_stopped )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            if ( _failed )
            {
                throw new IOException( "Clipboard Wayland protocol failed." );
            }

            AdvanceSync( );
            BeginServingAcknowledgement( );
            ReadControls( );
            if ( _stopped )
            {
                return;
            }

            ReadCommands( );
            foreach ( var offer in _pending.Where( static offer => Stopwatch.GetElapsedTime( offer.Started ) >= OfferLifetime ).ToArray( ) )
            {
                RemoveOffer( offer, notify: true );
            }

            EmitInventories( );
            for ( var index = 0; index < ClipboardHelperFraming.MaximumQueuedFrames; index++ )
            {
                ReceiveContent( );
            }
            var receiveDescriptor = events.Reader.Count < ClipboardHelperFraming.MaximumQueuedFrames ? _receive?.Fd ?? -1 : -1;
            WaylandDisplayPump.Dispatch( _display, receiveDescriptor, PollTimeout( ), wake );
        }
    }

    // Commands, controls, freed event capacity and cancellation arrive through the wake signal, and
    // compositor events and offer bytes through their descriptors, so only deadlines need a timeout.
    private int PollTimeout( )
    {
        var eventRoom = events.Reader.Count < ClipboardHelperFraming.MaximumQueuedFrames;
        var queuedOutput = _statuses.Count > 0 || _receive is { Ended: true } || _pending.Any( static offer => !offer.InventoryEnded );
        var servingNeedsSync = _served is { Acknowledged: false, SyncPending: false } && _callback == 0;
        if ( eventRoom && queuedOutput || servingNeedsSync )
        {
            return 0;
        }

        TimeSpan? next = null;
        foreach ( var offer in _pending )
        {
            next = Earliest( next, OfferLifetime - Stopwatch.GetElapsedTime( offer.Started ) );
        }

        if ( _receive is not null )
        {
            next = Earliest( next, ReceiveLifetime - Stopwatch.GetElapsedTime( _receive.Started ) );
        }

        if ( _callback != 0 )
        {
            next = Earliest( next, SyncLifetime - Stopwatch.GetElapsedTime( _syncStarted ) );
        }

        return next is { } remaining ? ( int ) Math.Clamp( Math.Ceiling( remaining.TotalMilliseconds ), 0, int.MaxValue ) : -1;

        static TimeSpan Earliest( TimeSpan? current, TimeSpan candidate ) => current is { } value && value < candidate ? value : candidate;
    }

    private void Attach( nint proxy )
    {
        if ( proxy == 0 || WaylandNative.AddDispatcher( proxy, &Dispatch, GCHandle.ToIntPtr( _self ), 0 ) != 0 )
        {
            throw new IOException( "Clipboard Wayland dispatcher could not be installed." );
        }
    }

    private nint Bind( uint name, string type )
    {
        var nativeType = ClipboardWaylandInterfaces.Get( type );
        WaylandArgument* arguments = stackalloc WaylandArgument[4];
        arguments[0].Uint = name;
        arguments[1].Pointer = (( WaylandInterface* ) nativeType)->Name;
        arguments[2].Uint = 1;
        arguments[3].Pointer = 0;
        var proxy = WaylandNative.MarshalArray( _registry, 0, nativeType, 1, 0, arguments );
        Attach( proxy );
        return proxy;
    }

    private void Sync( SyncPhase phase )
    {
        if ( _callback != 0 )
        {
            throw new IOException( "Clipboard synchronization is already pending." );
        }

        WaylandArgument argument = default;
        _callback = WaylandNative.MarshalArray( _display, 0, ClipboardWaylandInterfaces.Get( "wl_callback" ), 1, 0, &argument );
        Attach( _callback );
        _syncPhase = phase;
        _syncStarted = Stopwatch.GetTimestamp( );
    }

    private void AdvanceSync( )
    {
        if ( !_syncDone )
        {
            if ( _callback != 0 && Stopwatch.GetElapsedTime( _syncStarted ) >= SyncLifetime )
            {
                throw new TimeoutException( "Clipboard Wayland synchronization timed out." );
            }

            return;
        }

        _syncDone = false;
        WaylandNative.DestroyProxy( _callback );
        _callback = 0;
        if ( _syncPhase == SyncPhase.Discover )
        {
            if ( _managerName == 0 || _seats != 1 )
            {
                throw new IOException( "Clipboard capture requires ext-data-control and exactly one advertised seat." );
            }

            _manager = Bind( _managerName, "ext_data_control_manager_v1" );
            _seat = Bind( _seatName, "wl_seat" );
            WaylandArgument* arguments = stackalloc WaylandArgument[2];
            arguments[0].Pointer = 0;
            arguments[1].Pointer = _seat;
            _device = WaylandNative.MarshalArray( _manager, 1, ClipboardWaylandInterfaces.Get( "ext_data_control_device_v1" ), 1, 0, arguments );
            Attach( _device );
            Sync( SyncPhase.Initial );
            return;
        }

        if ( _syncPhase != SyncPhase.Serve )
        {
            foreach ( var offer in _offers.Values.ToArray( ) )
            {
                RemoveOffer( offer, notify: false );
            }
        }

        if ( _syncPhase == SyncPhase.Initial )
        {
            _ready = true;
            QueueStatus( ClipboardCaptureStatus.Ready );
        }
        else if ( _syncPhase == SyncPhase.Serve )
        {
            AcknowledgeServing( );
        }
        else
        {
            _armed = _consent != ClipboardConsent.None;
            QueueStatus( ClipboardCaptureStatus.Configured, ( uint ) _consent );
        }
    }

    private void ReadControls( )
    {
        while ( controls.TryRead( out var frame ) )
        {
            RequireEpoch( frame );
            if ( frame.Header.Type == ClipboardHelperFrameType.Shutdown && frame.Payload.Length == 0 )
            {
                _stopped = true;
                return;
            }

            if ( frame.Header.Type != ClipboardHelperFrameType.Cancel || frame.Header.OfferIndex != -1 ||
                frame.Header.Sequence != 0 || frame.Payload.Length != 0 )
            {
                throw new InvalidDataException( "Clipboard control message is invalid." );
            }

            var offer = _pending.FirstOrDefault( offer => offer.Id == frame.Header.SelectionId );
            if ( offer is not null )
            {
                RemoveOffer( offer, notify: true );
            }

            CancelWrite( frame.Header.SelectionId );

            if ( !acknowledgements.TryWrite( new( new( ClipboardHelperFrameType.Ack, epoch, frame.Header.SelectionId, -1, 0, 0 ), ReadOnlyMemory<byte>.Empty ) ) )
            {
                throw new IOException( "Clipboard cancellation acknowledgement capacity was exceeded." );
            }
        }
    }

    private void ReadCommands( )
    {
        while ( commands.TryRead( out var frame ) )
        {
            RequireEpoch( frame );
            if ( TryHandleWrite( frame ) )
            {
                continue;
            }

            if ( frame.Header.Type == ClipboardHelperFrameType.ReceiveOffer )
            {
                BeginReceive( frame );
                continue;
            }

            if ( !_ready || _callback != 0 || frame.Header.Type != ClipboardHelperFrameType.Configure ||
                frame.Header.Sequence != _configurationSequence || frame.Header.SelectionId != Guid.Empty )
            {
                throw new InvalidDataException( "Clipboard command is unavailable in this host state." );
            }

            _consent = ClipboardCaptureProtocol.ReadConfiguration( frame );
            _configurationSequence++;
            _armed = false;
            foreach ( var offer in _pending.ToArray( ) )
            {
                RemoveOffer( offer, notify: true );
            }

            Sync( SyncPhase.Arm );
        }
    }

    private void RequireEpoch( ClipboardHelperFrame frame )
    {
        if ( frame.Header.HostEpoch != epoch )
        {
            throw new InvalidDataException( "Clipboard capture host epoch changed." );
        }
    }

    private void QueueStatus( ClipboardCaptureStatus status, uint detail = 0, Guid selection = default )
    {
        if ( _statuses.Count == 8 )
        {
            throw new IOException( "Clipboard status capacity was exceeded." );
        }

        _statuses.Enqueue( ClipboardCaptureProtocol.Status( epoch, status, detail, selection ) );
    }

    public void Dispose( )
    {
        foreach ( var offer in _offers.Values.ToArray( ) )
        {
            RemoveOffer( offer, notify: false );
        }

        DisposeServing( );

        foreach ( var proxy in new[] { _callback, _device, _seat, _manager, _registry } )
        {
            if ( proxy != 0 )
            {
                WaylandNative.DestroyProxy( proxy );
            }
        }

        if ( _display != 0 )
        {
            WaylandNative.Disconnect( _display );
        }

        _callback = _device = _seat = _manager = _registry = _display = 0;
        if ( _self.IsAllocated )
        {
            _self.Free( );
        }
    }

    private enum SyncPhase
    {
        Discover, Initial, Arm, Serve
    }
}
