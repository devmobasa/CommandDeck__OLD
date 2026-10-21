using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    public ValueTask ReplaceAllAsync(
        IAsyncEnumerable<IndexedItem> items,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken ) =>
        ReplaceGenerationAsync(
            items,
            static ( ) => [],
            static ( ) => [],
            progress,
            cancellationToken );

    public ValueTask ReplaceGenerationAsync(
        IAsyncEnumerable<IndexedItem> items,
        Func<IReadOnlyList<IndexExclusionDiagnostic>> diagnostics,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken ) =>
        ReplaceGenerationAsync( items, diagnostics, static ( ) => [], progress, cancellationToken );

    public async ValueTask ReplaceGenerationAsync(
        IAsyncEnumerable<IndexedItem> items,
        Func<IReadOnlyList<IndexExclusionDiagnostic>> diagnostics,
        Func<IReadOnlyList<IndexWorkItem>> workItems,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( diagnostics );
        ArgumentNullException.ThrowIfNull( workItems );
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var stage = new GenerationStage( connection );
        await using ( var createStage = connection.CreateCommand( ) )
        {
            // The connection-local tables spill to disk without holding the main
            // database writer lock while discovery and extraction await I/O.
            createStage.CommandText = "PRAGMA temp_store = FILE;\n" +
                CreateRowStageSql( "staged_catalog_items", "staged_catalog_fts", byChange: false );
            await createStage.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

        // Each staged item and its search row share one generation-local number,
        // which becomes the published search rowid.
        await using var stager = await RowStager.CreateAsync(
            connection,
            "staged_catalog_items",
            "staged_catalog_fts",
            byChange: false,
            cancellationToken ).ConfigureAwait( false );

        long indexed = 0;
        await foreach ( var item in items.WithCancellation( cancellationToken ).ConfigureAwait( false ) )
        {
            await stager.StageAsync( item, cancellationToken ).ConfigureAwait( false );

            indexed++;
            if ( indexed % 100 == 0 )
            {
                progress?.Report( new IndexProgress
                {
                    Stage = "write",
                    Indexed = indexed,
                    CurrentPath = item.Path
                } );
            }
        }

        var diagnosticSnapshot = diagnostics( );
        var workItemSnapshot = workItems( );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken ).ConfigureAwait( false );

        // Other writers wait for this transaction, so it only copies staged rows.
        // Catalog rows go in primary-key order through the staged index.
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            $"""
            {RecreateCatalogFtsSql}
            DELETE FROM catalog_items;
            {CopyStagedRowsSql( "staged_catalog_items", "staged_catalog_fts" )}
            """,
            cancellationToken ).ConfigureAwait( false );

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            INSERT INTO catalog_state(key, value)
            VALUES('reindex_required', '0')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            cancellationToken ).ConfigureAwait( false );

        await ReplaceExclusionDiagnosticsAsync(
            connection,
            transaction,
            diagnosticSnapshot,
            cancellationToken ).ConfigureAwait( false );
        await ReplaceIndexWorkItemsAsync(
            connection,
            transaction,
            workItemSnapshot,
            cancellationToken ).ConfigureAwait( false );

        if ( _configuredQuicklinks is not null )
        {
            var quicklinks = await ReadQuicklinksAsync( connection, transaction, null, cancellationToken ).ConfigureAwait( false );
            await PublishQuicklinksAsync( connection, transaction, quicklinks.Quicklinks, cancellationToken ).ConfigureAwait( false );
        }

        await using ( var optimize = connection.CreateCommand( ) )
        {
            optimize.Transaction = transaction;
            optimize.CommandText = "INSERT INTO catalog_fts(catalog_fts) VALUES('optimize');";
            await optimize.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        await InvalidateFileHandlersAsync( ).ConfigureAwait( false );

        // The generation is published; cancelling the cleanup would report a
        // failure for it.
        await TruncateWriteAheadLogAsync( connection, CancellationToken.None ).ConfigureAwait( false );
    }

    // A published generation rewrites every catalog and search row, so the WAL
    // grows to about the size of the catalog. The automatic checkpoint copies
    // it back but leaves the file at that size; TRUNCATE returns the space.
    // TRUNCATE holds the writer lock while its busy handler waits for readers
    // on an older snapshot, so it runs with the connection's busy timeout off:
    // it then copies what it can, reports busy in its result row instead of
    // failing, and journal_size_limit or a later checkpoint finishes the job.
    private static async ValueTask TruncateWriteAheadLogAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.CommandText = "PRAGMA busy_timeout;";
        var busyTimeout = Convert.ToInt64(
            await command.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false ),
            CultureInfo.InvariantCulture );

        command.CommandText = "PRAGMA busy_timeout = 0; PRAGMA wal_checkpoint(TRUNCATE);";
        try
        {
            await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            command.CommandText = string.Create(
                CultureInfo.InvariantCulture,
                $"PRAGMA busy_timeout = {busyTimeout};" );
            await command.ExecuteNonQueryAsync( CancellationToken.None ).ConfigureAwait( false );
        }
    }

    private static async ValueTask ReplaceIndexWorkItemsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<IndexWorkItem> workItems,
        CancellationToken cancellationToken )
    {
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "DELETE FROM index_work_items;",
            cancellationToken ).ConfigureAwait( false );

        await using var insert = connection.CreateCommand( );
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO index_work_items(
                id, state, root, relative_path, reason, updated_unix_ms)
            VALUES(
                $id, $state, $root, $relative_path, $reason, $updated_unix_ms);
            """;
        AddParameters(
            insert,
            "$id",
            "$state",
            "$root",
            "$relative_path",
            "$reason",
            "$updated_unix_ms" );
        await insert.PrepareAsync( cancellationToken ).ConfigureAwait( false );
        foreach ( var item in workItems )
        {
            insert.Parameters["$id"].Value = item.Id;
            insert.Parameters["$state"].Value = item.State;
            insert.Parameters["$root"].Value = item.Root;
            insert.Parameters["$relative_path"].Value = item.RelativePath;
            insert.Parameters["$reason"].Value = item.Reason;
            insert.Parameters["$updated_unix_ms"].Value = item.UpdatedUnixMilliseconds;
            await insert.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
    }

    private static void BindItem( SqliteCommand command, IndexedItem item )
    {
        command.Parameters["$stable_id"].Value = item.StableId;
        command.Parameters["$kind"].Value = item.Kind;
        command.Parameters["$title"].Value = item.Title;
        command.Parameters["$subtitle"].Value = ( object? ) item.Subtitle ?? DBNull.Value;
        command.Parameters["$path"].Value = ( object? ) item.Path ?? DBNull.Value;
        command.Parameters["$icon"].Value = ( object? ) item.Icon ?? DBNull.Value;
        command.Parameters["$payload_json"].Value = ( object? ) item.PayloadJson ?? DBNull.Value;
        command.Parameters["$size_bytes"].Value = ( object? ) item.SizeBytes ?? DBNull.Value;
        command.Parameters["$modified_unix_ms"].Value =
            ( object? ) item.ModifiedUnixMilliseconds ?? DBNull.Value;
        command.Parameters["$provider_id"].Value = item.ProviderId;
        command.Parameters["$startup_wm_class"].Value =
            ( object? ) item.StartupWmClass ?? DBNull.Value;
        command.Parameters["$source_id"].Value = item.SourceId;
        command.Parameters["$extractor_version"].Value =
            ( object? ) item.ExtractorVersion ?? DBNull.Value;
        command.Parameters["$index_policy_json"].Value = item.IndexPolicies.Count == 0
            ? DBNull.Value
            : System.Text.Json.JsonSerializer.Serialize(
                item.IndexPolicies,
                CommandDeckJsonContext.Default.ListIndexFieldPolicy );
        command.Parameters["$title_in_search"].Value = item.IncludeTitleInSearch ? 1 : 0;
        command.Parameters["$metadata_in_search"].Value = item.IncludeMetadataInSearch ? 1 : 0;
    }

    private static void BindFts( SqliteCommand command, IndexedItem item )
    {
        command.Parameters["$stable_id"].Value = item.StableId;
        command.Parameters["$kind"].Value = item.Kind;
        command.Parameters["$source_id"].Value = item.SourceId;
        command.Parameters["$title"].Value = SearchTitle( item.Title, item.IncludeTitleInSearch );
        command.Parameters["$subtitle"].Value = SearchMetadata( item.Subtitle, item.IncludeMetadataInSearch );
        command.Parameters["$path"].Value = SearchMetadata( item.Path, item.IncludeMetadataInSearch );
        command.Parameters["$body"].Value = ( object? ) item.Body ?? string.Empty;
        command.Parameters["$config_keys"].Value = ( object? ) item.ConfigKeys ?? string.Empty;
    }

    private sealed class GenerationStage(
        SqliteConnection connection,
        string itemTable = "staged_catalog_items",
        string searchTable = "staged_catalog_fts" ) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync( )
        {
            // A pooled handle keeps TEMP tables even with temp_store already FILE.
            // Drop this stage explicitly before returning the physical connection.
            await using var cleanup = connection.CreateCommand( );
            cleanup.CommandText = $"DROP TABLE IF EXISTS temp.{searchTable}; DROP TABLE IF EXISTS temp.{itemTable};";
            await cleanup.ExecuteNonQueryAsync( CancellationToken.None ).ConfigureAwait( false );
        }
    }
}
