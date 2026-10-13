using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    // One pass over the catalog probes each linked search row by rowid, and one
    // pass over the projection counts its rows and duplicate stable IDs. A
    // catalog row whose link finds no search row is missing; a search row that
    // no catalog row links is an orphan. The CROSS JOIN keeps catalog_items as
    // the outer loop, because catalog_fts cannot be probed by fts_rowid.
    internal static SqliteCommand CreateProjectionConsistencyCommand( SqliteConnection connection )
    {
        RegisterSearchProjectionFunctions( connection );

        var command = connection.CreateCommand( );
        command.CommandText = """
            WITH
                items AS (SELECT COUNT(*) AS row_count FROM catalog_items),
                search AS (
                    SELECT COALESCE(SUM(row_count), 0) AS row_count,
                           COALESCE(SUM(row_count - 1), 0) AS duplicate_count
                      FROM (SELECT COUNT(*) AS row_count
                              FROM catalog_fts
                             GROUP BY stable_id)),
                linked AS (
                    SELECT COUNT(*) AS row_count,
                           COUNT(DISTINCT i.fts_rowid) AS search_row_count,
                           COALESCE(SUM(
                               i.stable_id IS NOT f.stable_id
                               OR i.kind IS NOT f.kind
                               OR i.source_id IS NOT f.source_id
                               OR f.title IS NOT
                                  commanddeck_search_title(i.title, i.title_in_search)
                               OR f.subtitle IS NOT
                                  commanddeck_search_metadata(i.subtitle, i.metadata_in_search)
                               OR f.path IS NOT
                                  commanddeck_search_metadata(i.path, i.metadata_in_search)), 0)
                               AS mismatched_count
                      FROM catalog_items i
                     CROSS JOIN catalog_fts f
                     WHERE f.rowid = i.fts_rowid)
            SELECT items.row_count,
                   search.row_count,
                   items.row_count - linked.row_count,
                   search.row_count - linked.search_row_count,
                   linked.mismatched_count,
                   search.duplicate_count
              FROM items, search, linked;
            """;

        return command;
    }

    private static async ValueTask<ProjectionConsistency> ReadProjectionConsistencyAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken )
    {
        await using var command = CreateProjectionConsistencyCommand( connection );
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );

        return new ProjectionConsistency(
            CatalogItems: reader.GetInt64( 0 ),
            SearchRows: reader.GetInt64( 1 ),
            Missing: reader.GetInt64( 2 ),
            Orphan: reader.GetInt64( 3 ),
            Mismatched: reader.GetInt64( 4 ),
            Duplicate: reader.GetInt64( 5 ) );
    }

    private readonly record struct ProjectionConsistency(
        long CatalogItems,
        long SearchRows,
        long Missing,
        long Orphan,
        long Mismatched,
        long Duplicate )
    {
        public long AnomalyCount => Missing + Orphan + Mismatched + Duplicate;

        public bool IsConsistent => CatalogItems == SearchRows && AnomalyCount == 0;
    }
}
