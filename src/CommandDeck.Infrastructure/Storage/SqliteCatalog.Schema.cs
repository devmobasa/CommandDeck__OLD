using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    private const string BootstrapSchema = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS schema_migrations (
            version INTEGER PRIMARY KEY,
            applied_unix_ms INTEGER NOT NULL,
            description TEXT NOT NULL
        );
        """;

    private static readonly DatabaseMigration[] Migrations =
    [
        new(1, "Initial catalog schema", """
            CREATE TABLE IF NOT EXISTS catalog_items (
                stable_id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                title TEXT NOT NULL,
                subtitle TEXT,
                path TEXT,
                icon TEXT,
                payload_json TEXT,
                size_bytes INTEGER,
                modified_unix_ms INTEGER,
                provider_id TEXT NOT NULL
            ) WITHOUT ROWID;

            CREATE VIRTUAL TABLE IF NOT EXISTS catalog_fts USING fts5(
                stable_id UNINDEXED,
                kind UNINDEXED,
                title,
                subtitle,
                path,
                body,
                config_keys,
                tokenize = 'unicode61 remove_diacritics 2'
            );

            CREATE TABLE IF NOT EXISTS usage (
                stable_id TEXT PRIMARY KEY,
                use_count INTEGER NOT NULL DEFAULT 0,
                last_used_unix_ms INTEGER
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS invocation_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                stable_id TEXT NOT NULL,
                action_id TEXT NOT NULL,
                query_text TEXT,
                invoked_unix_ms INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_invocation_events_time
                ON invocation_events(invoked_unix_ms);
            """),
        new(2, "Index application window classes", """
            ALTER TABLE catalog_items ADD COLUMN startup_wm_class TEXT;

            CREATE TABLE catalog_state (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            ) WITHOUT ROWID;

            INSERT INTO catalog_state(key, value)
            VALUES('reindex_required', '1');

            CREATE INDEX ix_catalog_items_stable_id_nocase
                ON catalog_items(stable_id COLLATE NOCASE);

            CREATE INDEX ix_catalog_items_startup_wm_class
                ON catalog_items(startup_wm_class COLLATE NOCASE)
                WHERE startup_wm_class IS NOT NULL;
            """),
        new(3, "Repair catalog rebuild state", """
            CREATE TABLE IF NOT EXISTS catalog_state (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            ) WITHOUT ROWID;

            INSERT INTO catalog_state(key, value)
            VALUES('reindex_required', '1')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """),
        new(4, "Launcher sources and customization", """
            ALTER TABLE catalog_items ADD COLUMN source_id TEXT NOT NULL DEFAULT 'status';
            CREATE INDEX ix_catalog_items_source ON catalog_items(source_id);

            DROP TABLE IF EXISTS catalog_fts;
            CREATE VIRTUAL TABLE catalog_fts USING fts5(
                stable_id UNINDEXED,
                kind UNINDEXED,
                source_id,
                title,
                subtitle,
                path,
                body,
                config_keys,
                tokenize = 'unicode61 remove_diacritics 2'
            );

            CREATE TABLE favorites (
                stable_id TEXT PRIMARY KEY,
                position INTEGER NOT NULL,
                title_snapshot TEXT NOT NULL,
                source_id TEXT NOT NULL,
                icon_snapshot TEXT,
                created_unix_ms INTEGER NOT NULL,
                updated_unix_ms INTEGER NOT NULL
            ) WITHOUT ROWID;
            CREATE UNIQUE INDEX ix_favorites_position ON favorites(position);

            CREATE TABLE aliases (
                alias TEXT COLLATE NOCASE PRIMARY KEY,
                stable_id TEXT NOT NULL,
                created_unix_ms INTEGER NOT NULL,
                updated_unix_ms INTEGER NOT NULL
            ) WITHOUT ROWID;
            CREATE INDEX ix_aliases_stable_id ON aliases(stable_id);

            CREATE TABLE source_preferences (
                source_id TEXT PRIMARY KEY,
                enabled_in_search INTEGER NOT NULL CHECK(enabled_in_search IN (0, 1)),
                enabled_on_home INTEGER NOT NULL CHECK(enabled_on_home IN (0, 1)),
                position INTEGER NOT NULL,
                updated_unix_ms INTEGER NOT NULL
            ) WITHOUT ROWID;
            CREATE UNIQUE INDEX ix_source_preferences_position
                ON source_preferences(position);
            CREATE TABLE IF NOT EXISTS usage (
                stable_id TEXT PRIMARY KEY,
                use_count INTEGER NOT NULL DEFAULT 0,
                last_used_unix_ms INTEGER
            ) WITHOUT ROWID;
            CREATE INDEX ix_usage_last_used
                ON usage(last_used_unix_ms DESC);

            CREATE TABLE launcher_preferences (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_unix_ms INTEGER NOT NULL
            ) WITHOUT ROWID;

            INSERT INTO catalog_state(key, value)
            VALUES('reindex_required', '1')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """),
        new(5, "Bounded index exclusion diagnostics", """
            CREATE TABLE index_exclusion_diagnostics (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                root TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                reason TEXT NOT NULL,
                rule TEXT
            );
            CREATE INDEX ix_index_exclusion_diagnostics_reason
                ON index_exclusion_diagnostics(reason);
            """),
        new(6, "Durable index work inspection", """
            CREATE TABLE index_work_items (
                id TEXT PRIMARY KEY,
                state TEXT NOT NULL CHECK(state IN ('failed', 'stale', 'pending')),
                root TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                reason TEXT NOT NULL,
                updated_unix_ms INTEGER NOT NULL
            ) WITHOUT ROWID;
            CREATE INDEX ix_index_work_items_state_path
                ON index_work_items(state, relative_path COLLATE NOCASE, id);
            """),
        new(7, "Track content extractor versions per indexed path", """
            ALTER TABLE catalog_items ADD COLUMN extractor_version TEXT;
            CREATE INDEX ix_catalog_items_extractor_version
                ON catalog_items(source_id, extractor_version, path)
                WHERE path IS NOT NULL;
            """),
        new(8, "Bound fuzzy title candidate lookup", """
            CREATE INDEX ix_catalog_items_title_nocase
                ON catalog_items(title COLLATE NOCASE);
            """),
        new(9, "Persist bounded index policy provenance", """
            ALTER TABLE catalog_items ADD COLUMN index_policy_json TEXT;
            """),
        new(10, "Store opt-in bounded query history", """
            CREATE TABLE query_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                query_text TEXT NOT NULL,
                stable_id TEXT NOT NULL,
                action_id TEXT NOT NULL,
                invoked_unix_ms INTEGER NOT NULL
            );
            CREATE INDEX ix_query_history_time
                ON query_history(invoked_unix_ms DESC, id DESC);
            """),
        new(11, "Persist user filter aliases", """
            CREATE TABLE filter_aliases (
                alias TEXT COLLATE NOCASE PRIMARY KEY,
                filter_id TEXT NOT NULL,
                created_unix_ms INTEGER NOT NULL,
                updated_unix_ms INTEGER NOT NULL
            ) WITHOUT ROWID;
            CREATE INDEX ix_filter_aliases_filter
                ON filter_aliases(filter_id, alias COLLATE NOCASE);
            """),
        new(12, "Persist typed saved searches", """
            CREATE TABLE saved_searches (
                id TEXT PRIMARY KEY,
                name TEXT COLLATE NOCASE NOT NULL UNIQUE,
                expression_json TEXT NOT NULL,
                shortcut TEXT COLLATE NOCASE UNIQUE,
                show_on_home INTEGER NOT NULL CHECK(show_on_home IN (0, 1)),
                position INTEGER NOT NULL,
                created_unix_ms INTEGER NOT NULL,
                updated_unix_ms INTEGER NOT NULL
            ) WITHOUT ROWID;
            CREATE INDEX ix_saved_searches_position
                ON saved_searches(position, name COLLATE NOCASE);
            """),
        new(13, "Refresh desktop handler metadata", """
            INSERT INTO catalog_state(key, value)
            VALUES('reindex_required', '1')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """),
        new(14, "Persist AI selection and prompt templates", """
            CREATE TABLE ai_preferences (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_unix_ms INTEGER NOT NULL
            ) WITHOUT ROWID;
            CREATE TABLE ai_prompt_templates (
                id TEXT PRIMARY KEY,
                name TEXT COLLATE NOCASE NOT NULL UNIQUE,
                workflow_kind TEXT NOT NULL,
                system_template TEXT NOT NULL,
                user_template TEXT NOT NULL,
                parameters_json TEXT NOT NULL,
                position INTEGER NOT NULL,
                created_unix_ms INTEGER NOT NULL,
                updated_unix_ms INTEGER NOT NULL
            ) WITHOUT ROWID;
            CREATE UNIQUE INDEX ix_ai_prompt_templates_position
                ON ai_prompt_templates(position);
            """),
        new(15, "Persist agent grants and unified assistant preferences", """
            CREATE TABLE agent_grants (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                scope TEXT NOT NULL,
                identity TEXT,
                payload_digest TEXT NOT NULL,
                state TEXT NOT NULL,
                label TEXT,
                created_unix_ms INTEGER NOT NULL,
                updated_unix_ms INTEGER NOT NULL,
                expires_unix_ms INTEGER,
                last_validated_unix_ms INTEGER NOT NULL
            ) WITHOUT ROWID;
            CREATE INDEX ix_agent_grants_kind_state ON agent_grants(kind, state);
            """),
        new(16, "Extension installation registry and independent command projection", """
            DROP INDEX ix_source_preferences_position;
            CREATE TABLE extension_registry (
                id INTEGER PRIMARY KEY CHECK(id = 1),
                revision INTEGER NOT NULL,
                document_json TEXT NOT NULL
            );
            CREATE TABLE extension_commands (
                stable_id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                title TEXT NOT NULL,
                subtitle TEXT,
                path TEXT,
                icon TEXT,
                payload_json TEXT,
                size_bytes INTEGER,
                modified_unix_ms INTEGER,
                provider_id TEXT NOT NULL,
                startup_wm_class TEXT,
                source_id TEXT NOT NULL,
                extractor_version TEXT,
                index_policy_json TEXT
            ) WITHOUT ROWID;
            CREATE INDEX ix_extension_commands_source ON extension_commands(source_id);
            CREATE VIEW customizable_items AS SELECT * FROM catalog_items UNION ALL SELECT * FROM extension_commands;
            """),
        new(17, "Bound fuzzy title lookup by source", """
            CREATE INDEX ix_catalog_items_source_title_nocase
                ON catalog_items(source_id, title COLLATE NOCASE);
            """),
        new(18, "Persist hidden launcher items by canonical identity", """
            CREATE TABLE hidden_items (
                stable_id TEXT PRIMARY KEY,
                title_snapshot TEXT NOT NULL,
                subtitle_snapshot TEXT,
                source_id TEXT NOT NULL,
                icon_snapshot TEXT,
                hidden_unix_ms INTEGER NOT NULL
            ) WITHOUT ROWID;
            """),
        new(19, "Separate extension data revisions from registry authority", """
            CREATE TABLE extension_storage (
                installation_key TEXT PRIMARY KEY,
                revision INTEGER NOT NULL CHECK(revision >= 0),
                values_json TEXT NOT NULL
            ) WITHOUT ROWID;
            """),
        // stable_id is UNINDEXED in catalog_fts, so finding a row's search entry
        // by it scans the whole projection. Each catalog row now records the
        // rowid of its search row; existing rows are linked in one pass over
        // the projection with primary-key probes, so no rebuild is required.
        new(20, "Link catalog rows to search rows by rowid", """
            ALTER TABLE catalog_items ADD COLUMN fts_rowid INTEGER;

            DROP VIEW customizable_items;
            CREATE VIEW customizable_items AS
                SELECT stable_id, kind, title, subtitle, path, icon, payload_json,
                    size_bytes, modified_unix_ms, provider_id, startup_wm_class,
                    source_id, extractor_version, index_policy_json
                FROM catalog_items
                UNION ALL
                SELECT stable_id, kind, title, subtitle, path, icon, payload_json,
                    size_bytes, modified_unix_ms, provider_id, startup_wm_class,
                    source_id, extractor_version, index_policy_json
                FROM extension_commands;

            CREATE INDEX ix_catalog_items_path ON catalog_items(path, source_id);

            UPDATE catalog_items
               SET fts_rowid = f.rowid
              FROM catalog_fts AS f
             WHERE f.stable_id = catalog_items.stable_id;
            """),
        // The indexer blanks a title, subtitle or path that the search policy
        // excludes. Storing the inclusion it applied lets the integrity check
        // and repair apply the same projection. Existing rows take it from the
        // policy provenance that was written from the same decision.
        new(21, "Store search inclusion applied to catalog rows", """
            ALTER TABLE catalog_items ADD COLUMN title_in_search INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE catalog_items ADD COLUMN metadata_in_search INTEGER NOT NULL DEFAULT 1;

            UPDATE catalog_items
               SET title_in_search = 0
             WHERE EXISTS (
                   SELECT 1
                     FROM json_each(CASE WHEN json_valid(index_policy_json) THEN index_policy_json END)
                    WHERE json_extract(value, '$.field') = 'title'
                      AND json_extract(value, '$.included') = 0);
            UPDATE catalog_items
               SET metadata_in_search = 0
             WHERE EXISTS (
                   SELECT 1
                     FROM json_each(CASE WHEN json_valid(index_policy_json) THEN index_policy_json END)
                    WHERE json_extract(value, '$.field') = 'metadata'
                      AND json_extract(value, '$.included') = 0);
            """)
    ];

    // Activation replaces the search projection by recreating it, which frees
    // its pages instead of tokenizing every deleted row again. Keep this
    // definition identical to the latest migration that creates catalog_fts.
    private const string RecreateCatalogFtsSql = """
        DROP TABLE catalog_fts;
        CREATE VIRTUAL TABLE catalog_fts USING fts5(
            stable_id UNINDEXED,
            kind UNINDEXED,
            source_id,
            title,
            subtitle,
            path,
            body,
            config_keys,
            tokenize = 'unicode61 remove_diacritics 2'
        );
        """;

    private static async ValueTask ApplyMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken )
    {
        await RefuseNewerSchemaAsync( connection, cancellationToken ).ConfigureAwait( false );

        foreach ( var migration in Migrations )
        {
            await using var transaction =
                connection.BeginTransaction( deferred: false );
            await using var applied = connection.CreateCommand( );
            applied.Transaction = transaction;
            applied.CommandText =
                "SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE version = $version);";
            applied.Parameters.AddWithValue( "$version", migration.Version );
            var value = await applied.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false );
            if ( Convert.ToInt64( value, System.Globalization.CultureInfo.InvariantCulture ) != 0 )
            {
                await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
                continue;
            }

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                migration.Sql,
                cancellationToken ).ConfigureAwait( false );

            await using var record = connection.CreateCommand( );
            record.Transaction = transaction;
            record.CommandText = """
                INSERT INTO schema_migrations(version, applied_unix_ms, description)
                VALUES($version, $applied, $description);
                """;
            record.Parameters.AddWithValue( "$version", migration.Version );
            record.Parameters.AddWithValue(
                "$applied",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds( ) );
            record.Parameters.AddWithValue( "$description", migration.Description );
            await record.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
            await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        }
    }

    // Migrations are forward-only, so a build must not read or write a schema
    // it does not know: its queries could silently miss newer columns and its
    // writes could break the newer build's invariants.
    private static async ValueTask RefuseNewerSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken )
    {
        await using ( var ledger = connection.CreateCommand( ) )
        {
            ledger.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'schema_migrations');";
            var exists = Convert.ToInt64( await ledger.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false ),
                System.Globalization.CultureInfo.InvariantCulture );
            if ( exists == 0 )
            {
                return;
            }
        }

        await using var newest = connection.CreateCommand( );
        newest.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        var value = await newest.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false );
        var supportedVersion = Migrations[^1].Version;
        if ( value is long appliedVersion && appliedVersion > supportedVersion )
        {
            throw new InvalidOperationException(
                $"The catalog database '{connection.DataSource}' uses schema version {appliedVersion}, " +
                $"but this CommandDeck build supports schema version {supportedVersion} at most. " +
                "Update CommandDeck; this build does not open a newer catalog." );
        }
    }

    // A migration or catalog maintenance sets this flag; a full rebuild clears it
    // in the transaction that replaces the catalog.
    public async ValueTask<bool> RequiresReindexAsync( CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT value
            FROM catalog_state
            WHERE key = 'reindex_required';
            """;
        var value = await command.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false );
        return string.Equals( value as string, "1", StringComparison.Ordinal );
    }

    private sealed record DatabaseMigration( int Version, string Description, string Sql );
}
