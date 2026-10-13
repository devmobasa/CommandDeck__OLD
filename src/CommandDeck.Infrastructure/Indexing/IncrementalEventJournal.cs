using System.Globalization;
using System.Text;

namespace CommandDeck;

internal enum IncrementalEventKind
{
    Upsert,
    Delete,
    Rename,
    Overflow,
    PolicyChanged
}

internal sealed record IncrementalJournalEvent(
    long Sequence,
    IncrementalEventKind Kind,
    string Path,
    string? OldPath );

// Events are written as they arrive but flushed to disk once per debounce
// window (group commit). A durable window marker records that unflushed events
// may exist, so an operating-system crash that drops them is detected and
// reconciled; a process crash keeps them in the page cache.
internal sealed partial class IncrementalEventJournal : IDisposable
{
    private const UnixFileMode PrivateFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _journalPath;
    private readonly string _statePath;
    private readonly string _ownerPath;
    private readonly string _windowPath;
    private readonly Action<FileStream> _flushToDisk;
    private readonly string? _bootId;
    private readonly object _gate = new( );
    private readonly List<IncrementalJournalEvent> _events = [];
    private long _checkpoint;
    private FileStream? _ownerLease;
    private bool _unflushed;

    public IncrementalEventJournal( string databasePath )
        : this( databasePath, static stream => stream.Flush( flushToDisk: true ), ReadBootId( ) )
    {
    }

    // Specs substitute the flush to count disk flushes and the boot ID to model
    // a restart after a process crash or after an operating-system crash.
    internal IncrementalEventJournal(
        string databasePath,
        Action<FileStream> flushToDisk,
        string? bootId )
    {
        ArgumentNullException.ThrowIfNull( flushToDisk );

        var expanded = PathExpansion.Expand( databasePath );
        _journalPath = expanded + ".index-events";
        _statePath = expanded + ".index-state";
        _ownerPath = expanded + ".index-owner";
        _windowPath = expanded + ".index-window";
        _flushToDisk = flushToDisk;
        _bootId = string.IsNullOrWhiteSpace( bootId ) ? null : bootId;
    }

    public bool IsCorrupted { get; private set; }
    public string? ExtractorVersion { get; private set; }
    public long LastSequence { get; private set; }
    public bool UnsyncedWindowLost { get; private set; }

    public long PendingCount
    {
        get
        {
            lock ( _gate )
            {
                return _events.LongCount( value => value.Sequence > _checkpoint );
            }
        }
    }

    public bool IsOwner => _ownerLease is not null;

    public bool Initialize( )
    {
        lock ( _gate )
        {
            if ( _ownerLease is not null )
            {
                return true;
            }

            var directory = Path.GetDirectoryName( _journalPath )!;
            var directoryExists = Directory.Exists( directory );
            Directory.CreateDirectory( directory );
            if ( !directoryExists && OperatingSystem.IsLinux( ) )
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute );
            }

            try
            {
                var ownerExisted = File.Exists( _ownerPath );
                _ownerLease = OpenPrivateFile(
                    _ownerPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None );
                if ( !LinuxFileLease.TryAcquire( _ownerLease.SafeFileHandle ) )
                {
                    _ownerLease.Dispose( );
                    _ownerLease = null;
                    return false;
                }
                if ( !ownerExisted )
                {
                    LinuxDirectoryDurability.FlushParent( _ownerPath );
                }
            }
            catch ( IOException )
            {
                return false;
            }

