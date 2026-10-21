using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    // The unary plus keeps the planner off the low-selectivity source index, so
    // both statements find the changed rows through ix_catalog_items_path.
    private const string ChangedPathRowsPredicate = """
        +source_id IN ($files_source_id, $configuration_source_id)
          AND (path = $path OR (
              $remove_descendants = 1 AND
              path >= $descendant_lower AND path < $descendant_upper
          ))
        """;

    public async ValueTask ApplyPathChangesAsync(
        IReadOnlyList<IndexedPathChange> changes,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( changes );
        if ( changes.Count == 0 )
        {
            return;
        }

        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken ).ConfigureAwait( false );

        await using var removeFts = CreateRemovePathSearchRowsCommand( connection );
        removeFts.Transaction = transaction;
        await removeFts.PrepareAsync( cancellationToken ).ConfigureAwait( false );

        await using var removeItems = CreateRemovePathItemsCommand( connection );
        removeItems.Transaction = transaction;
        await removeItems.PrepareAsync( cancellationToken ).ConfigureAwait( false );

        await using var insertFts = CreateInsertSearchRowCommand( connection );
        insertFts.Transaction = transaction;
        await insertFts.PrepareAsync( cancellationToken ).ConfigureAwait( false );

        await using var insertItem = CreateInsertItemCommand( connection );
        insertItem.Transaction = transaction;
        await insertItem.PrepareAsync( cancellationToken ).ConfigureAwait( false );

        foreach ( var change in changes )
        {
            var pathPrefix = PathPrefix( change.Path );
            BindPathRemoval( removeFts, change, pathPrefix );
            await removeFts.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
            BindPathRemoval( removeItems, change, pathPrefix );
            await removeItems.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );

            foreach ( var item in change.Items )
            {
                if ( !string.Equals( item.Path, change.Path, StringComparison.Ordinal ) &&
                    (!change.RemoveDescendants ||
                     item.Path is null ||
                     !item.Path.StartsWith( pathPrefix, StringComparison.Ordinal )) )
                {
                    throw new InvalidOperationException(
                        "An incremental catalog item must belong to its path change." );
                }

                BindFts( insertFts, item );
                await insertFts.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
                BindItem( insertItem, item );
                await insertItem.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
            }
        }

        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
    }

    // Desktop entries are rescanned as a whole, because a user entry shadows a
    // system entry with the same ID. Their rows are replaced as one set in one
    // transaction; other sources and personalization keyed by ID are untouched.
    public async ValueTask ReplaceApplicationsAsync(
        IReadOnlyList<IndexedItem> items,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( items );
        if ( items.Any( static item => item.SourceId != SourceIds.Applications ) )
        {
            throw new InvalidOperationException( "Only application rows can replace the application source." );
        }

        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken ).ConfigureAwait( false );

        await using ( var remove = connection.CreateCommand( ) )
        {
            remove.Transaction = transaction;
            remove.CommandText = """
                DELETE FROM catalog_fts
                WHERE rowid IN (SELECT fts_rowid FROM catalog_items WHERE source_id = $source_id);
                DELETE FROM catalog_items WHERE source_id = $source_id;
                """;
            remove.Parameters.AddWithValue( "$source_id", SourceIds.Applications );
            await remove.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

        await using var insertFts = CreateInsertSearchRowCommand( connection );
        insertFts.Transaction = transaction;
        await using var insertItem = CreateInsertItemCommand( connection );
        insertItem.Transaction = transaction;
        foreach ( var item in items )
        {
            BindFts( insertFts, item );
            await insertFts.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
            BindItem( insertItem, item );
            await insertItem.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        await InvalidateFileHandlersAsync( ).ConfigureAwait( false );
    }

    private static SqliteCommand CreateInsertSearchRowCommand( SqliteConnection connection )
    {
        var command = connection.CreateCommand( );
        command.CommandText = """
            INSERT INTO catalog_fts(
                stable_id, kind, source_id, title, subtitle, path, body, config_keys)
            VALUES(
                $stable_id, $kind, $source_id, $title, $subtitle, $path, $body, $config_keys);
            """;
        AddParameters( command,
            "$stable_id", "$kind", "$source_id", "$title", "$subtitle", "$path", "$body", "$config_keys" );

        return command;
    }

    // Runs directly after the search row insert on the same connection, so
    // last_insert_rowid() is the rowid of the search row of this catalog row.
    private static SqliteCommand CreateInsertItemCommand( SqliteConnection connection )
    {
        var command = connection.CreateCommand( );
        command.CommandText = """
            INSERT INTO catalog_items(
                stable_id, kind, title, subtitle, path, icon, payload_json,
                size_bytes, modified_unix_ms, provider_id, startup_wm_class, source_id,
                extractor_version, index_policy_json, title_in_search, metadata_in_search,
                fts_rowid)
            VALUES(
                $stable_id, $kind, $title, $subtitle, $path, $icon, $payload_json,
                $size_bytes, $modified_unix_ms, $provider_id, $startup_wm_class, $source_id,
                $extractor_version, $index_policy_json, $title_in_search, $metadata_in_search,
                last_insert_rowid());
            """;
        AddParameters( command,
            "$stable_id", "$kind", "$title", "$subtitle", "$path", "$icon",
            "$payload_json", "$size_bytes", "$modified_unix_ms", "$provider_id",
            "$startup_wm_class", "$source_id", "$extractor_version", "$index_policy_json",
            "$title_in_search", "$metadata_in_search" );

        return command;
    }

    internal static SqliteCommand CreateRemovePathSearchRowsCommand( SqliteConnection connection )
    {
        var command = connection.CreateCommand( );
        command.CommandText = $"""
            DELETE FROM catalog_fts
            WHERE rowid IN (
                SELECT fts_rowid FROM catalog_items
                WHERE {ChangedPathRowsPredicate}
            );
            """;
        AddPathRemovalParameters( command );

        return command;
    }

    internal static SqliteCommand CreateRemovePathItemsCommand( SqliteConnection connection )
    {
        var command = connection.CreateCommand( );
        command.CommandText = $"""
            DELETE FROM catalog_items
            WHERE {ChangedPathRowsPredicate};
            """;
        AddPathRemovalParameters( command );

        return command;
    }

    private static void AddPathRemovalParameters( SqliteCommand command )
    {
        command.Parameters.Add( "$path", SqliteType.Text );
        command.Parameters.Add( "$descendant_lower", SqliteType.Text );
        command.Parameters.Add( "$descendant_upper", SqliteType.Text );
        command.Parameters.Add( "$remove_descendants", SqliteType.Integer );
        command.Parameters.AddWithValue( "$files_source_id", SourceIds.Files );
        command.Parameters.AddWithValue( "$configuration_source_id", SourceIds.Configuration );
    }

    // path is compared with BINARY collation, so the paths that start with
    // "dir/" are exactly those in ["dir/", "dir0"): '0' follows '/' in UTF-8.
    private static void BindPathRemoval(
        SqliteCommand command,
        IndexedPathChange change,
        string pathPrefix )
    {
        var upperBound = pathPrefix[..^1] + ( char ) (pathPrefix[^1] + 1);

        command.Parameters["$path"].Value = change.Path;
        command.Parameters["$descendant_lower"].Value = pathPrefix;
        command.Parameters["$descendant_upper"].Value = upperBound;
        command.Parameters["$remove_descendants"].Value = change.RemoveDescendants ? 1 : 0;
    }

    public async ValueTask<IReadOnlyList<string>> GetPathsRequiringExtractorVersionAsync(
        string extractorVersion,
        int limit,
        CancellationToken cancellationToken )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( extractorVersion );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( limit );

        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT DISTINCT path
            FROM catalog_items
            WHERE source_id IN ($files_source_id, $configuration_source_id)
              AND path IS NOT NULL
              AND COALESCE(extractor_version, '') <> $extractor_version
            ORDER BY path COLLATE NOCASE
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue( "$files_source_id", SourceIds.Files );
        command.Parameters.AddWithValue( "$configuration_source_id", SourceIds.Configuration );
        command.Parameters.AddWithValue( "$extractor_version", extractorVersion );
        command.Parameters.AddWithValue( "$limit", limit );

        List<string> paths = [];
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            paths.Add( reader.GetString( 0 ) );
        }

        return paths;
    }

    public async ValueTask<IReadOnlyList<string>> GetIndexedPathsAfterAsync(
        string? afterPath,
        int limit,
        CancellationToken cancellationToken )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( limit );
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );
        await using var command = CreateIndexedPathsPageCommand( connection, afterPath, limit );

        List<string> paths = [];
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            paths.Add( reader.GetString( 0 ) );
        }

        return paths;
    }

    public async ValueTask<IReadOnlyList<IndexedFileState>> GetIndexedFileStatesAsync(
        string rootPath,
        CancellationToken cancellationToken )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( rootPath );

        var path = Path.TrimEndingDirectorySeparator( rootPath );
        var pathPrefix = Path.EndsInDirectorySeparator( path )
            ? path
            : path + Path.DirectorySeparatorChar;

        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT path, size_bytes, modified_unix_ms
            FROM catalog_items
            WHERE source_id = $files_source_id
              AND path IS NOT NULL
              AND (path = $path OR substr(path, 1, length($path_prefix)) = $path_prefix);
            """;
        command.Parameters.AddWithValue( "$files_source_id", SourceIds.Files );
        command.Parameters.AddWithValue( "$path", path );
        command.Parameters.AddWithValue( "$path_prefix", pathPrefix );

        List<IndexedFileState> states = [];
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            states.Add( new IndexedFileState(
                reader.GetString( 0 ),
                reader.IsDBNull( 1 ) ? null : reader.GetInt64( 1 ),
                reader.IsDBNull( 2 ) ? null : reader.GetInt64( 2 ) ) );
        }

        return states;
    }

    // Each page is a range scan of the covering (path, source_id) index, which
    // already yields distinct paths in order. A parameter-dependent OR in the
    // path predicate would stop SQLite from starting the scan at $after_path.
    internal static SqliteCommand CreateIndexedPathsPageCommand(
        SqliteConnection connection,
        string? afterPath,
        int limit )
    {
        var pathPredicate = afterPath is null
            ? "path IS NOT NULL"
            : "path COLLATE BINARY > $after_path COLLATE BINARY";

        var command = connection.CreateCommand( );
        command.CommandText = $"""
            SELECT DISTINCT path
            FROM catalog_items
            WHERE +source_id IN ($files_source_id, $configuration_source_id)
              AND {pathPredicate}
            ORDER BY path COLLATE BINARY
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue( "$files_source_id", SourceIds.Files );
        command.Parameters.AddWithValue( "$configuration_source_id", SourceIds.Configuration );
        if ( afterPath is not null )
        {
            command.Parameters.AddWithValue( "$after_path", afterPath );
        }

        command.Parameters.AddWithValue( "$limit", limit );

        return command;
    }
}
