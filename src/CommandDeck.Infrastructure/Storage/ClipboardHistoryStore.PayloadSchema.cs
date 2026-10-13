namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    // Version 2 moved offers into payload files and added a full-text index,
    // version 3 added rich projections, and version 4 dropped the index again.
    private const long RichSchemaVersion = 3;
    private const long CurrentSchemaVersion = 4;

    private const string SelectionSchema = """
        CREATE TABLE clipboard_selections(
            entry_id TEXT PRIMARY KEY REFERENCES entries(id) ON DELETE CASCADE,
            kind TEXT NOT NULL,preferred_mime TEXT NOT NULL,dedup_key TEXT NOT NULL);
        CREATE TABLE clipboard_offers(
            entry_id TEXT NOT NULL REFERENCES entries(id) ON DELETE CASCADE,
            payload_id TEXT NOT NULL UNIQUE REFERENCES clipboard_payloads(id),mime TEXT NOT NULL,
            offer_index INTEGER NOT NULL,PRIMARY KEY(entry_id,mime));
        """;

    private static readonly string[] PayloadTriggers =
    [
        """
        CREATE TRIGGER clipboard_entry_delete BEFORE DELETE ON entries BEGIN
            UPDATE clipboard_payloads SET state=2 WHERE id IN (SELECT payload_id FROM clipboard_offers WHERE entry_id=old.id);
        END
        """
    ];

    // Schema versions 2 and 3 only: nothing ever queried this index, and FTS5
    // keeps a deleted row's terms in older segments until a merge, so deleted
    // clipboard text stayed readable in the database file.
    internal const string SearchIndexSchema = "CREATE VIRTUAL TABLE clipboard_fts USING fts5(text,keywords,tokenize='unicode61')";

    private static readonly string[] SearchIndexTables =
        ["clipboard_fts", "clipboard_fts_data", "clipboard_fts_idx", "clipboard_fts_content", "clipboard_fts_docsize", "clipboard_fts_config"];

    internal static readonly string[] SearchIndexPayloadTriggers =
    [
        """
        CREATE TRIGGER clipboard_entry_delete BEFORE DELETE ON entries BEGIN
            UPDATE clipboard_payloads SET state=2 WHERE id IN (SELECT payload_id FROM clipboard_offers WHERE entry_id=old.id);
            DELETE FROM clipboard_fts WHERE rowid=old.rowid;
        END
        """,
        """
        CREATE TRIGGER clipboard_entry_insert AFTER INSERT ON entries BEGIN
            INSERT INTO clipboard_fts(rowid,text,keywords) VALUES(new.rowid,new.text,new.keywords);
        END
        """,
        """
        CREATE TRIGGER clipboard_entry_update AFTER UPDATE OF text,keywords ON entries BEGIN
            DELETE FROM clipboard_fts WHERE rowid=old.rowid;
            INSERT INTO clipboard_fts(rowid,text,keywords) VALUES(new.rowid,new.text,new.keywords);
        END
        """
    ];

    private long SchemaVersion => Scalar( "PRAGMA user_version" );

    private bool HasRichSchema => SchemaVersion >= RichSchemaVersion;

    private void ValidatePayloadSchema( bool current )
    {
        var searchIndex = current && SchemaVersion < CurrentSchemaVersion;
        string[] expected = current
            ? ["entries", "clipboard_policy", "clipboard_state", "clipboard_payloads", "clipboard_selections", "clipboard_offers",
                "clipboard_migrations"]
            : ["entries", "clipboard_policy", "clipboard_state", "clipboard_payloads", "clipboard_payload_migration", "clipboard_migrated_entries"];
        if ( searchIndex )
        {
            expected = [.. expected, .. SearchIndexTables];
        }

        if ( current && HasRichSchema )
        {
            expected = [.. expected, "clipboard_projections"];
        }

        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var reader = command.ExecuteReader( );
        List<string> names = [];
        while ( reader.Read( ) )
        {
            names.Add( reader.GetString( 0 ) );
        }

        reader.Close( );
        if ( !names.SequenceEqual( expected.Order( StringComparer.Ordinal ), StringComparer.Ordinal ) ||
            Scalar( "SELECT count(*) FROM sqlite_master WHERE type='view'" ) != 0 )
        {
            throw new IOException( "Unknown clipboard payload schema; existing data was preserved." );
        }

        RequireColumns( "clipboard_payloads", ["id", "selection_id", "state", "charge", "actual", "digest"] );
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='trigger'";
        using var triggers = command.ExecuteReader( );
        var actualTriggers = new HashSet<string>( StringComparer.Ordinal );
        while ( triggers.Read( ) )
        {
            actualTriggers.Add( triggers.GetString( 0 ) );
        }

        triggers.Close( );
        string[] expectedTriggers = !current ? [] : searchIndex ? SearchIndexPayloadTriggers : PayloadTriggers;
        if ( !actualTriggers.SetEquals( expectedTriggers ) )
        {
            throw new IOException( "Unknown clipboard mutation triggers; existing data was preserved." );
        }

        if ( !current )
        {
            RequireColumns( "clipboard_payload_migration", ["revision"] );
            RequireColumns( "clipboard_migrated_entries", ["id", "payload_id", "dedup_key"] );
            return;
        }

        RequireColumns( "clipboard_selections", ["entry_id", "kind", "preferred_mime", "dedup_key"] );
        RequireColumns( "clipboard_offers", ["entry_id", "payload_id", "mime", "offer_index"] );
        RequireColumns( "clipboard_migrations", ["version", "entry_count", "original_capacity", "effective_capacity", "quota_mib"] );
        if ( HasRichSchema )
        {
            ValidateProjectionSchema( );
        }

        string[] definitions = SelectionSchema.Split( ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
        if ( searchIndex )
        {
            definitions = [.. definitions, SearchIndexSchema];
        }

        foreach ( var definition in definitions )
        {
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE sql=$sql";
            command.Parameters.Clear( );
            command.Parameters.AddWithValue( "$sql", definition );
            if ( ( long ) command.ExecuteScalar( )! != 1 )
            {
                throw new IOException( "Clipboard payload relationships or search schema changed; existing data was preserved." );
            }
        }
    }

    private void ValidateSearchIndexRows( )
    {
        if ( SchemaVersion >= CurrentSchemaVersion )
        {
            return;
        }

        if ( Scalar( "SELECT count(*) FROM clipboard_fts" ) != Scalar( "SELECT count(*) FROM entries" ) ||
            Scalar( "SELECT count(*) FROM entries e LEFT JOIN clipboard_fts f ON e.rowid=f.rowid WHERE f.rowid IS NULL OR f.text!=e.text OR f.keywords!=e.keywords" ) != 0 )
        {
            throw new IOException( "Clipboard search projections are invalid; existing data was preserved." );
        }
    }

    private void ValidateTextPayloadReferences( )
    {
        ValidateSearchIndexRows( );
        if ( HasRichSchema )
        {
            ValidateRichPayloadReferences( );
            return;
        }

        // Plain aliases retain their exact advertised names. Rich kinds require their own validators.
        if ( Scalar( $"""
            SELECT count(*) FROM entries e LEFT JOIN clipboard_selections s ON s.entry_id=e.id
            LEFT JOIN clipboard_offers o ON o.entry_id=e.id LEFT JOIN clipboard_payloads p ON p.id=o.payload_id
            WHERE s.entry_id IS NULL OR s.kind!='{ClipboardPresentationKinds.Text}' OR typeof(s.preferred_mime)!='text' OR
                typeof(s.dedup_key)!='text' OR length(s.dedup_key)!=64 OR o.entry_id IS NULL OR
                typeof(o.mime)!='text' OR typeof(o.offer_index)!='integer' OR o.offer_index NOT BETWEEN 0 AND 511 OR
                p.id IS NULL OR p.state!=1 OR p.actual!=length(cast(e.text AS blob)) OR p.digest!=lower(e.id)
            """ ) != 0 ||
            Scalar( "SELECT count(*) FROM entries e WHERE (SELECT count(*) FROM clipboard_offers o WHERE o.entry_id=e.id) NOT BETWEEN 1 AND 2" ) != 0 ||
            Scalar( "SELECT count(*) FROM (SELECT entry_id,offer_index FROM clipboard_offers GROUP BY entry_id,offer_index HAVING count(*)>1)" ) != 0 ||
            Scalar( "SELECT count(*) FROM clipboard_selections" ) != Scalar( "SELECT count(*) FROM entries" ) ||
            Scalar( "SELECT count(*) FROM clipboard_payloads WHERE state=1 AND id NOT IN (SELECT payload_id FROM clipboard_offers)" ) != 0 )
        {
            throw new IOException( "Clipboard payload references are invalid; existing data was preserved." );
        }

        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT e.id,cast(e.text AS blob),s.dedup_key,s.preferred_mime,o.mime FROM entries e JOIN clipboard_selections s ON e.id=s.entry_id JOIN clipboard_offers o ON e.id=o.entry_id ORDER BY e.id,o.mime";
        using var reader = command.ExecuteReader( );
        string? id = null;
        byte[] bytes = [];
        string digest = "", preferred = "";
        List<string> names = [];
        while ( reader.Read( ) )
        {
            var next = reader.GetString( 0 );
            if ( id is not null && id != next )
            {
                ValidateAliases( );
                names.Clear( );
            }

            id = next;
            bytes = reader.GetFieldValue<byte[]>( 1 );
            digest = reader.GetString( 2 );
            preferred = reader.GetString( 3 );
            names.Add( reader.GetString( 4 ) );
        }

        if ( id is not null )
        {
            ValidateAliases( );
        }

        void ValidateAliases( )
        {
            var plan = ClipboardOfferPlan.Create( names, ClipboardConsent.Text );
            if ( !plan.Accepted || plan.Offers.Count != names.Count || plan.Offers.Any( static offer => offer.Kind != ClipboardOfferKind.PlainText ) ||
                !names.Contains( preferred, StringComparer.Ordinal ) || digest != TextOfferDigest( bytes, names ) )
            {
                throw new IOException( "Clipboard retained aliases or digest are invalid; existing data was preserved." );
            }
        }
    }
}