            var journalExisted = File.Exists( _journalPath );
            using ( OpenPrivateFile(
                _journalPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read ) )
            {
            }
            if ( !journalExisted )
            {
                LinuxDirectoryDurability.FlushParent( _journalPath );
            }
            ReadState( );
            ReadJournal( );
            LastSequence = Math.Max( LastSequence, _checkpoint );
            RecoverWindow( );
            return true;
        }
    }

    // Writes without flushing: the event survives a process crash in the page
    // cache, and the open window marker covers an operating-system crash.
    public long Append( IncrementalEventKind kind, string path, string? oldPath )
    {
        lock ( _gate )
        {
            var sequence = checked(LastSequence + 1);
            IncrementalJournalEvent entry = new( sequence, kind, path, oldPath );
            var line = Serialize( entry );
            OpenWindow( );

            using ( var stream = OpenPrivateFile(
                _journalPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read ) )
            {
                using StreamWriter writer = new( stream, Encoding.UTF8, leaveOpen: true );
                writer.WriteLine( line );
                writer.Flush( );
            }

            _unflushed = true;
            _events.Add( entry );
            LastSequence = sequence;
            return sequence;
        }
    }

    // Closes the current window with one flush of every event written in it.
    public void Commit( )
    {
        lock ( _gate )
        {
            if ( !_unflushed || _ownerLease is null )
            {
                return;
            }

            FlushJournal( );
            _unflushed = false;
            CloseWindow( durable: false );
        }
    }

    public void Dispose( )
    {
        lock ( _gate )
        {
            if ( _ownerLease is not null )
            {
                try
                {
                    if ( _unflushed )
                    {
                        FlushJournal( );
                        _unflushed = false;
                    }

                    CloseWindow( durable: true );
                }
                catch ( Exception exception ) when (
                    exception is IOException or UnauthorizedAccessException )
                {
                    // The marker stays open: the next start flushes or reconciles.
                }
            }

            _ownerLease?.Dispose( );
            _ownerLease = null;
        }
    }

    public IReadOnlyList<IncrementalJournalEvent> ReadPending( )
        => ReadPending( int.MaxValue );

    public IReadOnlyList<IncrementalJournalEvent> ReadPending( int maximumCount )
    {
        lock ( _gate )
        {
            return _events.Where( value => value.Sequence > _checkpoint )
                .Take( Math.Max( 1, maximumCount ) )
                .ToArray( );
        }
    }

    public void AdvanceCheckpoint( long sequence, string? extractorVersion )
    {
        lock ( _gate )
        {
            _checkpoint = Math.Max( _checkpoint, Math.Min( sequence, LastSequence ) );
            ExtractorVersion = extractorVersion;
            WriteState( );
            _events.RemoveAll( value => value.Sequence <= _checkpoint );
            RewriteJournal( );

            // The rewrite flushed every retained event, and the caller has
            // reconciled whatever a lost window or a corrupt line left behind.
            _unflushed = false;
            IsCorrupted = false;
            var lostWindowReconciled = UnsyncedWindowLost;
            UnsyncedWindowLost = false;
            CloseWindow( durable: lostWindowReconciled );
        }
    }

    private void ReadState( )
    {
        if ( !File.Exists( _statePath ) )
        {
            return;
        }

        try
        {
            var fields = File.ReadAllText( _statePath, Encoding.UTF8 ).Split( '|' );
            if ( fields.Length != 3 ||
                !long.TryParse( fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out _checkpoint ) ||
                !long.TryParse( fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var last ) )
            {
                IsCorrupted = true;
                return;
            }

            LastSequence = last;
            ExtractorVersion = Decode( fields[2] );
        }
        catch ( Exception exception ) when (
            exception is IOException or UnauthorizedAccessException or FormatException )
        {
            IsCorrupted = true;
        }
    }

    private void ReadJournal( )
    {
        if ( !File.Exists( _journalPath ) )
        {
            return;
        }

        try
        {
            foreach ( var line in File.ReadLines( _journalPath, Encoding.UTF8 ) )
            {
                if ( line.Length == 0 )
                {
                    continue;
                }

                var entry = Deserialize( line );
                if ( entry is null )
                {
                    IsCorrupted = true;
                    continue;
                }

                _events.Add( entry );
                LastSequence = Math.Max( LastSequence, entry.Sequence );
            }
        }
        catch ( Exception exception ) when (
            exception is IOException or UnauthorizedAccessException or FormatException )
        {
            IsCorrupted = true;
        }
    }

    private void WriteState( )
    {
        var temporaryPath = _statePath + ".tmp";
        var content = string.Join(
            '|',
            _checkpoint.ToString( CultureInfo.InvariantCulture ),
            LastSequence.ToString( CultureInfo.InvariantCulture ),
            Encode( ExtractorVersion ) );
        using ( var stream = OpenPrivateFile(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None ) )
        {
            using ( StreamWriter writer = new( stream, Encoding.UTF8, leaveOpen: true ) )
            {
                writer.Write( content );
                writer.Flush( );
                _flushToDisk( stream );
            }
        }
        File.Move( temporaryPath, _statePath, overwrite: true );
        EnsurePrivateMode( _statePath );
        LinuxDirectoryDurability.FlushParent( _statePath );
    }

    private void RewriteJournal( )
    {
        var temporaryPath = _journalPath + ".tmp";
        using ( var stream = OpenPrivateFile(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None ) )
        {
            using ( StreamWriter writer = new( stream, Encoding.UTF8, leaveOpen: true ) )
            {
                foreach ( var entry in _events )
                {
                    writer.WriteLine( Serialize( entry ) );
                }

                writer.Flush( );
                _flushToDisk( stream );
            }
        }
        File.Move( temporaryPath, _journalPath, overwrite: true );
        EnsurePrivateMode( _journalPath );
        LinuxDirectoryDurability.FlushParent( _journalPath );
    }

    private static string Serialize( IncrementalJournalEvent entry ) =>
        string.Join(
            '|',
            entry.Sequence.ToString( CultureInfo.InvariantCulture ),
            (( int ) entry.Kind).ToString( CultureInfo.InvariantCulture ),
            Encode( entry.Path ),
            Encode( entry.OldPath ) );

    private static IncrementalJournalEvent? Deserialize( string line )
    {
        var fields = line.Split( '|' );
        if ( fields.Length != 4 ||
            !long.TryParse( fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence ) ||
            !int.TryParse( fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var kindValue ) ||
            !Enum.IsDefined( ( IncrementalEventKind ) kindValue ) )
        {
            return null;
        }

        return new IncrementalJournalEvent(
            sequence,
            ( IncrementalEventKind ) kindValue,
            Decode( fields[2] ) ?? string.Empty,
            Decode( fields[3] ) );
    }

    private static string Encode( string? value ) =>
        value is null ? string.Empty : Convert.ToBase64String( Encoding.UTF8.GetBytes( value ) );

    private static string? Decode( string value ) =>
        value.Length == 0 ? null : Encoding.UTF8.GetString( Convert.FromBase64String( value ) );

    private static FileStream OpenPrivateFile(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share )
    {
        FileStreamOptions options = new( )
        {
            Mode = mode,
            Access = access,
            Share = share,
            Options = FileOptions.SequentialScan
        };
        if ( OperatingSystem.IsLinux( ) )
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        FileStream stream = new( path, options );
        EnsurePrivateMode( path );
        return stream;
    }

    private static void EnsurePrivateMode( string path )
    {
        if ( OperatingSystem.IsLinux( ) )
        {
            File.SetUnixFileMode( path, PrivateFileMode );
        }
    }
}
