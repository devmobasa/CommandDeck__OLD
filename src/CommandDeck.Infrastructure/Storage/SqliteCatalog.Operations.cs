using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    private const int MaximumInspectionDimensionCounts = 64;
    private const int MaximumInspectionDimensionIdCharacters = 128;

    // VACUUM rewrites the whole database and blocks other writers while it
    // runs; SQLite reuses free pages for new rows anyway. It runs only when at
    // least a quarter of the file is free, so it returns a meaningful share of
    // the file instead of rewriting a compact catalog every maintenance run.
    private const int VacuumFreePagePercent = 25;

    public ValueTask<CatalogInspection> InspectAsync( CancellationToken cancellationToken ) =>
        InspectAsync( 0, 50, cancellationToken );

    public async ValueTask<CatalogInspection> InspectAsync(
        int workItemOffset,
        int workItemLimit,
        CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );

        (var sourceCounts, var sourceCountsTruncated) =
            await ReadDimensionCountsAsync(
                connection,
                "source_id",
                cancellationToken ).ConfigureAwait( false );
        (var kindCounts, var kindCountsTruncated) =
            await ReadDimensionCountsAsync(
                connection,
                "kind",
                cancellationToken ).ConfigureAwait( false );
        (var exclusionDiagnostics, var totalExclusionDiagnostics) =
            await ReadExclusionDiagnosticsAsync( connection, cancellationToken )
                .ConfigureAwait( false );
        (var workItems, var failed, var stale, var pending, var totalWorkItems) =
            await ReadIndexWorkItemsAsync(
                connection,
                workItemOffset,
                workItemLimit,
                cancellationToken ).ConfigureAwait( false );

        return new CatalogInspection
        {
            Stats = await GetStatsAsync( cancellationToken ).ConfigureAwait( false ),
            SourceCounts = sourceCounts,
            KindCounts = kindCounts,
            SourceCountsTruncated = sourceCountsTruncated,
            KindCountsTruncated = kindCountsTruncated,
            RebuildRequired = await RequiresReindexAsync( cancellationToken ).ConfigureAwait( false ),
            FailedItemCount = failed,
            StaleItemCount = stale,
            PendingItemCount = pending,
            DetailedWorkStateAvailable = true,
            WorkItems = workItems,
            TotalWorkItemCount = totalWorkItems,
            WorkItemOffset = Math.Max( 0, workItemOffset ),
            WorkItemsTruncated = Math.Max( 0, workItemOffset ) + workItems.Count < totalWorkItems,
            ExclusionDiagnostics = exclusionDiagnostics,
            TotalExclusionDiagnosticCount = totalExclusionDiagnostics,
            ExclusionDiagnosticsTruncated =
                totalExclusionDiagnostics > exclusionDiagnostics.Count
        };
    }

    public async ValueTask<CatalogMaintenanceResult> CheckIntegrityAsync(
        CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        var bytesBefore = GetDatabaseBytes( );
        List<string> messages = [];
        var succeeded = true;
        var rebuildRequired = await RequiresReindexAsync( cancellationToken ).ConfigureAwait( false );

        try
        {
            await using var connection = await OpenAsync( cancellationToken )
                .ConfigureAwait( false );
            var integrityErrorCount = 0;
            await using ( var integrity = connection.CreateCommand( ) )
            {
                integrity.CommandText = "PRAGMA integrity_check(100);";
                await using var reader = await integrity.ExecuteReaderAsync(
                    cancellationToken ).ConfigureAwait( false );
                while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
                {
                    var result = reader.GetString( 0 );
                    if ( !string.Equals( result, "ok", StringComparison.OrdinalIgnoreCase ) )
                    {
                        succeeded = false;
                        integrityErrorCount++;
                    }
                }
            }

            if ( integrityErrorCount > 0 )
            {
                messages.Add(
                    $"SQLite integrity check reported {integrityErrorCount} error(s); " +
                    "details were omitted to avoid exposing stored data." );
            }

            var projection = await ReadProjectionConsistencyAsync( connection, cancellationToken )
                .ConfigureAwait( false );
            if ( !projection.IsConsistent )
            {
                succeeded = false;
                rebuildRequired = true;
                messages.Add(
                    "Search projection differs from catalog items; run a full reindex. " +
                    $"Counts: items={projection.CatalogItems}, projection={projection.SearchRows}, " +
                    $"missing={projection.Missing}, orphan={projection.Orphan}, " +
                    $"mismatched={projection.Mismatched}, duplicate={projection.Duplicate}." );
                await MarkRebuildRequiredAsync( connection, cancellationToken ).ConfigureAwait( false );
            }
        }
        catch ( SqliteException exception )
        {
            succeeded = false;
            messages.Add( $"SQLite integrity check failed: {exception.SqliteErrorCode}." );
        }

        if ( succeeded )
        {
            messages.Add( "SQLite storage and the search projection are consistent." );
        }

        return MaintenanceResult(
            CatalogOperationNames.Integrity,
            succeeded,
            affectedItemCount: 0,
            bytesBefore,
            rebuildRequired,
            messages );
    }

    public async ValueTask<CatalogMaintenanceResult> OptimizeAsync(
        CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        var bytesBefore = GetDatabaseBytes( );
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );

        await using ( var optimize = connection.CreateCommand( ) )
        {
            optimize.CommandText = """
                INSERT INTO catalog_fts(catalog_fts) VALUES('optimize');
                PRAGMA optimize;
                """;
            await optimize.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

        (var freePages, var totalPages) = await ReadPageCountsAsync( connection, cancellationToken )
            .ConfigureAwait( false );
        string message;
        if ( freePages * 100 >= totalPages * VacuumFreePagePercent )
        {
            await using ( var vacuum = connection.CreateCommand( ) )
            {
                vacuum.CommandText = "VACUUM;";
                await vacuum.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
            }

            // VACUUM writes the rewritten database through the WAL; the database
            // file shrinks and the WAL gives its space back only at a checkpoint.
            await TruncateWriteAheadLogAsync( connection, CancellationToken.None ).ConfigureAwait( false );
            message = "FTS indexes optimized, query planner statistics refreshed, and database vacuumed " +
                      $"({freePages} of {totalPages} pages were free).";
        }
        else
        {
            message = "FTS indexes optimized and query planner statistics refreshed; vacuum skipped " +
                      $"because {freePages} of {totalPages} pages are free " +
                      $"(below {VacuumFreePagePercent}%).";
        }

        return MaintenanceResult(
            CatalogOperationNames.Optimize,
            succeeded: true,
            affectedItemCount: 0,
            bytesBefore,
            await RequiresReindexAsync( cancellationToken ).ConfigureAwait( false ),
            [message] );
    }

    private static async ValueTask<(long Free, long Total)> ReadPageCountsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.CommandText = "SELECT freelist_count, page_count FROM pragma_freelist_count, pragma_page_count;";
        await using var reader = await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );

        return (reader.GetInt64( 0 ), reader.GetInt64( 1 ));
    }

    public async ValueTask<CatalogMaintenanceResult> RepairAsync(
        CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        var bytesBefore = GetDatabaseBytes( );
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );

        var projection = await ReadProjectionConsistencyAsync( connection, cancellationToken )
            .ConfigureAwait( false );
        if ( projection.AnomalyCount == 0 )
        {
            return MaintenanceResult(
                CatalogOperationNames.Repair,
                succeeded: true,
                affectedItemCount: 0,
                bytesBefore,
                await RequiresReindexAsync( cancellationToken ).ConfigureAwait( false ),
                ["No catalog projection repair was needed."] );
        }

        // Rebuilt search rows apply the indexer's projection to the inclusion
        // stored on each catalog row, so an excluded field stays out of search.
        RegisterSearchProjectionFunctions( connection );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken )
                .ConfigureAwait( false );
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            DROP TABLE IF EXISTS temp.commanddeck_catalog_repair;
            CREATE TEMP TABLE commanddeck_catalog_repair (
                stable_id TEXT PRIMARY KEY,
                body TEXT,
                config_keys TEXT
            ) WITHOUT ROWID;

            INSERT INTO commanddeck_catalog_repair(stable_id, body, config_keys)
            SELECT f.stable_id, MAX(f.body), MAX(f.config_keys)
              FROM catalog_fts f
              JOIN catalog_items i ON i.stable_id = f.stable_id
             GROUP BY f.stable_id;

            DELETE FROM catalog_fts;
            WITH numbered AS (
                SELECT stable_id, row_number() OVER (ORDER BY stable_id) AS fts_rowid
                  FROM catalog_items
            )
            UPDATE catalog_items
               SET fts_rowid = numbered.fts_rowid
              FROM numbered
             WHERE numbered.stable_id = catalog_items.stable_id;
            INSERT INTO catalog_fts(
                rowid, stable_id, kind, source_id, title, subtitle, path, body, config_keys)
            SELECT i.fts_rowid, i.stable_id, i.kind, i.source_id,
                   commanddeck_search_title(i.title, i.title_in_search),
                   commanddeck_search_metadata(i.subtitle, i.metadata_in_search),
                   commanddeck_search_metadata(i.path, i.metadata_in_search),
                   COALESCE(r.body, ''), COALESCE(r.config_keys, '')
              FROM catalog_items i
              LEFT JOIN commanddeck_catalog_repair r ON r.stable_id = i.stable_id;

            INSERT INTO catalog_state(key, value)
            VALUES('reindex_required', '1')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            DROP TABLE commanddeck_catalog_repair;
            """,
            cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );

        return MaintenanceResult(
            CatalogOperationNames.Repair,
            succeeded: true,
            projection.CatalogItems,
            bytesBefore,
            rebuildRequired: true,
            [
                "Rebuilt the search projection from canonical catalog rows.",
                "A full reindex is required to restore any missing extracted content."
            ] );
    }

    public async ValueTask<CatalogMaintenanceResult> PurgeProjectionAsync(
        CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        var bytesBefore = GetDatabaseBytes( );
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken )
                .ConfigureAwait( false );

        long itemCount;
        await using ( var count = connection.CreateCommand( ) )
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM catalog_items;";
            itemCount = Convert.ToInt64(
                await count.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false ),
                System.Globalization.CultureInfo.InvariantCulture );
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            DELETE FROM catalog_fts;
            DELETE FROM catalog_items;
            DELETE FROM index_exclusion_diagnostics;
            DELETE FROM index_work_items;
            INSERT INTO catalog_state(key, value)
            VALUES('reindex_required', '1')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );

        return MaintenanceResult(
            CatalogOperationNames.Purge,
            succeeded: true,
            itemCount,
            bytesBefore,
            rebuildRequired: true,
            [
                "Removed only rebuildable catalog, search, and exclusion-diagnostic rows.",
                "Bookmarks, aliases, launcher preferences, and usage history were preserved."
            ] );
    }

    private static async ValueTask<(List<CatalogDimensionCount> Counts, bool Truncated)>
        ReadDimensionCountsAsync(
        SqliteConnection connection,
        string column,
        CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.CommandText = $"""
            SELECT bounded_id, SUM(item_count)
              FROM (
                    SELECT substr({column}, 1, $maximum_id_characters) AS bounded_id,
                           COUNT(*) AS item_count
                      FROM catalog_items
                     GROUP BY {column}
                   )
             GROUP BY bounded_id
             ORDER BY bounded_id COLLATE NOCASE
             LIMIT $maximum_counts_plus_one;
            """;
        command.Parameters.AddWithValue(
            "$maximum_id_characters",
            MaximumInspectionDimensionIdCharacters );
        command.Parameters.AddWithValue(
            "$maximum_counts_plus_one",
            MaximumInspectionDimensionCounts + 1 );
        List<CatalogDimensionCount> counts = [];
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            counts.Add( new CatalogDimensionCount
            {
                Id = reader.GetString( 0 ),
                Count = reader.GetInt64( 1 )
            } );
        }

        var truncated = counts.Count > MaximumInspectionDimensionCounts;
        if ( truncated )
        {
            counts.RemoveAt( counts.Count - 1 );
        }

        return (counts, truncated);
    }

    private static async ValueTask<(
        List<IndexWorkItem> Items,
        long Failed,
        long Stale,
        long Pending,
        long Total)> ReadIndexWorkItemsAsync(
        SqliteConnection connection,
        int offset,
        int limit,
        CancellationToken cancellationToken )
    {
        var boundedOffset = Math.Max( 0, offset );
        var boundedLimit = Math.Clamp( limit, 1, 256 );
        await using var counts = connection.CreateCommand( );
        counts.CommandText = """
            SELECT
                SUM(CASE WHEN state = 'failed' THEN 1 ELSE 0 END),
                SUM(CASE WHEN state = 'stale' THEN 1 ELSE 0 END),
                SUM(CASE WHEN state = 'pending' THEN 1 ELSE 0 END),
                COUNT(*)
            FROM index_work_items;
            """;
        await using var countReader = await counts.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        await countReader.ReadAsync( cancellationToken ).ConfigureAwait( false );
        var failed = countReader.IsDBNull( 0 ) ? 0 : countReader.GetInt64( 0 );
        var stale = countReader.IsDBNull( 1 ) ? 0 : countReader.GetInt64( 1 );
        var pending = countReader.IsDBNull( 2 ) ? 0 : countReader.GetInt64( 2 );
        var total = countReader.GetInt64( 3 );

        await using var page = connection.CreateCommand( );
        page.CommandText = """
            SELECT id, state, root, relative_path, reason, updated_unix_ms
            FROM index_work_items
            ORDER BY CASE state WHEN 'failed' THEN 0 WHEN 'stale' THEN 1 ELSE 2 END,
                     relative_path COLLATE NOCASE,
                     id
            LIMIT $work_item_limit OFFSET $work_item_offset;
            """;
        page.Parameters.AddWithValue( "$work_item_limit", boundedLimit );
        page.Parameters.AddWithValue( "$work_item_offset", boundedOffset );
        List<IndexWorkItem> items = [];
        await using var reader = await page.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            items.Add( new IndexWorkItem
            {
                Id = reader.GetString( 0 ),
                State = reader.GetString( 1 ),
                Root = reader.GetString( 2 ),
                RelativePath = reader.GetString( 3 ),
                Reason = reader.GetString( 4 ),
                UpdatedUnixMilliseconds = reader.GetInt64( 5 )
            } );
        }

        return (items, failed, stale, pending, total);
    }

    private static async ValueTask MarkRebuildRequiredAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            INSERT INTO catalog_state(key, value)
            VALUES('reindex_required', '1')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }

    private CatalogMaintenanceResult MaintenanceResult(
        string operation,
        bool succeeded,
        long affectedItemCount,
        long bytesBefore,
        bool rebuildRequired,
        List<string> messages ) =>
        new( )
        {
            Operation = operation,
            Succeeded = succeeded,
            AffectedItemCount = affectedItemCount,
            DatabaseBytesBefore = bytesBefore,
            DatabaseBytesAfter = GetDatabaseBytes( ),
            RebuildRequired = rebuildRequired,
            Messages = messages
        };

    private long GetDatabaseBytes( ) =>
        File.Exists( _databasePath ) ? new FileInfo( _databasePath ).Length : 0;
}
