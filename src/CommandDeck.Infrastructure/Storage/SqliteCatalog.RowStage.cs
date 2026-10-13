using Microsoft.Data.Sqlite;

namespace CommandDeck;

// Connection-local staging shared by the full rebuild and path rescans. Staged
// rows spill to disk without the database writer lock. Each staged item and
// its search row share one stage-local number: the item's fts_rowid and the
// staged search row's rowid, which publication turns into the published
// search rowid. Items keep their search inclusion flags, and their search rows
// hold only the fields search may see.
public sealed partial class SqliteCatalog
{
    private const string CatalogItemFields = """
        stable_id, kind, title, subtitle, path, icon, payload_json,
                size_bytes, modified_unix_ms, provider_id, startup_wm_class, source_id,
                extractor_version, index_policy_json, title_in_search, metadata_in_search
        """;

    private const string CatalogItemColumns = CatalogItemFields + ", fts_rowid";

    private const string CatalogSearchColumns =
        "stable_id, kind, source_id, title, subtitle, path, body, config_keys";

    // A stage by change also records which change of a batch each item belongs
    // to, and holds a stable ID once per change.
    private static string CreateRowStageSql( string itemTable, string searchTable, bool byChange ) => $"""
        CREATE TEMP TABLE {itemTable} AS
            SELECT {CatalogItemColumns}{(byChange ? ", 0 AS change_index" : string.Empty)}
            FROM main.catalog_items WHERE 0;
        CREATE UNIQUE INDEX temp.ix_{itemTable}_stable_id
            ON {itemTable}({(byChange ? "change_index, " : string.Empty)}stable_id);
        CREATE TEMP TABLE {searchTable} AS
            SELECT {CatalogSearchColumns}
            FROM main.catalog_fts WHERE 0;
        """;

    // Copies staged rows into the published tables: catalog rows in primary-key
    // order, search rows by stage number. Published search rowids are the stage
    // numbers plus searchBase, when given; the filters select part of the stage.
    private static string CopyStagedRowsSql(
        string itemTable,
        string searchTable,
        string? searchBase = null,
        string? itemFilter = null,
        string? searchFilter = null )
    {
        var offset = searchBase is null ? string.Empty : $" + {searchBase}";
        return $"""
            INSERT INTO catalog_items({CatalogItemColumns})
            SELECT {CatalogItemFields}, fts_rowid{offset}
            FROM temp.{itemTable}
            {(itemFilter is null ? string.Empty : $"WHERE {itemFilter}")}
            ORDER BY stable_id;
            INSERT INTO catalog_fts(rowid, {CatalogSearchColumns})
            SELECT rowid{offset}, {CatalogSearchColumns}
            FROM temp.{searchTable}
            {(searchFilter is null ? string.Empty : $"WHERE {searchFilter}")}
            ORDER BY rowid;
            """;
    }

    private sealed class RowStager : IAsyncDisposable
    {
        private readonly SqliteCommand _insertItem;
        private readonly SqliteCommand _insertSearch;
        private readonly bool _byChange;

        private RowStager( SqliteCommand insertItem, SqliteCommand insertSearch, bool byChange )
        {
            _insertItem = insertItem;
            _insertSearch = insertSearch;
            _byChange = byChange;
        }

        public long Count
        {
            get; private set;
        }

        // The change that staged items belong to, in a stage by change.
        public int ChangeIndex
        {
            get; set;
        }

        // In a stage by change, an item whose stable ID its change already
        // staged is skipped. Otherwise it fails, as a duplicate row would.
        public static async ValueTask<RowStager> CreateAsync(
            SqliteConnection connection,
            string itemTable,
            string searchTable,
            bool byChange,
            CancellationToken cancellationToken )
        {
            var insertItem = connection.CreateCommand( );
            insertItem.CommandText = $"""
                INSERT {(byChange ? "OR IGNORE " : string.Empty)}INTO temp.{itemTable}(
                    {CatalogItemColumns}{(byChange ? ", change_index" : string.Empty)})
                VALUES(
                    $stable_id, $kind, $title, $subtitle, $path, $icon, $payload_json,
                    $size_bytes, $modified_unix_ms, $provider_id, $startup_wm_class, $source_id,
                    $extractor_version, $index_policy_json, $title_in_search, $metadata_in_search,
                    $fts_rowid{(byChange ? ", $change_index" : string.Empty)});
                """;
            AddParameters( insertItem,
                "$stable_id", "$kind", "$title", "$subtitle", "$path", "$icon",
                "$payload_json", "$size_bytes", "$modified_unix_ms", "$provider_id",
                "$startup_wm_class", "$source_id", "$extractor_version", "$index_policy_json",
                "$title_in_search", "$metadata_in_search", "$fts_rowid" );
            if ( byChange )
            {
                AddParameters( insertItem, "$change_index" );
            }

            await insertItem.PrepareAsync( cancellationToken ).ConfigureAwait( false );

            var insertSearch = connection.CreateCommand( );
            insertSearch.CommandText = $"""
                INSERT INTO temp.{searchTable}(rowid, {CatalogSearchColumns})
                VALUES(
                    $fts_rowid, $stable_id, $kind, $source_id, $title, $subtitle, $path, $body,
                    $config_keys);
                """;
            AddParameters( insertSearch,
                "$fts_rowid", "$stable_id", "$kind", "$source_id", "$title", "$subtitle", "$path",
                "$body", "$config_keys" );
            await insertSearch.PrepareAsync( cancellationToken ).ConfigureAwait( false );

            return new RowStager( insertItem, insertSearch, byChange );
        }

        // Returns false for a skipped duplicate.
        public async ValueTask<bool> StageAsync( IndexedItem item, CancellationToken cancellationToken )
        {
            var number = Count + 1;

            BindItem( _insertItem, item );
            _insertItem.Parameters["$fts_rowid"].Value = number;
            if ( _byChange )
            {
                _insertItem.Parameters["$change_index"].Value = ChangeIndex;
            }

            if ( await _insertItem.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false ) == 0 )
            {
                return false;
            }

            BindFts( _insertSearch, item );
            _insertSearch.Parameters["$fts_rowid"].Value = number;
            await _insertSearch.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );

            Count = number;
            return true;
        }

        public async ValueTask DisposeAsync( )
        {
            await _insertItem.DisposeAsync( ).ConfigureAwait( false );
            await _insertSearch.DisposeAsync( ).ConfigureAwait( false );
        }
    }
}
