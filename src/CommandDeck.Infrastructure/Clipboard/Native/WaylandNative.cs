using System.Runtime.InteropServices;

namespace CommandDeck;

internal static unsafe partial class WaylandNative
{
    private const string Client = "libwayland-client.so.0";

    [LibraryImport( Client, EntryPoint = "wl_display_connect", StringMarshalling = StringMarshalling.Utf8 )]
    internal static partial nint Connect( string? name );
    [LibraryImport( Client, EntryPoint = "wl_display_disconnect" )]
    internal static partial void Disconnect( nint display );
    [LibraryImport( Client, EntryPoint = "wl_display_get_fd" )]
    internal static partial int GetFd( nint display );
    [LibraryImport( Client, EntryPoint = "wl_display_dispatch_pending" )]
    internal static partial int DispatchPending( nint display );
    [LibraryImport( Client, EntryPoint = "wl_display_prepare_read" )]
    internal static partial int PrepareRead( nint display );
    [LibraryImport( Client, EntryPoint = "wl_display_read_events" )]
    internal static partial int ReadEvents( nint display );
    [LibraryImport( Client, EntryPoint = "wl_display_cancel_read" )]
    internal static partial void CancelRead( nint display );
    [LibraryImport( Client, EntryPoint = "wl_display_flush", SetLastError = true )]
    internal static partial int Flush( nint display );
    [LibraryImport( Client, EntryPoint = "wl_proxy_marshal_array_flags" )]
    internal static partial nint MarshalArray( nint proxy, uint opcode, nint type, uint version, uint flags, WaylandArgument* arguments );
    [LibraryImport( Client, EntryPoint = "wl_proxy_add_dispatcher" )]
    internal static partial int AddDispatcher( nint proxy, delegate* unmanaged[Cdecl]< nint, nint, uint, nint, WaylandArgument*, int > dispatcher, nint implementation, nint data );
    [LibraryImport( Client, EntryPoint = "wl_proxy_destroy" )]
    internal static partial void DestroyProxy( nint proxy );
    [LibraryImport( "libc", EntryPoint = "poll", SetLastError = true )]
    internal static partial int Poll( PollDescriptor* descriptors, nuint count, int timeoutMilliseconds );
    [LibraryImport( "libc", EntryPoint = "eventfd", SetLastError = true )]
    internal static partial int EventFd( uint initialValue, int flags );
    [LibraryImport( "libc", EntryPoint = "pipe2", SetLastError = true )]
    internal static partial int Pipe( int* descriptors, int flags );
    [LibraryImport( "libc", EntryPoint = "fcntl", SetLastError = true )]
    internal static partial int SetFlags( int descriptor, int command, int flags );
    [LibraryImport( "libc", EntryPoint = "read", SetLastError = true )]
    internal static partial nint Read( int descriptor, byte* buffer, nuint count );
    [LibraryImport( "libc", EntryPoint = "write", SetLastError = true )]
    internal static partial nint Write( int descriptor, byte* buffer, nuint count );
    [LibraryImport( "libc", EntryPoint = "close" )]
    internal static partial int Close( int descriptor );
}

[StructLayout( LayoutKind.Explicit )]
internal struct WaylandArgument
{
    [FieldOffset( 0 )] public int Int;
    [FieldOffset( 0 )] public uint Uint;
    [FieldOffset( 0 )] public nint Pointer;
}

[StructLayout( LayoutKind.Sequential )]
internal struct PollDescriptor
{
    public int Fd;
    public short Events;
    public short ReturnedEvents;
}

[StructLayout( LayoutKind.Sequential )]
internal struct WaylandInterface
{
    public nint Name;
    public int Version;
    public int RequestCount;
    public nint Requests;
    public int EventCount;
    public nint Events;
}

[StructLayout( LayoutKind.Sequential )]
internal struct WaylandMessage
{
    public nint Name;
    public nint Signature;
    public nint Types;
}

internal sealed record WaylandMessageDefinition( string Name, string Signature, string?[] Types );
internal sealed record WaylandInterfaceDefinition( string Name, int Version, WaylandMessageDefinition[] Requests, WaylandMessageDefinition[] Events );
