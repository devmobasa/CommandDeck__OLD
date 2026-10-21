using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CommandDeck.Specs;

// A private protocol server with synthetic declarations only. Never connects to a desktop.
internal sealed unsafe partial class ClipboardWaylandServerFixture : IDisposable
{
    private const string Server = "libwayland-server.so.0";
    private readonly ConcurrentQueue<string> _commands = new( );
    private readonly Dictionary<nint, string> _resources = [];
    private readonly Dictionary<nint, OfferScenario> _scenarios = [];
    private OfferScenario? _latestOffer;
    private OfferScenario? _awaitedReceive;
    private readonly Dictionary<nint, List<string>> _sources = [];
    private nint _selectionSource;
    private int _cancelled;
    private readonly CancellationTokenSource _writers = new( );
    private GCHandle _self;
    private nint _display;
    private nint _client;
    private nint _device;
    private int _receives;
    private int _finishedReceives;
    private bool _failed;

    internal int Run( )
    {
        _display = CreateDisplay( );
        if ( _display == 0 )
        {
            throw new IOException( "Synthetic Wayland display allocation failed." );
        }

        _self = GCHandle.Alloc( this );
        var handle = GCHandle.ToIntPtr( _self );
        var socket = AddSocket( _display );
        if ( socket == 0 || CreateGlobal( _display, ClipboardWaylandInterfaces.Get( "wl_seat" ), 1, handle, &BindSeat ) == 0 ||
            CreateGlobal( _display, ClipboardWaylandInterfaces.Get( "ext_data_control_manager_v1" ), 1, handle, &BindManager ) == 0 )
        {
            throw new IOException( "Synthetic Wayland globals could not be created." );
        }

        Console.WriteLine( Marshal.PtrToStringUTF8( socket ) );
        _ = Task.Run( ReadCommands );
        var loop = GetLoop( _display );
        while ( !_failed )
        {
            if ( DispatchLoop( loop, 10 ) < 0 )
            {
                return 2;
            }

            while ( _commands.TryDequeue( out var command ) )
            {
                if ( command == "quit" )
                {
                    return 0;
                }

                if ( command == "count" )
                {
                    Console.WriteLine( _receives );
                }
                else if ( command == "offers" )
                {
                    Console.WriteLine( _selectionSource != 0 && _sources.TryGetValue( _selectionSource, out var mimes ) ? string.Join( ',', mimes ) : "" );
                }
                else if ( command == "cancelled" )
                {
                    Console.WriteLine( _cancelled );
                }
                else if ( command.StartsWith( "paste ", StringComparison.Ordinal ) )
                {
                    Paste( command[6..] );
                }
                else if ( command == "finished" )
                {
                    Console.WriteLine( Volatile.Read( ref _finishedReceives ) );
                }
                else if ( command == "wait-receive" )
                {
                    _awaitedReceive = _latestOffer;
                }
                else
                {
                    Announce( command );
                    Console.WriteLine( "sent" );
                }
            }

            if ( _awaitedReceive is { } receive && Volatile.Read( ref receive.Finished ) )
            {
                _awaitedReceive = null;
                Console.WriteLine( "finished" );
            }

            FlushClients( _display );
        }

        return 2;
    }

    private void ReadCommands( )
    {
        while ( Console.ReadLine( ) is { } command )
        {
            _commands.Enqueue( command );
        }

        _commands.Enqueue( "quit" );
    }

