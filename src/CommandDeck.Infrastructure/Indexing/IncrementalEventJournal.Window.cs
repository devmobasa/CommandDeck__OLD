using System.Text;

namespace CommandDeck;

internal sealed partial class IncrementalEventJournal
{
    private const string ClosedWindowMarker = "closed";
    private const string OpenWindowMarkerPrefix = "open|";
    private const int WindowMarkerLength = 64;
    private const int MaximumWindowMarkerBytes = 256;
    private const string BootIdPath = "/proc/sys/kernel/random/boot_id";

    private bool _windowOpen;

    private void RecoverWindow( )
    {
        var marker = ReadWindowMarker( );
        if ( marker is null )
        {
            // Journals written before group commit flushed every event.
            WriteWindowMarker( open: false, durable: true );
            LinuxDirectoryDurability.FlushParent( _windowPath );
            return;
        }

        if ( !marker.Value.Open )
        {
            return;
        }

        if ( _bootId is not null &&
            string.Equals( marker.Value.BootId, _bootId, StringComparison.Ordinal ) )
        {
            // Only the process stopped: its writes are still in the page cache.
            FlushJournal( );
            WriteWindowMarker( open: false, durable: true );
            return;
        }

        UnsyncedWindowLost = true;
        _windowOpen = true;
    }

    // The marker is durable before the first unflushed event is written.
    private void OpenWindow( )
    {
        if ( _windowOpen )
        {
            return;
        }

        WriteWindowMarker( open: true, durable: true );
        _windowOpen = true;
    }

    // A closed marker that is lost in a crash only costs an extra reconcile.
    private void CloseWindow( bool durable )
    {
        if ( UnsyncedWindowLost || (!_windowOpen && !durable) )
        {
            return;
        }

        WriteWindowMarker( open: false, durable );
        _windowOpen = false;
    }

    private void FlushJournal( )
    {
        using var stream = OpenPrivateFile(
            _journalPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read );
        _flushToDisk( stream );
    }

    private void WriteWindowMarker( bool open, bool durable )
    {
        var text = open ? OpenWindowMarkerPrefix + _bootId : ClosedWindowMarker;
        var bytes = Encoding.UTF8.GetBytes( text.PadRight( WindowMarkerLength ) );

        // Rewrite one fixed-length record in place: no crash leaves it empty.
        using var stream = OpenPrivateFile(
            _windowPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.None );
        stream.Write( bytes );
        if ( stream.Length != bytes.Length )
        {
            stream.SetLength( bytes.Length );
        }

        if ( durable )
        {
            _flushToDisk( stream );
        }
    }

    private WindowMarker? ReadWindowMarker( )
    {
        string text;
        try
        {
            if ( !File.Exists( _windowPath ) )
            {
                return null;
            }

            using var stream = new FileStream( _windowPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite );
            var buffer = new byte[MaximumWindowMarkerBytes + 1];
            var length = stream.ReadAtLeast( buffer, buffer.Length, throwOnEndOfStream: false );
            if ( length > MaximumWindowMarkerBytes )
            {
                return new WindowMarker( Open: true, BootId: null );
            }

            text = Encoding.UTF8.GetString( buffer, 0, length ).Trim( );
        }
        catch ( Exception exception ) when (
            exception is IOException or UnauthorizedAccessException )
        {
            // A marker that cannot be read cannot vouch for the journal.
            return new WindowMarker( Open: true, BootId: null );
        }

        if ( string.Equals( text, ClosedWindowMarker, StringComparison.Ordinal ) )
        {
            return new WindowMarker( Open: false, BootId: null );
        }

        var bootId = text.StartsWith( OpenWindowMarkerPrefix, StringComparison.Ordinal )
            ? text[OpenWindowMarkerPrefix.Length..]
            : string.Empty;
        return new WindowMarker( Open: true, BootId: bootId.Length == 0 ? null : bootId );
    }

    private static string? ReadBootId( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return null;
        }

        try
        {
            var bootId = File.ReadAllText( BootIdPath, Encoding.ASCII ).Trim( );
            return bootId.Length == 0 ? null : bootId;
        }
        catch ( Exception exception ) when (
            exception is IOException or UnauthorizedAccessException )
        {
            // Without a boot ID every open window is treated as possibly lost.
            return null;
        }
    }

    private readonly record struct WindowMarker( bool Open, string? BootId );
}
