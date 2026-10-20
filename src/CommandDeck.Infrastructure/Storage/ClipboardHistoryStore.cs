using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed record ClipboardHistoryEntry( string Id, string Text, bool Pinned );

public sealed partial class ClipboardHistoryStore : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new( false, true );
    private readonly object _gate = new( );
    private readonly SqliteConnection _connection;
    private readonly ClipboardHistoryOptions _options;
    private readonly TimeProvider _time;
    private readonly string _path;
    private readonly Func<long>? _migrationFreeSpace;
    private readonly Action<string>? _migrationCheckpoint;
    private ClipboardPayloadDirectory? _payloadDirectory;
    private ClipboardPayloadLedger? _payloadLedger;
    private FileStream? _ownerLease;
    private long _captureEpoch = 1;
    private long _clearEpoch = 1;
    public long CaptureEpoch => Interlocked.Read( ref _captureEpoch );
    public long ClearEpoch => Interlocked.Read( ref _clearEpoch );

    public ClipboardHistoryStore( string path, ClipboardHistoryOptions options, TimeProvider? time = null )
        : this( path, options, time, null )
    {
    }

    internal ClipboardHistoryStore( string path, ClipboardHistoryOptions options, TimeProvider? time, Func<long>? migrationFreeSpace,
        Action<string>? migrationCheckpoint = null )
    {
        _options = options;
        _time = time ?? TimeProvider.System;
        _path = Path.GetFullPath( path );
        _migrationFreeSpace = migrationFreeSpace;
        _migrationCheckpoint = migrationCheckpoint;
        Directory.CreateDirectory( Path.GetDirectoryName( Path.GetFullPath( path ) )! );
        var existed = File.Exists( path );
        if ( new FileInfo( path ).LinkTarget is not null )
        {
            throw new IOException( "Clipboard history database cannot be a symbolic link." );
        }
        if ( existed && new FileInfo( path ).Length == 0 )
        {
            throw new IOException( "Clipboard history is empty or truncated; existing file was preserved." );
        }

        FileStreamOptions fileOptions = new( )
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.ReadWrite
        };
        if ( OperatingSystem.IsLinux( ) )
        {
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        _connection = new SqliteConnection( new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString( ) );
        try
        {
            _ownerLease = new FileStream( path, fileOptions );
            if ( !LinuxFileLease.TryAcquire( _ownerLease.SafeFileHandle ) )
            {
                throw new IOException( "Clipboard history is already owned by another process." );
            }

            if ( OperatingSystem.IsLinux( ) )
            {
                File.SetUnixFileMode( path, UnixFileMode.UserRead | UnixFileMode.UserWrite );
            }

            _connection.Open( );
            InitializePolicy( allowNewDatabase: !existed );
            InitializePayloadStorage( );
            InitializeProjectionStorage( );
            RemoveSearchIndex( );
            CompleteStartupClear( );
        }
        catch
        {
            _connection.Dispose( );
            _payloadDirectory?.Dispose( );
            _ownerLease?.Dispose( );
            throw;
        }
    }

    public bool Enabled
    {
        get
        {
            lock ( _gate )
            {
                var policy = ReadPolicy( );
                return policy.Consent != ClipboardConsent.None && !policy.Paused && !MigrationRequired( );
            }
        }
        set
        {
            lock ( _gate )
            {
                Execute( "UPDATE clipboard_policy SET consent=$value,paused=0,revision=revision+1", ("$value", value ? 1 : 0) );
                AdvanceCaptureEpoch( );
            }
        }
    }

    public void Capture( string text ) => Capture( text, CaptureEpoch );

    public void Capture( string text, long captureEpoch )
    {
        if ( string.IsNullOrEmpty( text ) || text.Length > 32768 || text.Contains( '\0' ) )
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes( text );
        }
        catch ( EncoderFallbackException )
        {
            return;
        }

        if ( bytes.Length > 32768 )
        {
            return;
        }

        lock ( _gate )
        {
            if ( !Enabled || captureEpoch != _captureEpoch )
            {
                return;
            }

            var id = Convert.ToHexString( SHA256.HashData( bytes ) );
            if ( Read( id ) is not null )
            {
                Transaction( ( ) =>
                {
                    Execute( "UPDATE entries SET copied=$time,revision=revision+1 WHERE id=$id",
                        ("$id", id), ("$time", _time.GetUtcNow( ).ToUnixTimeSeconds( )) );
                    AdvanceRevision( );
                    return 0;
                } );

                QueueSequenceCopy( id );
                return;
            }

            WriteTextPayload( bytes, ( payload, digest ) =>
            {
                Execute( "INSERT INTO entries(id,text,copied,incarnation) VALUES($id,$text,$time,$incarnation)",
                    ("$id", id), ("$text", text), ("$time", _time.GetUtcNow( ).ToUnixTimeSeconds( )), ("$incarnation", Guid.NewGuid( ).ToString( "N" )) );
                Execute( "INSERT INTO clipboard_selections VALUES($id,'text',$mime,$digest); INSERT INTO clipboard_offers VALUES($id,$payload,$mime,0)",
                    ("$id", id), ("$mime", PlainMime), ("$digest", digest), ("$payload", payload.Id.ToString( "N" )) );
                WriteProjectionMetadata( id, EmptyProjection );
                AdvanceRevision( );
                PrunePolicy( ReadPolicy( ) );
            }, ( ) => Enabled && captureEpoch == _captureEpoch );
            QueueSequenceCopy( id );
            _payloadLedger!.Cleanup( );
        }
    }

    public List<ClipboardHistoryEntry> Search( string text, int limit )
    {
        lock ( _gate )
        {
            Prune( );
            using var command = _connection.CreateCommand( );
            command.CommandText = "SELECT id,text,pinned FROM entries WHERE instr(lower(text),lower($text))>0 ORDER BY pinned DESC,copied DESC,rowid DESC LIMIT $limit";
            command.Parameters.AddWithValue( "$text", text );
            command.Parameters.AddWithValue( "$limit", Math.Clamp( limit, 1, 100 ) );
            using var reader = command.ExecuteReader( );
            List<ClipboardHistoryEntry> entries = [];
            while ( reader.Read( ) )
            {
                entries.Add( new( reader.GetString( 0 ), reader.GetString( 1 ), reader.GetBoolean( 2 ) ) );
            }
            return entries;
        }
    }

    public string? Read( string id )
    {
        lock ( _gate )
        {
            Prune( );
            using var command = _connection.CreateCommand( );
            command.CommandText = "SELECT text FROM entries WHERE id=$id";
            command.Parameters.AddWithValue( "$id", id );
            return command.ExecuteScalar( ) as string;
        }
    }

    public bool Pin( string id, bool pinned )
    {
        lock ( _gate )
        {
            // Reserve half the capacity for new copies even if users pin many entries.
            return Transaction( ( ) =>
            {
                if ( pinned && PolicyRows( ).Where( row => row.Pinned || row.Id == id ).Sum( static row => row.Bytes ) >
                    ( long ) ReadPolicy( ).QuotaMiB * 1024 * 1024 / 2 )
                {
                    return false;
                }

                var changed = Execute( "UPDATE entries SET pinned=$pinned,revision=revision+1 WHERE id=$id AND ($pinned=0 OR pinned=1 OR (SELECT count(*) FROM entries WHERE pinned=1)<$maximum)",
                    ("$id", id), ("$pinned", pinned ? 1 : 0), ("$maximum", ReadPolicy( ).MaximumEntries / 2) ) > 0;
                if ( changed )
                {
                    AdvanceRevision( );
                }

                return changed;
            } );
        }
    }

    public void Delete( string id )
    {
        lock ( _gate )
        {
            Transaction( ( ) =>
            {
                var changed = Execute( "DELETE FROM entries WHERE id=$id", ("$id", id) );
                if ( changed > 0 )
                {
                    AdvanceRevision( );
                }

                return changed;
            } );
            _payloadLedger!.Cleanup( );
        }
    }
    public void Clear( long? expectedStoreRevision = null )
    {
        lock ( _gate )
        {
            Transaction( ( ) =>
            {
                if ( expectedStoreRevision.HasValue && expectedStoreRevision != StoreRevision )
                {
                    throw new InvalidOperationException( "Clipboard history changed; review Clear again." );
                }

                Execute( "DELETE FROM entries" );
                AdvanceRevision( );
                return 0;
            } );
            _sequence = null;
            AdvanceCaptureEpoch( );
            Interlocked.Increment( ref _clearEpoch );
            _payloadLedger!.Cleanup( );
            Execute( "VACUUM" );
        }
    }

    public void Prune( )
    {
        lock ( _gate )
        {
            if ( MigrationRequired( ) )
            {
                return;
            }

            Transaction( ( ) =>
            {
                PrunePolicy( ReadPolicy( ) );
                return 0;
            } );
            _payloadLedger!.Cleanup( );
        }
    }

    private int Execute( string sql, params (string Name, object Value)[] parameters )
    {
        using var command = _connection.CreateCommand( );
        command.CommandText = sql;
        foreach ( var (name, value) in parameters )
        {
            command.Parameters.AddWithValue( name, value );
        }

        return command.ExecuteNonQuery( );
    }

    public void Dispose( )
    {
        lock ( _gate )
        {
            _connection.Dispose( );
            _payloadDirectory?.Dispose( );
            _ownerLease?.Dispose( );
        }
    }
}
