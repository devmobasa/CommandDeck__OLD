using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CommandDeck;

/// <summary>
/// Two durable admissions. Arming is committed before a dispatch permit may be sent.
/// Reopening never arms or dispatches an old attempt. Callers supply verified local evidence.
/// </summary>
public sealed class ClipboardHandoffJournal : IDisposable
{
    private readonly object _gate = new( );
    private readonly SqliteConnection _connection;
    private readonly string _bootId;
    private readonly HashSet<Guid> _admitted = [];
    private bool _faulted;

    public ClipboardHandoffJournal( string path, string bootId )
    {
        ValidateIdentity( bootId );
        _bootId = bootId;
        var fullPath = Path.GetFullPath( path );
        var directory = Path.GetDirectoryName( fullPath )!;
        if ( !Directory.Exists( directory ) || new DirectoryInfo( directory ).LinkTarget is not null )
        {
            throw new IOException( "Handoff journal requires an existing private directory." );
        }

        if ( OperatingSystem.IsLinux( ) &&
            File.GetUnixFileMode( directory ) != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute) )
        {
            throw new IOException( "Handoff journal directory must have mode 0700." );
        }

        var existed = File.Exists( fullPath );
        if ( !existed )
        {
            using var created = SafeRegularFile.CreatePrivateWriteStream( fullPath );
            created.Flush( flushToDisk: true );
            LinuxDirectoryDurability.FlushParent( fullPath );
        }

        using var identity = SafeRegularFile.Open( fullPath );
        if ( OperatingSystem.IsLinux( ) &&
            File.GetUnixFileMode( fullPath ) != (UnixFileMode.UserRead | UnixFileMode.UserWrite) )
        {
            throw new IOException( "Handoff journal must have mode 0600." );
        }

