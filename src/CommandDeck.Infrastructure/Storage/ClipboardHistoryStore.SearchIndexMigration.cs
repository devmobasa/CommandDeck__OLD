namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    /// <summary>
    /// Migrates schema version 3 to 4: drops the unused full-text index and the
    /// triggers that fed it. With secure_delete on, SQLite zeroes every page the
    /// index frees, including terms of entries deleted long before.
    /// </summary>
    private void RemoveSearchIndex( )
    {
        if ( SchemaVersion >= CurrentSchemaVersion )
        {
            return;
        }

        RequireMigrationSpace( );
        Transaction( ( ) =>
        {
            Execute( """
                DROP TRIGGER clipboard_entry_insert;
                DROP TRIGGER clipboard_entry_update;
                DROP TRIGGER clipboard_entry_delete;
                DROP TABLE clipboard_fts;
                """ );
            foreach ( var trigger in PayloadTriggers )
            {
                Execute( trigger );
            }

            Execute( """
                INSERT INTO clipboard_migrations SELECT 4,(SELECT count(*) FROM entries),original_capacity,
                    maximum_entries,quota_mib FROM clipboard_state CROSS JOIN clipboard_policy;
                PRAGMA user_version=4;
                """ );
            return 0;
        } );
    }
}
