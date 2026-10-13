namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    private const string PlainMime = "text/plain;charset=utf-8";

    private void RequireMigrationSpace( )
    {
        var bytes = Scalar( "SELECT coalesce(sum(length(cast(text AS blob))),0) FROM entries" );
        var count = Scalar( "SELECT count(*) FROM entries" );
        var databaseBytes = checked(Scalar( "PRAGMA page_count" ) * Scalar( "PRAGMA page_size" ));
        // Source, rollback journal, payloads, FTS/projection and per-file allocation slack.
        var required = checked(2 * databaseBytes + 3 * bytes + count * 8192 + 16L * 1024 * 1024);
        var available = _migrationFreeSpace?.Invoke( ) ?? _payloadDirectory?.AvailableFreeSpace ??
            new DriveInfo( Path.GetDirectoryName( _path )! ).AvailableFreeSpace;
        if ( available < required )
        {
            throw new IOException( "Clipboard migration needs additional free disk space; retained history was preserved." );
        }
    }

    private void InitializePayloadStorage( )
    {
        var current = Scalar( "PRAGMA user_version" ) >= 2;
        if ( !current )
        {
            RequireMigrationSpace( );
        }

        _payloadDirectory = new( _path + ".payloads" );
        _payloadLedger = new( _connection, _payloadDirectory, _gate );
        if ( current )
        {
            RecoverPayloads( );
            return;
        }

        if ( Scalar( "SELECT count(*) FROM sqlite_master WHERE name='clipboard_payload_migration'" ) == 0 )
        {
            Transaction( ( ) =>
            {
                _payloadLedger.CreateSchema( );
                Execute( """
                    CREATE TABLE clipboard_payload_migration(revision INTEGER NOT NULL);
                    INSERT INTO clipboard_payload_migration SELECT revision FROM clipboard_state;
                    CREATE TABLE clipboard_migrated_entries(
                        id TEXT PRIMARY KEY REFERENCES entries(id),
                        payload_id TEXT NOT NULL UNIQUE REFERENCES clipboard_payloads(id),dedup_key TEXT NOT NULL);
                    """ );
                return 0;
            } );
            _migrationCheckpoint?.Invoke( "migration-marked" );
        }

        ValidateMigrationProgress( );
        _payloadLedger.Recover( ( _, damaged ) =>
        {
            foreach ( var id in damaged )
            {
                Execute( "DELETE FROM clipboard_migrated_entries WHERE payload_id=$id", ("$id", id.ToString( "N" )) );
            }
        } );
        _payloadLedger.Cleanup( );
        ValidateMigrationProgress( );
        MigrateTextPayloads( );

        // This step still produces exactly schema version 2, including the
        // full-text index that RemoveSearchIndex drops on the way to version 4.
        Transaction( ( ) =>
        {
            Execute( SelectionSchema );
            Execute( SearchIndexSchema );
            Execute( """
                INSERT INTO clipboard_selections SELECT id,'text',$mime,dedup_key FROM clipboard_migrated_entries;
                INSERT INTO clipboard_offers SELECT id,payload_id,$mime,0 FROM clipboard_migrated_entries;
                INSERT INTO clipboard_fts(rowid,text,keywords) SELECT rowid,text,keywords FROM entries;
                CREATE TABLE clipboard_migrations(version INTEGER PRIMARY KEY,entry_count INTEGER NOT NULL,
                    original_capacity INTEGER NOT NULL,effective_capacity INTEGER NOT NULL,quota_mib INTEGER NOT NULL);
                INSERT INTO clipboard_migrations SELECT 2,(SELECT count(*) FROM entries),original_capacity,
                    maximum_entries,quota_mib FROM clipboard_state CROSS JOIN clipboard_policy;
                UPDATE clipboard_state SET migration_review=1 WHERE EXISTS(
                    SELECT 1 FROM entries WHERE pinned=0 AND copied<$cutoff);
                DROP TABLE clipboard_migrated_entries;
                DROP TABLE clipboard_payload_migration;
                PRAGMA user_version=2;
                """, ("$mime", PlainMime),
                ("$cutoff", _time.GetUtcNow( ).AddHours( -ReadPolicy( ).RetentionHours ).ToUnixTimeSeconds( )) );
            foreach ( var trigger in SearchIndexPayloadTriggers )
            {
                Execute( trigger );
            }

            return 0;
        } );
        _migrationCheckpoint?.Invoke( "schema-switched" );
    }

    private void MigrateTextPayloads( )
    {
        while ( true )
        {
            using var command = _connection.CreateCommand( );
            command.CommandText = "SELECT id,cast(text AS blob) FROM entries WHERE id NOT IN (SELECT id FROM clipboard_migrated_entries) ORDER BY id LIMIT 1";
            using var reader = command.ExecuteReader( );
            if ( !reader.Read( ) )
            {
                return;
            }

            var id = reader.GetString( 0 );
            var bytes = reader.GetFieldValue<byte[]>( 1 );
            reader.Close( );
            WriteTextPayload( bytes, ( payload, digest ) => Execute(
                "INSERT INTO clipboard_migrated_entries VALUES($id,$payload,$digest)",
                ("$id", id), ("$payload", payload.Id.ToString( "N" )), ("$digest", digest) ),
                ( ) => Scalar( "SELECT revision FROM clipboard_payload_migration" ) == StoreRevision );
            _migrationCheckpoint?.Invoke( "row-migrated" );
        }
    }

    private void WriteTextPayload( byte[] bytes, Action<ClipboardCompletedPayload, string> publish, Func<bool> canPublish )
    {
        var ledger = _payloadLedger!;
        ledger.Cleanup( );
        using var content = new MemoryStream( bytes, writable: false );
        var digest = ClipboardOfferDigest.Compute( [new( PlainMime, bytes.Length, content )] );
        using var reservation = ledger.Reserve( ClipboardOfferPlan.Create( [PlainMime], ClipboardConsent.Text ),
            ( long ) ReadPolicy( ).QuotaMiB * 1024 * 1024 );
        using var writer = ledger.OpenWriter( reservation, 0 );
        writer.WriteAsync( bytes, CancellationToken.None ).AsTask( ).GetAwaiter( ).GetResult( );
        var payload = writer.Complete( );
        ledger.Publish( reservation, new HashSet<Guid> { payload.Id }, canPublish, ( _, _ ) => publish( payload, digest ) );
    }

    private void ValidateMigrationProgress( )
    {
        if ( Scalar( "SELECT revision FROM clipboard_payload_migration" ) != StoreRevision ||
            Scalar( "SELECT count(*) FROM clipboard_payload_migration" ) != 1 ||
            Scalar( """
                SELECT count(*) FROM clipboard_migrated_entries m
                LEFT JOIN entries e ON e.id=m.id LEFT JOIN clipboard_payloads p ON p.id=m.payload_id
                WHERE e.id IS NULL OR p.id IS NULL OR p.state!=1 OR p.actual!=length(cast(e.text AS blob)) OR p.digest!=lower(e.id)
                """ ) != 0 ||
            Scalar( "SELECT count(*) FROM clipboard_payloads WHERE state=1 AND id NOT IN (SELECT payload_id FROM clipboard_migrated_entries)" ) != 0 )
        {
            throw new IOException( "Clipboard migration progress no longer matches preserved source history." );
        }

        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT cast(e.text AS blob),m.dedup_key FROM entries e JOIN clipboard_migrated_entries m ON e.id=m.id";
        using var reader = command.ExecuteReader( );
        while ( reader.Read( ) )
        {
            var bytes = reader.GetFieldValue<byte[]>( 0 );
            using var stream = new MemoryStream( bytes, writable: false );
            if ( reader.GetString( 1 ) != ClipboardOfferDigest.Compute( [new( PlainMime, bytes.Length, stream )] ) )
            {
                throw new IOException( "Clipboard migration digest does not match preserved source history." );
            }
        }
    }

    private void RecoverPayloads( )
    {
        _payloadLedger!.Recover( ( _, damaged ) =>
        {
            foreach ( var id in damaged )
            {
                Execute( "DELETE FROM entries WHERE id IN (SELECT entry_id FROM clipboard_offers WHERE payload_id=$id)",
                    ("$id", id.ToString( "N" )) );
            }

            if ( damaged.Count > 0 )
            {
                AdvanceRevision( );
            }
        } );
        _payloadLedger.Cleanup( );
    }
}
