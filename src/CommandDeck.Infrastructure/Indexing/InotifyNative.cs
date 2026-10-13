using System.Runtime.InteropServices;

namespace CommandDeck;

internal static unsafe partial class InotifyNative
{
    internal const int NonBlocking = 0x800;
    internal const int CloseOnExec = 0x80000;
    internal const short PollIn = 0x1;

    internal const int InterruptedError = 4;
    internal const int TryAgainError = 11;
    internal const int NoSpaceError = 28;

    [LibraryImport( "libc", EntryPoint = "inotify_init1", SetLastError = true )]
    internal static partial int Initialize( int flags );

    [LibraryImport(
        "libc",
        EntryPoint = "inotify_add_watch",
        StringMarshalling = StringMarshalling.Utf8,
        SetLastError = true )]
    internal static partial int AddWatch( int descriptor, string path, uint mask );

    [LibraryImport( "libc", EntryPoint = "inotify_rm_watch", SetLastError = true )]
    internal static partial int RemoveWatch( int descriptor, int watch );

    [LibraryImport( "libc", EntryPoint = "eventfd", SetLastError = true )]
    internal static partial int CreateEvent( uint initialValue, int flags );

    [LibraryImport( "libc", EntryPoint = "poll", SetLastError = true )]
    internal static partial int Poll( PollEntry* entries, nuint count, int timeoutMilliseconds );

    [LibraryImport( "libc", EntryPoint = "read", SetLastError = true )]
    internal static partial nint Read( int descriptor, byte* buffer, nuint count );

    [LibraryImport( "libc", EntryPoint = "write", SetLastError = true )]
    internal static partial nint Write( int descriptor, byte* buffer, nuint count );

    [LibraryImport( "libc", EntryPoint = "close" )]
    internal static partial int Close( int descriptor );

    [StructLayout( LayoutKind.Sequential )]
    internal struct PollEntry
    {
        public int Descriptor;
        public short Events;
        public short ReturnedEvents;
    }
}