        _connection = new SqliteConnection( new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString( ) );
        try
        {
            _connection.Open( );
            Execute( "PRAGMA busy_timeout=1000; PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA secure_delete=ON;" );
            if ( !existed )
            {
                Execute( "BEGIN IMMEDIATE; CREATE TABLE header(version INTEGER NOT NULL, boot TEXT NOT NULL, checksum TEXT NOT NULL); " +
                    "CREATE TABLE slots(slot INTEGER PRIMARY KEY CHECK(slot BETWEEN 0 AND 1), body TEXT NOT NULL, checksum TEXT NOT NULL); " +
                    "INSERT INTO header VALUES(1,$boot,$checksum); COMMIT;", ("$boot", bootId), ("$checksum", Digest( "1\n" + bootId )) );
            }

            using var check = _connection.CreateCommand( );
            check.CommandText = "PRAGMA integrity_check";
            if ( !string.Equals( check.ExecuteScalar( ) as string, "ok", StringComparison.Ordinal ) )
            {
                throw new IOException( "Handoff journal integrity check failed; recovery required." );
            }

            Mutate( ( ) =>
            {
                using var header = _connection.CreateCommand( );
                header.CommandText = "SELECT version,boot,checksum FROM header";
                using var reader = header.ExecuteReader( );
                if ( !reader.Read( ) || reader.GetInt32( 0 ) != 1 )
                {
                    throw new IOException( "Unsupported handoff journal schema." );
                }

                var previousBoot = reader.GetString( 1 );
                ValidateIdentity( previousBoot );
                if ( reader.GetString( 2 ) != Digest( "1\n" + previousBoot ) )
                {
                    throw new IOException( "Handoff journal header checksum failed; recovery required." );
                }
                if ( reader.Read( ) )
                {
                    throw new IOException( "Invalid handoff journal header." );
                }

                reader.Close( );
                var records = ReadAll( );
                if ( records.Any( record => record.BootId != previousBoot ) )
                {
                    throw new IOException( "Invalid handoff journal boot identity." );
                }

                if ( previousBoot != bootId )
                {
                    foreach ( var record in records )
                    {
                        Save( record with
                        {
                            BootId = bootId,
                            Phase = ClipboardHandoffPhase.Retired,
                            Outcome = record.Outcome == ClipboardEffectOutcome.NotSent
                                ? ClipboardEffectOutcome.NotSent : ClipboardEffectOutcome.Unknown,
                            Work = ClipboardManagerWork.NoneProven,
                            WorkerReaped = true
                        } );
                    }

                    Execute( "UPDATE header SET boot=$boot,checksum=$checksum", ("$boot", bootId), ("$checksum", Digest( "1\n" + bootId )) );
                }

                return 0;
            } );
        }
        catch
        {
            _connection.Dispose( );
            throw;
        }
    }

    public IReadOnlyList<ClipboardHandoff> Snapshot( )
    {
        lock ( _gate )
        {
            EnsureAvailable( );
            return ReadAll( );
        }
    }

    public ClipboardHandoff? TryAdmit( Guid owner, string workerIdentity, string managerIdentity )
    {
        if ( owner == Guid.Empty )
        {
            throw new ArgumentException( "An owner is required.", nameof( owner ) );
        }

        ValidateIdentity( workerIdentity );
        ValidateIdentity( managerIdentity );
        return Mutate<ClipboardHandoff?>( ( ) =>
        {
            var occupied = ReadAll( ).Where( record => record.Charged ).Select( record => record.Slot ).ToHashSet( );
            var slot = Enumerable.Range( 0, 2 ).FirstOrDefault( value => !occupied.Contains( value ), -1 );
            if ( slot < 0 )
            {
                return null;
            }

            ClipboardHandoff record = new( slot, Guid.NewGuid( ), owner, _bootId,
                workerIdentity, managerIdentity, ClipboardHandoffPhase.Charged,
                ClipboardEffectOutcome.NotSent, ClipboardManagerWork.NoneProven, false );
            Save( record );
            _admitted.Add( record.Attempt );
            return record;
        } );
    }

    public bool TryArm( Guid attempt ) => Mutate( ( ) =>
    {
        var record = Find( attempt );
        if ( record is null || !_admitted.Contains( attempt ) ||
            record.Phase != ClipboardHandoffPhase.Charged || record.WorkerReaped )
        {
            return false;
        }

        Save( record with
        {
            Phase = ClipboardHandoffPhase.DispatchArmed,
            Outcome = ClipboardEffectOutcome.Unknown,
            Work = ClipboardManagerWork.CallInFlight
        } );
        return true;
    } );

    public void Observe( Guid attempt, ClipboardHandoffEvidence evidence ) => Mutate( ( ) =>
    {
        var record = Find( attempt );
        if ( record is null || record.Phase is ClipboardHandoffPhase.Charged or ClipboardHandoffPhase.Retired )
        {
            return 0;
        }

        if ( record.Work == ClipboardManagerWork.NoneProven )
        {
            return 0;
        }

        var (outcome, work) = evidence switch
        {
            ClipboardHandoffEvidence.ExecConfirmed => (ClipboardEffectOutcome.ExecConfirmed, ClipboardManagerWork.NoneProven),
            ClipboardHandoffEvidence.TerminalEmptyUnit => (ClipboardEffectOutcome.Unknown, ClipboardManagerWork.NoneProven),
            ClipboardHandoffEvidence.ProvenPreExecFailure => (ClipboardEffectOutcome.NoExec, ClipboardManagerWork.NoneProven),
            ClipboardHandoffEvidence.StartJobPending => (ClipboardEffectOutcome.Unknown, ClipboardManagerWork.JobPending),
            _ => (ClipboardEffectOutcome.Unknown, ClipboardManagerWork.Unverifiable)
        };
        Save( RetireIfReady( record with
        {
            Phase = ClipboardHandoffPhase.Observed,
            Outcome = outcome,
            Work = work
        } ) );
        return 0;
    } );

    public void WorkerReaped( Guid attempt ) => Mutate( ( ) =>
    {
        var record = Find( attempt );
        if ( record is not null && record.Charged )
        {
            Save( RetireIfReady( record with
            {
                WorkerReaped = true
            } ) );
        }

        return 0;
    } );

    private static ClipboardHandoff RetireIfReady( ClipboardHandoff record ) =>
        record.WorkerReaped && record.Work == ClipboardManagerWork.NoneProven
            ? record with
            {
                Phase = ClipboardHandoffPhase.Retired
            } : record;

    private ClipboardHandoff? Find( Guid attempt ) => ReadAll( ).SingleOrDefault( record => record.Attempt == attempt );

    private T Mutate<T>( Func<T> mutation )
    {
        lock ( _gate )
        {
            EnsureAvailable( );
            try
            {
                Execute( "BEGIN IMMEDIATE" );
                var result = mutation( );
                Execute( "COMMIT" );
                return result;
            }
            catch
            {
                _faulted = true;
                try
                {
                    Execute( "ROLLBACK" );
                }
                catch ( SqliteException ) { }

                throw;
            }
        }
    }

    private void EnsureAvailable( )
    {
        if ( _faulted )
        {
            throw new IOException( "Handoff journal requires recovery before further admission." );
        }
    }

    private List<ClipboardHandoff> ReadAll( )
    {
        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT slot,body,checksum FROM slots ORDER BY slot LIMIT 3";
        using var reader = command.ExecuteReader( );
        List<ClipboardHandoff> records = [];
        while ( reader.Read( ) )
        {
            var body = reader.GetString( 1 );
            if ( body.Length > 4096 || Digest( body ) != reader.GetString( 2 ) )
            {
                throw new IOException( "Handoff journal checksum failed; recovery required." );
            }

            records.Add( ParseRecord( body, reader.GetInt32( 0 ) ) );
        }

        if ( records.Count > 2 )
        {
            throw new IOException( "Handoff journal exceeds its slot bound." );
        }

        return records;
    }

    private static ClipboardHandoff ParseRecord( string body, int expectedSlot )
    {
        var fields = body.Split( '\n' );
        if ( fields.Length != 11 || !int.TryParse( fields[0], out var slot ) || slot != expectedSlot ||
            slot is < 0 or > 1 || !Guid.TryParseExact( fields[1], "N", out var attempt ) || attempt == Guid.Empty ||
            !Guid.TryParseExact( fields[2], "N", out var owner ) || owner == Guid.Empty ||
            !Enum.TryParse<ClipboardHandoffPhase>( fields[6], out var phase ) || !Enum.IsDefined( phase ) ||
            !Enum.TryParse<ClipboardEffectOutcome>( fields[7], out var outcome ) || !Enum.IsDefined( outcome ) ||
            !Enum.TryParse<ClipboardManagerWork>( fields[8], out var work ) || !Enum.IsDefined( work ) ||
            !bool.TryParse( fields[9], out var reaped ) || fields[10] != "1" )
        {
            throw new IOException( "Invalid handoff journal record." );
        }

        ValidateIdentity( fields[3] );
        ValidateIdentity( fields[4] );
        ValidateIdentity( fields[5] );
        return new( slot, attempt, owner, fields[3], fields[4], fields[5], phase, outcome, work, reaped );
    }

    private void Save( ClipboardHandoff record )
    {
        var body = string.Join( '\n', record.Slot.ToString( CultureInfo.InvariantCulture ),
            record.Attempt.ToString( "N" ), record.Owner.ToString( "N" ), record.BootId,
            record.WorkerIdentity, record.ManagerIdentity, record.Phase.ToString( ),
            record.Outcome.ToString( ), record.Work.ToString( ), record.WorkerReaped.ToString( ), "1" );
        Execute( "INSERT INTO slots VALUES($slot,$body,$checksum) ON CONFLICT(slot) DO UPDATE SET body=excluded.body,checksum=excluded.checksum",
            ("$slot", record.Slot), ("$body", body), ("$checksum", Digest( body )) );
    }

    private static string Digest( string body )
    {
        using MemoryStream bytes = new( );
        using ( BinaryWriter writer = new( bytes, Encoding.UTF8, leaveOpen: true ) )
        {
            foreach ( var field in body.Split( '\n' ) )
            {
                writer.Write( field );
            }
        }

        return Convert.ToHexString( SHA256.HashData( bytes.GetBuffer( ).AsSpan( 0, checked(( int ) bytes.Length) ) ) );
    }

    private static void ValidateIdentity( string identity )
    {
        if ( string.IsNullOrEmpty( identity ) || identity.Length > 256 ||
            identity.Any( character => !char.IsAsciiLetterOrDigit( character ) && character is not ':' and not '-' and not '.' ) )
        {
            throw new ArgumentException( "Invalid handoff bookkeeping identity." );
        }
    }

    private void Execute( string sql, params (string Name, object Value)[] parameters )
    {
        using var command = _connection.CreateCommand( );
        command.CommandText = sql;
        foreach ( var (name, value) in parameters )
        {
            command.Parameters.AddWithValue( name, value );
        }

        command.ExecuteNonQuery( );
    }

    public void Dispose( )
    {
        lock ( _gate )
        {
            _connection.Dispose( );
        }
    }
}
