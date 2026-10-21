namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    private static DatabaseMigration QuicklinksMigration => new( 23, "User quicklinks and configured quicklink exclusions", """
        CREATE TABLE IF NOT EXISTS user_quicklinks (
            id TEXT PRIMARY KEY COLLATE NOCASE,
            title TEXT NOT NULL,
            url_template TEXT NOT NULL,
            alias TEXT COLLATE NOCASE UNIQUE,
            icon TEXT,
            created_unix_ms INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS disabled_configured_quicklinks (
            id TEXT PRIMARY KEY COLLATE NOCASE
        );
        CREATE INDEX IF NOT EXISTS ix_catalog_items_quicklink_alias
            ON catalog_items(json_extract(payload_json, '$.alias') COLLATE NOCASE)
            WHERE kind = 'quicklink';
        """ );
}
