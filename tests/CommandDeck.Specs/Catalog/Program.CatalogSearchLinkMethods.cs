using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task PathRemovalProbesSearchRowsByRowidAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-path-removal-plan-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await using var connection = await catalog.OpenAsync( CancellationToken.None );

            // A full catalog_fts scan per change cost 330 ms at 100k rows and
            // held the writer for 36 s over 100 changes.
            await using var removeSearchRows = SqliteCatalog.CreateRemovePathSearchRowsCommand( connection );
            var searchPlan = await ExplainPlanStepsAsync( removeSearchRows );
            var searchSteps = string.Join( " | ", searchPlan.Select( static step => step.Detail ) );
            True( searchPlan.Any( static step => IsSearchRowidLookup( step.Detail ) ),
                $"search rows are deleted by rowid: {searchSteps}" );
            True( !searchPlan.Any( static step => IsUnconstrainedSearchScan( step.Detail ) ),
                $"no full search-projection scan: {searchSteps}" );
            True( searchPlan.Any( static step =>
                    step.Detail.Contains( "USING INDEX ix_catalog_items_path", StringComparison.Ordinal ) ) &&
                !searchPlan.Any( static step =>
                    step.Detail.Contains( "SCAN catalog_items", StringComparison.Ordinal ) ),
                $"changed rows are found through the path index: {searchSteps}" );

            await using var removeItems = SqliteCatalog.CreateRemovePathItemsCommand( connection );
            var itemPlan = await ExplainPlanStepsAsync( removeItems );
            var itemSteps = string.Join( " | ", itemPlan.Select( static step => step.Detail ) );
            True( itemPlan.All( static step =>
                    step.Detail.Contains( "USING INDEX ix_catalog_items_path", StringComparison.Ordinal ) ||
                    !step.Detail.Contains( "catalog_items", StringComparison.Ordinal ) ),
                $"catalog rows are deleted through the path index: {itemSteps}" );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task PathRemovalMatchesOnlyThePathAndDescendantsAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-path-removal-range-{Guid.NewGuid( ):N}.db" );
        string[] removed =
        [
            "/spec/dir",
            "/spec/dir/a.txt",
            "/spec/dir/sub/b.txt",
            "/spec/dir/ünïcode.txt",
            "/spec/exact"
        ];
        string[] kept =
        [
            "/spec/di.txt",
            "/spec/dir-sibling.txt",
            "/spec/dir.txt",
            "/spec/dir0.txt",
            "/spec/dirx/c.txt",
            "/spec/diré.txt",
            "/spec/exact/child.txt",
            "/spec/exact.txt"
        ];
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync( Items( ), null, CancellationToken.None );

            await catalog.ApplyPathChangesAsync(
            [
                new IndexedPathChange { Path = "/spec/dir", RemoveDescendants = true },
                new IndexedPathChange { Path = "/spec/exact" }
            ],
            CancellationToken.None );

            foreach ( var path in removed )
            {
                True( (await SearchAsync( catalog, BodyToken( path ) )).Count == 0, $"{path} was removed" );
            }

            foreach ( var path in kept )
            {
                True( (await SearchAsync( catalog, BodyToken( path ) )).Count == 1, $"{path} was kept" );
            }

            Equal( "app:range.desktop", (await SearchAsync( catalog, "rangeapplication" )).Single( ).Id );
            Equal( ( long ) kept.Length + 1, (await catalog.GetStatsAsync( CancellationToken.None )).ItemCount );
            True( (await catalog.CheckIntegrityAsync( CancellationToken.None )).Succeeded );
        }
        finally
        {
            DeleteDatabase( database );
        }

        async IAsyncEnumerable<IndexedItem> Items( )
        {
            foreach ( var path in removed.Concat( kept ) )
            {
                yield return new IndexedItem
                {
                    StableId = StableId.Create( ResultKinds.File, path ),
                    Kind = ResultKinds.File,
                    Title = Path.GetFileName( path ),
                    Path = path,
                    Body = BodyToken( path ),
                    ProviderId = ProviderIds.Files,
                    SourceId = SourceIds.Files
                };
            }

            yield return new IndexedItem
            {
                StableId = "app:range.desktop",
                Kind = ResultKinds.Application,
                Title = "RangeApplication",
                Path = "/spec/dir/range.desktop",
                ProviderId = "applications",
                SourceId = SourceIds.Applications
            };
            await Task.CompletedTask;
        }

        // Fixed-width tokens keep one path's token from prefixing another's.
        string BodyToken( string path ) =>
            $"rangemarker{Array.IndexOf( [.. removed, .. kept], path ):D2}";
    }

    private static async Task StalePathPagerReadsThePathIndexInOrderAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-stale-path-pager-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.ReplaceAllAsync( PagerItems( ), null, CancellationToken.None );

            // Every 256-path page sorted all file paths in a temporary B-tree:
            // 391 pages took 5.8 s at 100k rows.
            await using ( var connection = await catalog.OpenAsync( CancellationToken.None ) )
            {
                foreach ( var afterPath in new[] { null, "/spec/pager/0300.txt" } )
                {
                    await using var page = SqliteCatalog.CreateIndexedPathsPageCommand( connection, afterPath, 256 );
                    var plan = await ExplainPlanStepsAsync( page );
                    var steps = string.Join( " | ", plan.Select( static step => step.Detail ) );
                    True( plan.Any( static step =>
                            step.Detail.Contains( "COVERING INDEX ix_catalog_items_path", StringComparison.Ordinal ) ) &&
                        !plan.Any( static step => step.Detail.Contains( "TEMP B-TREE", StringComparison.Ordinal ) ),
                        $"page after {afterPath ?? "the start"} reads the path index in order: {steps}" );
                }
            }

            List<string> paged = [];
            string? after = null;
            while ( true )
            {
                var paths = await catalog.GetIndexedPathsAfterAsync( after, 256, CancellationToken.None );
                paged.AddRange( paths );
                if ( paths.Count < 256 )
                {
                    break;
                }

                after = paths[^1];
            }

            string[] expected =
            [
                .. Enumerable.Range( 0, 600 ).Select( static index => $"/spec/pager/{index:D4}.txt" ),
                "/spec/pager/settings.toml"
            ];
            Array.Sort( expected, StringComparer.Ordinal );
            Equal( string.Join( '\n', expected ), string.Join( '\n', paged ) );
        }
        finally
        {
            DeleteDatabase( database );
        }

        static async IAsyncEnumerable<IndexedItem> PagerItems( )
        {
            for ( var index = 599; index >= 0; index-- )
            {
                yield return new IndexedItem
                {
                    StableId = $"file:pager-{index:D4}",
                    Kind = ResultKinds.File,
                    Title = $"{index:D4}.txt",
                    Path = $"/spec/pager/{index:D4}.txt",
                    ProviderId = ProviderIds.Files,
                    SourceId = SourceIds.Files
                };
            }

            foreach ( var key in new[] { "general.gaps", "general.border" } )
            {
                yield return new IndexedItem
                {
                    StableId = $"configuration:pager-{key}",
                    Kind = ResultKinds.Configuration,
                    Title = key,
                    Path = "/spec/pager/settings.toml",
                    ProviderId = "configuration",
                    SourceId = SourceIds.Configuration
                };
            }

            yield return new IndexedItem
            {
                StableId = "app:pager.desktop",
                Kind = ResultKinds.Application,
                Title = "Pager",
                Path = "/spec/pager/0000.desktop",
                ProviderId = "applications",
                SourceId = SourceIds.Applications
            };
            await Task.CompletedTask;
        }
    }

    private static async Task CatalogMigrationLinksExistingSearchRowsAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-search-link-migration-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using ( SqliteCatalog current = new( new StorageOptions { DatabasePath = database } ) )
            {
                await current.ReplaceAllAsync( LinkMigrationItems( ), null, CancellationToken.None );
            }

            SqliteConnection.ClearAllPools( );
            await using ( SqliteConnection connection = new( $"Data Source={database};Pooling=False" ) )
            {
                await connection.OpenAsync( );
                await using var downgrade = connection.CreateCommand( );
                // Recreate the version 19 layout, with search rowids that no longer
                // follow catalog order, as an existing installation has it.
                downgrade.CommandText = """
                    DROP VIEW customizable_items;
                    DROP INDEX ix_catalog_items_path;
                    ALTER TABLE catalog_items DROP COLUMN fts_rowid;
                    ALTER TABLE catalog_items DROP COLUMN title_in_search;
                    ALTER TABLE catalog_items DROP COLUMN metadata_in_search;
                    CREATE VIEW customizable_items AS SELECT * FROM catalog_items UNION ALL SELECT * FROM extension_commands;
                    DELETE FROM schema_migrations WHERE version >= 20;

                    CREATE TEMP TABLE reordered AS
                        SELECT stable_id, kind, source_id, title, subtitle, path, body, config_keys
                        FROM catalog_fts ORDER BY rowid DESC;
                    DELETE FROM catalog_fts;
                    INSERT INTO catalog_fts(stable_id, kind, source_id, title, subtitle, path, body, config_keys)
                        SELECT * FROM temp.reordered;
                    """;
                await downgrade.ExecuteNonQueryAsync( );
            }

            await using SqliteCatalog migrated = new( new StorageOptions { DatabasePath = database } );
            await migrated.InitializeAsync( CancellationToken.None );
            True( !await migrated.RequiresReindexAsync( CancellationToken.None ),
                "linking existing rows keeps the published catalog usable without a rebuild" );
            await using ( var connection = await migrated.OpenAsync( CancellationToken.None ) )
            {
                await using var check = connection.CreateCommand( );
                check.CommandText = """
                    SELECT
                        (SELECT MAX(version) FROM schema_migrations),
                        (SELECT COUNT(*) FROM catalog_items),
                        (SELECT COUNT(*) FROM catalog_items i
                           JOIN catalog_fts f ON f.rowid = i.fts_rowid AND f.stable_id = i.stable_id);
                    """;
                await using var reader = await check.ExecuteReaderAsync( );
                True( await reader.ReadAsync( ) );
                Equal( 23L, reader.GetInt64( 0 ) );
                Equal( 3L, reader.GetInt64( 1 ) );
                Equal( 3L, reader.GetInt64( 2 ) );
            }

            True( (await migrated.CheckIntegrityAsync( CancellationToken.None )).Succeeded );
            var favorite = (await SearchAsync( migrated, "linkmigrationalpha" )).Single( );
            await migrated.SetFavoriteAsync( favorite, true, CancellationToken.None );
            True( (await migrated.GetSnapshotAsync( includeShortcuts: false, CancellationToken.None ))
                .Customizations.Single( ).IsAvailable );

            await migrated.ApplyPathChangesAsync(
                [new IndexedPathChange { Path = "/spec/link/beta.txt" }],
                CancellationToken.None );
            Equal( 0, (await SearchAsync( migrated, "linkmigrationbeta" )).Count );
            Equal( 1, (await SearchAsync( migrated, "linkmigrationalpha" )).Count );
            True( (await migrated.CheckIntegrityAsync( CancellationToken.None )).Succeeded,
                "the incremental delete removed the migrated search row" );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> LinkMigrationItems( )
    {
        foreach ( var name in new[] { "alpha", "beta", "gamma" } )
        {
            yield return new IndexedItem
            {
                StableId = $"file:link-{name}",
                Kind = ResultKinds.File,
                Title = $"{name}.txt",
                Path = $"/spec/link/{name}.txt",
                Body = $"linkmigration{name}",
                ProviderId = ProviderIds.Files,
                SourceId = SourceIds.Files
            };
        }

        await Task.CompletedTask;
    }

    private static bool IsSearchRowidLookup( string detail ) =>
        SearchPlanConstraints( detail ) is { } constraints && constraints.Contains( '=' );

    private static bool IsUnconstrainedSearchScan( string detail ) =>
        SearchPlanConstraints( detail ) is { Length: 0 };

    // catalog_fts is the only virtual table in these statements. FTS5 reports
    // its constraints after "VIRTUAL TABLE INDEX n:"; "=" is a rowid lookup and
    // an empty string is a scan of every search row.
    private static string? SearchPlanConstraints( string detail )
    {
        const string marker = "VIRTUAL TABLE INDEX ";
        var index = detail.IndexOf( marker, StringComparison.Ordinal );
        if ( index < 0 )
        {
            return null;
        }

        var constraints = detail[(index + marker.Length)..];
        var separator = constraints.IndexOf( ':', StringComparison.Ordinal );
        if ( separator < 0 )
        {
            return string.Empty;
        }

        // A join suffix such as " LEFT-JOIN" follows the constraint string.
        return constraints[(separator + 1)..].Split( ' ' )[0];
    }

    private static async Task<IReadOnlyList<(long Id, long Parent, string Detail)>> ExplainPlanStepsAsync(
        SqliteCommand command )
    {
        foreach ( SqliteParameter parameter in command.Parameters )
        {
            parameter.Value ??= parameter.SqliteType == SqliteType.Integer ? 1 : "/spec/plan";
        }

        command.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
        List<(long, long, string)> steps = [];
        await using var reader = await command.ExecuteReaderAsync( );
        while ( await reader.ReadAsync( ) )
        {
            steps.Add( (reader.GetInt64( 0 ), reader.GetInt64( 1 ), reader.GetString( 3 )) );
        }

        return steps;
    }
}