    [UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
    private static void BindSeat( nint client, nint data, uint version, uint id ) => Bind( client, data, id, "wl_seat" );

    [UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
    private static void BindManager( nint client, nint data, uint version, uint id ) => Bind( client, data, id, "ext_data_control_manager_v1" );

    private static void Bind( nint client, nint data, uint id, string type )
    {
        var server = ( ClipboardWaylandServerFixture ) GCHandle.FromIntPtr( data ).Target!;
        try
        {
            server._client = client;
            var resource = server.Resource( type, id );
            if ( type == "wl_seat" )
            {
                WaylandArgument capabilities = default;
                PostEvent( resource, 0, &capabilities );
            }
        }
        catch
        {
            server._failed = true;
        }
    }

    private nint Resource( string type, uint id )
    {
        var resource = CreateResource( _client, ClipboardWaylandInterfaces.Get( type ), 1, id );
        if ( resource == 0 )
        {
            throw new IOException( "Synthetic resource allocation failed." );
        }

        _resources.Add( resource, type );
        var handle = GCHandle.ToIntPtr( _self );
        SetDispatcher( resource, &Dispatch, handle, handle, &ResourceDestroyed );
        return resource;
    }

    [UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
    private static void ResourceDestroyed( nint resource )
    {
        var server = ( ClipboardWaylandServerFixture ) GCHandle.FromIntPtr( GetResourceData( resource ) ).Target!;
        server._resources.Remove( resource );
        server._scenarios.Remove( resource );
        server._sources.Remove( resource );
        if ( resource == server._selectionSource )
        {
            server._selectionSource = 0;
        }

        if ( resource == server._device )
        {
            server._device = 0;
        }
    }

    [UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
    private static int Dispatch( nint implementation, nint target, uint opcode, nint message, WaylandArgument* arguments )
    {
        var server = ( ClipboardWaylandServerFixture ) GCHandle.FromIntPtr( implementation ).Target!;
        try
        {
            var type = server._resources[target];
            if ( type == "ext_data_control_manager_v1" && opcode == 1 )
            {
                server._device = server.Resource( "ext_data_control_device_v1", arguments[0].Uint );
                server.Announce( "initial" );
            }
            else if ( type == "ext_data_control_offer_v1" && opcode == 0 )
            {
                server._receives++;
                server.WriteOffer( server._scenarios[target], Marshal.PtrToStringUTF8( arguments[0].Pointer )!, arguments[1].Int );
            }
            else if ( type == "ext_data_control_offer_v1" && opcode == 1 )
            {
                server._resources.Remove( target );
                server._scenarios.Remove( target );
                DestroyResource( target );
            }
            else if ( type == "ext_data_control_manager_v1" && opcode == 0 )
            {
                server._sources.Add( server.Resource( "ext_data_control_source_v1", arguments[0].Uint ), [] );
            }
            else if ( type == "ext_data_control_source_v1" && opcode == 0 )
            {
                server._sources[target].Add( Marshal.PtrToStringUTF8( arguments[0].Pointer )! );
            }
            else if ( type == "ext_data_control_source_v1" && opcode == 1 )
            {
                server._sources.Remove( target );
                server._resources.Remove( target );
                if ( server._selectionSource == target )
                {
                    server._selectionSource = 0;
                }

                DestroyResource( target );
            }
            else if ( type == "ext_data_control_device_v1" && opcode == 0 )
            {
                var previous = server._selectionSource;
                server._selectionSource = arguments[0].Pointer;
                if ( previous != 0 && previous != server._selectionSource && server._sources.ContainsKey( previous ) )
                {
                    server._cancelled++;
                    PostEvent( previous, 1, null );
                }
            }
            else
            {
                server._failed = true;
                return -1;
            }

            return 0;
        }
        catch ( Exception exception )
        {
            Console.Error.WriteLine( exception );
            server._failed = true;
            return -1;
        }
    }

    private void Announce( string scenario )
    {
        if ( _device == 0 )
        {
            throw new InvalidOperationException( "Synthetic device has not been bound." );
        }

        var offer = Resource( "ext_data_control_offer_v1", 0 );
        _latestOffer = new( scenario );
        _scenarios.Add( offer, _latestOffer );
        WaylandArgument argument = new( )
        {
            Pointer = offer
        };
        PostEvent( _device, 0, &argument );
        string[] names = scenario switch
        {
            "sensitive" => ["text/plain", "x-kde-passwordManagerHint"],
            "aliases" => ["text/plain", "text/plain;charset=utf-8", "Text/Plain"],
            "owner" => ["text/plain", ClipboardOfferPlan.OwnerMime],
            "gnome-copy" or "gnome-cut" => ["text/plain", "x-special/gnome-copied-files"],
            "blocked" or "large" => ["image/png"],
            "rich" => ["text/plain", "text/html"],
            "many" => Enumerable.Range( 0, 511 ).Select( static index => $"application/x-fixture-{index}".PadRight( 256, 'x' ) ).Append( "text/plain" ).ToArray( ),
            _ => ["text/plain", "application/x-fixture-" + scenario]
        };
        foreach ( var name in names )
        {
            argument.Pointer = Marshal.StringToCoTaskMemUTF8( name );
            PostEvent( offer, 0, &argument );
            Marshal.FreeCoTaskMem( argument.Pointer );
        }

        argument.Pointer = offer;
        PostEvent( _device, scenario == "primary" ? 3u : 1u, &argument );
    }

    public void Dispose( )
    {
        _writers.Cancel( );
        if ( _display != 0 )
        {
            DestroyClients( _display );
            DestroyDisplay( _display );
            _display = 0;
        }

        if ( _self.IsAllocated )
        {
            _self.Free( );
        }
    }

    [LibraryImport( Server, EntryPoint = "wl_display_create" )]
    private static partial nint CreateDisplay( );
    [LibraryImport( Server, EntryPoint = "wl_display_add_socket_auto" )]
    private static partial nint AddSocket( nint display );
    [LibraryImport( Server, EntryPoint = "wl_global_create" )]
    private static partial nint CreateGlobal( nint display, nint type, int version, nint data, delegate* unmanaged[Cdecl]< nint, nint, uint, uint, void > bind );
    [LibraryImport( Server, EntryPoint = "wl_display_get_event_loop" )]
    private static partial nint GetLoop( nint display );
    [LibraryImport( Server, EntryPoint = "wl_event_loop_dispatch" )]
    private static partial int DispatchLoop( nint loop, int timeout );
    [LibraryImport( Server, EntryPoint = "wl_display_flush_clients" )]
    private static partial void FlushClients( nint display );
    [LibraryImport( Server, EntryPoint = "wl_display_destroy_clients" )]
    private static partial void DestroyClients( nint display );
    [LibraryImport( Server, EntryPoint = "wl_display_destroy" )]
    private static partial void DestroyDisplay( nint display );
    [LibraryImport( Server, EntryPoint = "wl_resource_create" )]
    private static partial nint CreateResource( nint client, nint type, int version, uint id );
    [LibraryImport( Server, EntryPoint = "wl_resource_set_dispatcher" )]
    private static partial void SetDispatcher( nint resource, delegate* unmanaged[Cdecl]< nint, nint, uint, nint, WaylandArgument*, int > dispatcher,
        nint implementation, nint data, delegate* unmanaged[Cdecl]< nint, void > destroy );
    [LibraryImport( Server, EntryPoint = "wl_resource_get_user_data" )]
    private static partial nint GetResourceData( nint resource );
    [LibraryImport( Server, EntryPoint = "wl_resource_post_event_array" )]
    private static partial void PostEvent( nint resource, uint opcode, WaylandArgument* arguments );
    [LibraryImport( Server, EntryPoint = "wl_resource_destroy" )]
    private static partial void DestroyResource( nint resource );

    private void Paste( string mime )
    {
        if ( _selectionSource == 0 )
        {
            Console.WriteLine( "" );
            return;
        }

        var pipes = stackalloc int[2];
        if ( CreatePipe( pipes, 0x80000 ) != 0 )
        {
            throw new IOException( "Synthetic paste pipe could not be created." );
        }

        var name = Marshal.StringToCoTaskMemUTF8( mime );
        WaylandArgument* arguments = stackalloc WaylandArgument[2];
        arguments[0].Pointer = name;
        arguments[1].Int = pipes[1];
        PostEvent( _selectionSource, 0, arguments );
        Marshal.FreeCoTaskMem( name );
        _ = ClosePipe( pipes[1] );
        var handle = new SafeFileHandle( pipes[0], ownsHandle: true );
        _ = Task.Run( ( ) =>
        {
            using ( handle )
            {
                using FileStream stream = new( handle, FileAccess.Read );
                using MemoryStream bytes = new( );
                stream.CopyTo( bytes );
                Console.WriteLine( Convert.ToBase64String( bytes.ToArray( ) ) );
            }
        } );
    }

    [LibraryImport( "libc", EntryPoint = "pipe2", SetLastError = true )]
    private static partial int CreatePipe( int* descriptors, int flags );
    [LibraryImport( "libc", EntryPoint = "close" )]
    private static partial int ClosePipe( int descriptor );

    private void WriteOffer( OfferScenario offer, string mime, int descriptor )
    {
        var scenario = offer.Name;
        var handle = new SafeFileHandle( descriptor, ownsHandle: true );
        _ = Task.Run( ( ) =>
        {
            using ( handle )
            {
                try
                {
                    using FileStream stream = new( handle, FileAccess.Write );
                    if ( scenario == "stall" )
                    {
                        _writers.Token.WaitHandle.WaitOne( );
                        return;
                    }

                    if ( scenario == "trickle" )
                    {
                        using PeriodicTimer cadence = new( TimeSpan.FromMilliseconds( 250 ) );
                        while ( cadence.WaitForNextTickAsync( _writers.Token ).AsTask( ).GetAwaiter( ).GetResult( ) )
                        {
                            stream.WriteByte( ( byte ) 'x' );
                            stream.Flush( );
                        }

                        return;
                    }

                    var bytes = mime == "x-special/gnome-copied-files"
                        ? Encoding.UTF8.GetBytes( scenario == "gnome-cut" ? "cut\nfile:///synthetic-secret\n" : "copy\nfile:///synthetic-body\n" )
                        : mime == "text/html" ? "<p>synthetic <b>rich</b></p>"u8.ToArray( )
                        : scenario switch
                        {
                            "limit" => Enumerable.Repeat( ( byte ) 'x', 32768 ).ToArray( ),
                            "oversize" => Enumerable.Repeat( ( byte ) 'x', 32769 ).ToArray( ),
                            "blocked" or "large" => new byte[16 * 1024 * 1024],
                            "rich" => "synthetic rich"u8.ToArray( ),
                            _ => Encoding.UTF8.GetBytes( "synthetic\r\n😀é\n" )
                        };
                    stream.Write( bytes );
                    stream.Flush( );
                }
                catch ( IOException ) { }
                catch ( OperationCanceledException ) when ( _writers.IsCancellationRequested ) { }
                finally
                {
                    Interlocked.Increment( ref _finishedReceives );
                    Volatile.Write( ref offer.Finished, true );
                }
            }
        } );
    }

    private sealed class OfferScenario( string name )
    {
        internal string Name { get; } = name;
        internal bool Finished;
    }
}
