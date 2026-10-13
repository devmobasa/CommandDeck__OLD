using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    private void InitializePolicy( bool allowNewDatabase )
    {
        Execute( "PRAGMA synchronous=FULL; PRAGMA secure_delete=ON; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=1000" );
        var version = Scalar( "PRAGMA user_version" );
        if ( version is < 0 or > CurrentSchemaVersion )
        {
            throw new IOException( "This clipboard history requires a newer CommandDeck version." );
        }

        if ( version >= 2 )
        {
            ValidateCurrentSchema( payloadSchema: true );
            return;
        }

        if ( Scalar( "SELECT count(*) FROM sqlite_master WHERE type IN ('trigger','view')" ) != 0 )
        {
            throw new IOException( "Unknown clipboard schema objects; existing data was preserved." );
        }

        if ( version == 1 )
        {
            ValidateCurrentSchema( migrating: Scalar( "SELECT count(*) FROM sqlite_master WHERE name='clipboard_payload_migration'" ) != 0 );
            Execute( "PRAGMA journal_mode=DELETE" );
            return;
        }

        Transaction( ( ) =>
        {
            var existing = Scalar( "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'" );
            if ( existing == 0 )
            {
                if ( !allowNewDatabase )
                {
                    throw new IOException( "Clipboard history has no recognized schema; existing file was preserved." );
                }

                Execute( "CREATE TABLE settings(enabled INTEGER NOT NULL); INSERT INTO settings VALUES(0); " +
                    "CREATE TABLE entries(id TEXT PRIMARY KEY,text TEXT NOT NULL,copied INTEGER NOT NULL,pinned INTEGER NOT NULL DEFAULT 0)" );
            }
            else if ( existing != 2 )
            {
                throw new IOException( "Unknown clipboard history schema; existing data was preserved." );
            }

            RequireColumns( "settings", ["enabled"] );
            RequireColumns( "entries", ["id", "text", "copied", "pinned"] );
            if ( Scalar( "SELECT count(*) FROM settings" ) != 1 || Scalar( "SELECT count(*) FROM settings WHERE typeof(enabled)!='integer' OR enabled NOT IN (0,1)" ) != 0 )
            {
                throw new IOException( "Invalid clipboard capture settings; existing data was preserved." );
            }

            var (count, pins, expired) = ValidateLegacyEntries( );
            RequireMigrationSpace( );
            var capacity = Math.Max( _options.MaximumEntries, Math.Max( count, checked(pins * 2) ) );
            if ( capacity > 1000 )
            {
                throw new IOException( "Clipboard migration needs more than 1000 entries of capacity; existing data was preserved." );
            }

            Execute( """
                ALTER TABLE entries ADD COLUMN incarnation TEXT NOT NULL DEFAULT '';
                ALTER TABLE entries ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;
                ALTER TABLE entries ADD COLUMN keywords TEXT NOT NULL DEFAULT '[]';
                UPDATE entries SET incarnation=lower(hex(randomblob(16)));
                CREATE TABLE clipboard_policy(
                    revision INTEGER NOT NULL,consent INTEGER NOT NULL,paused INTEGER NOT NULL,
                    maximum_entries INTEGER NOT NULL,retention_hours INTEGER NOT NULL,
                    quota_mib INTEGER NOT NULL,clear_on_startup INTEGER NOT NULL);
                INSERT INTO clipboard_policy SELECT 1,enabled,0,$capacity,$age,256,0 FROM settings;
                DROP TABLE settings;
                CREATE TABLE clipboard_state(revision INTEGER NOT NULL,migration_review INTEGER NOT NULL,original_capacity INTEGER NOT NULL);
                INSERT INTO clipboard_state VALUES(1,$review,$original);
                PRAGMA user_version=1;
                """, ("$capacity", capacity), ("$age", _options.RetentionHours),
                ("$review", capacity != _options.MaximumEntries || expired > 0 ? 1 : 0), ("$original", _options.MaximumEntries) );
            return 0;
        } );

        Execute( "PRAGMA journal_mode=DELETE" );
    }

    private (int Count, int Pins, int Expired) ValidateLegacyEntries( bool allowRich = false )
    {
        var identityLengths = allowRich ? "(32,64)" : "(64)";
        var maximumText = allowRich ? 65536 : 32768;
        RequireValidRows( "entries", $"typeof(id)!='text' OR length(id) NOT IN {identityLengths} OR typeof(text)!='text' OR typeof(copied)!='integer' OR copied NOT BETWEEN -62135596800 AND 253402300799 OR typeof(pinned)!='integer' OR length(cast(text AS blob))>{maximumText}" );
        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT id,cast(text AS blob),copied,pinned FROM entries LIMIT 1001";
        using var reader = command.ExecuteReader( );
        var count = 0;
        var pins = 0;
        var expired = 0;
        var cutoff = _time.GetUtcNow( ).AddHours( -_options.RetentionHours ).ToUnixTimeSeconds( );
        while ( reader.Read( ) )
        {
            var id = reader.GetString( 0 );
            var bytes = reader.GetFieldValue<byte[]>( 1 );
            string text;
            try
            {
                text = StrictUtf8.GetString( bytes );
            }
            catch ( DecoderFallbackException )
            {
                throw new IOException( "Clipboard text contains invalid UTF-8; existing data was preserved." );
            }
            var pinned = reader.GetInt64( 3 );
            var richIdentity = allowRich && Guid.TryParseExact( id, "N", out var parsed ) && parsed != Guid.Empty && id == parsed.ToString( "N" );
            if ( ++count > 1000 || bytes.Length > maximumText || text.Contains( '\0' ) || pinned is < 0 or > 1 ||
                !richIdentity && (bytes.Length is 0 or > 32768 || id != Convert.ToHexString( SHA256.HashData( bytes ) )) )
            {
                throw new IOException( "Invalid clipboard entry or migration capacity exceeded; existing data was preserved." );
            }

            var copied = reader.GetInt64( 2 );
            _ = DateTimeOffset.FromUnixTimeSeconds( copied );
            pins += ( int ) pinned;
            if ( pinned == 0 && copied < cutoff )
            {
                expired++;
            }
        }

        return (count, pins, expired);
    }

    private void RequireColumns( string table, string[] expected )
    {
        using var command = _connection.CreateCommand( );
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader( );
        List<string> columns = [];
        while ( reader.Read( ) )
        {
            columns.Add( reader.GetString( 1 ) );
        }

        if ( !columns.SequenceEqual( expected, StringComparer.Ordinal ) )
        {
            throw new IOException( "Unknown clipboard history schema; existing data was preserved." );
        }
    }

    private long Scalar( string sql )
    {
        using var command = _connection.CreateCommand( );
        command.CommandText = sql;
        var value = command.ExecuteScalar( );
        if ( value is null or DBNull )
        {
            throw new IOException( "Clipboard history state is missing." );
        }

        return Convert.ToInt64( value, System.Globalization.CultureInfo.InvariantCulture );
    }

    private T Transaction<T>( Func<T> mutation )
    {
        Execute( "BEGIN IMMEDIATE" );
        try
        {
            var result = mutation( );
            Execute( "COMMIT" );
            return result;
        }
        catch
        {
            Execute( "ROLLBACK" );
            throw;
        }
    }
}
