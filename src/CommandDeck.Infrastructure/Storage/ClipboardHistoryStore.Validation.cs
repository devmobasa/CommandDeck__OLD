using System.Text.Json;

namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    private void ValidateCurrentSchema( bool payloadSchema = false, bool migrating = false )
    {
        if ( payloadSchema || migrating )
        {
            ValidatePayloadSchema( payloadSchema );
        }
        else if ( Scalar( "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'" ) != 3 ||
            Scalar( "SELECT count(*) FROM sqlite_master WHERE type IN ('trigger','view')" ) != 0 )
        {
            throw new IOException( "Unknown clipboard history schema; existing data was preserved." );
        }

        RequireColumns( "entries", ["id", "text", "copied", "pinned", "incarnation", "revision", "keywords"] );
        RequireColumns( "clipboard_policy", ["revision", "consent", "paused", "maximum_entries", "retention_hours", "quota_mib", "clear_on_startup"] );
        RequireColumns( "clipboard_state", ["revision", "migration_review", "original_capacity"] );
        RequireValidRows( "clipboard_policy", "typeof(revision)!='integer' OR typeof(consent)!='integer' OR typeof(paused)!='integer' OR typeof(maximum_entries)!='integer' OR typeof(retention_hours)!='integer' OR typeof(quota_mib)!='integer' OR typeof(clear_on_startup)!='integer'" );
        RequireValidRows( "clipboard_state", "typeof(revision)!='integer' OR revision<1 OR typeof(migration_review)!='integer' OR migration_review NOT IN (0,1) OR typeof(original_capacity)!='integer' OR original_capacity NOT BETWEEN 10 AND 1000" );
        if ( Scalar( "SELECT count(*) FROM clipboard_state" ) != 1 )
        {
            throw new IOException( "Clipboard history state is missing or duplicated." );
        }

        var policy = ReadPolicy( );
        var (count, pins, _) = ValidateLegacyEntries( allowRich: payloadSchema && HasRichSchema );
        if ( count > policy.MaximumEntries || pins > policy.MaximumEntries / 2 )
        {
            throw new IOException( "Clipboard history exceeds its persisted limits; existing data was preserved." );
        }

        RequireValidRows( "entries", "typeof(incarnation)!='text' OR length(incarnation)!=32 OR typeof(revision)!='integer' OR revision<1 OR typeof(keywords)!='text' OR length(cast(keywords AS blob))>8192" );
        ValidateEntryMetadata( );
        if ( payloadSchema )
        {
            ValidateTextPayloadReferences( );
            if ( HasRichSchema )
            {
                ValidateProjectionRows( );
            }
        }

        using var command = _connection.CreateCommand( );
        command.CommandText = "PRAGMA quick_check";
        if ( command.ExecuteScalar( ) as string != "ok" )
        {
            throw new IOException( "Clipboard history integrity check failed; existing data was preserved." );
        }
    }

    private void ValidateEntryMetadata( )
    {
        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT id,incarnation,keywords FROM entries LIMIT 1001";
        using var reader = command.ExecuteReader( );
        HashSet<string> ids = new( StringComparer.Ordinal );
        HashSet<string> incarnations = new( StringComparer.Ordinal );
        while ( reader.Read( ) )
        {
            var incarnation = reader.GetString( 1 );
            if ( !ids.Add( reader.GetString( 0 ) ) || !Guid.TryParseExact( incarnation, "N", out var parsed ) || parsed == Guid.Empty || !incarnations.Add( incarnation ) )
            {
                throw new IOException( "Clipboard entry identity is invalid; existing data was preserved." );
            }

            try
            {
                var json = reader.GetString( 2 );
                var values = JsonSerializer.Deserialize( json, CommandDeckJsonContext.Default.StringArray );
                if ( values is null || !values.SequenceEqual( NormalizeKeywords( values ), StringComparer.Ordinal ) )
                {
                    throw new IOException( "Clipboard keywords are not canonical; existing data was preserved." );
                }
            }
            catch ( Exception exception ) when ( exception is JsonException or InvalidOperationException )
            {
                throw new IOException( "Clipboard keywords are invalid; existing data was preserved." );
            }
        }
    }

    private void RequireValidRows( string table, string invalidPredicate )
    {
        if ( Scalar( $"SELECT count(*) FROM {table} WHERE {invalidPredicate}" ) != 0 )
        {
            throw new IOException( "Clipboard history contains invalid stored values; existing data was preserved." );
        }
    }
}
