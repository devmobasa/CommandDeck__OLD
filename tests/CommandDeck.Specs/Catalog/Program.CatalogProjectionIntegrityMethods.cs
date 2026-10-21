using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ProjectionConsistencyAvoidsNestedSearchScansAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-projection-plan-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await using var connection = await catalog.OpenAsync( CancellationToken.None );

            // A LEFT JOIN on the UNINDEXED stable_id scanned every search row for
            // each catalog row: 43.7 s at 10,600 rows, about an hour at 100k.
            await using var command = SqliteCatalog.CreateProjectionConsistencyCommand( connection );
            var plan = await ExplainPlanStepsAsync( command );
            var steps = string.Join( " | ", plan.Select( static step => step.Detail ) );
            True( !HasNestedSearchScan( plan ), $"no search scan runs per catalog row: {steps}" );
            True( plan.Any( static step => IsSearchRowidLookup( step.Detail ) ),
                $"linked search rows are probed by rowid: {steps}" );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task ProjectionConsistencyDetectsEveryDriftAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-projection-drift-{Guid.NewGuid( ):N}.db" );
        (string Sql, string Counts)[] drifts =
        [
            ("DELETE FROM catalog_fts WHERE stable_id = 'file:drift-a';",
                "projection=2, missing=1, orphan=0, mismatched=0, duplicate=0"),
            ("""
                INSERT INTO catalog_fts(stable_id, kind, source_id, title, subtitle, path, body, config_keys)
                VALUES('file:ghost', 'file', 'files', 'ghost.txt', '', '/spec/drift/ghost.txt', '', '');
                """,
                "projection=4, missing=0, orphan=1, mismatched=0, duplicate=0"),
            ("UPDATE catalog_fts SET title = 'renamed.txt' WHERE stable_id = 'file:drift-b';",
                "projection=3, missing=0, orphan=0, mismatched=1, duplicate=0"),
            ("""
                INSERT INTO catalog_fts(stable_id, kind, source_id, title, subtitle, path, body, config_keys)
                SELECT stable_id, kind, source_id, title, subtitle, path, body, config_keys
                FROM catalog_fts WHERE stable_id = 'file:drift-c';
                """,
                "projection=4, missing=0, orphan=1, mismatched=0, duplicate=1"),
            ("UPDATE catalog_items SET fts_rowid = 999999 WHERE stable_id = 'file:drift-b';",
                "projection=3, missing=1, orphan=1, mismatched=0, duplicate=0")
        ];
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.ReplaceAllAsync( DriftItems( ), null, CancellationToken.None );

            foreach ( (var sql, var counts) in drifts )
            {
                await using ( var connection = await catalog.OpenAsync( CancellationToken.None ) )
                {
                    await using var drift = connection.CreateCommand( );
                    drift.CommandText = sql;
                    await drift.ExecuteNonQueryAsync( );
                }

                var detected = await catalog.CheckIntegrityAsync( CancellationToken.None );
                var messages = string.Join( ' ', detected.Messages );
                True( !detected.Succeeded && detected.RebuildRequired, $"drift detected: {sql}" );
                True( messages.Contains( $"items=3, {counts}.", StringComparison.Ordinal ),
                    $"expected {counts}: {messages}" );

                var repaired = await catalog.RepairAsync( CancellationToken.None );
                Equal( 3L, repaired.AffectedItemCount );
                True( (await catalog.CheckIntegrityAsync( CancellationToken.None )).Succeeded,
                    $"repair restores the projection after: {sql}" );
            }

            await catalog.ApplyPathChangesAsync(
                [new IndexedPathChange { Path = "/spec/drift/drift-a.txt" }],
                CancellationToken.None );
            Equal( 0, (await SearchAsync( catalog, "driftabody" )).Count );
            Equal( 2, (await SearchAsync( catalog, "drifttoken" )).Count );
            True( (await catalog.CheckIntegrityAsync( CancellationToken.None )).Succeeded,
                "repaired links still delete the right search rows" );
        }
        finally
        {
            DeleteDatabase( database );
        }

        static async IAsyncEnumerable<IndexedItem> DriftItems( )
        {
            foreach ( var name in new[] { "drift-a", "drift-b", "drift-c" } )
            {
                yield return new IndexedItem
                {
                    StableId = $"file:{name}",
                    Kind = ResultKinds.File,
                    Title = $"{name}.txt",
                    Path = $"/spec/drift/{name}.txt",
                    Body = $"drifttoken drift{name[^1]}body",
                    ProviderId = ProviderIds.Files,
                    SourceId = SourceIds.Files
                };
            }

            await Task.CompletedTask;
        }
    }

    private static async Task IntegrityCheckAndRepairScaleLinearlyAsync( )
    {
        const int itemCount = 12_000;
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-projection-scale-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.ReplaceAllAsync( ScaleItems( ), null, CancellationToken.None );

            var started = Stopwatch.GetTimestamp( );
            var integrity = await catalog.CheckIntegrityAsync( CancellationToken.None );
            var repair = await catalog.RepairAsync( CancellationToken.None );
            var elapsed = Stopwatch.GetElapsedTime( started );

            True( integrity.Succeeded );
            Equal( 0L, repair.AffectedItemCount );
            // Linear checks take tens of milliseconds here; the quadratic join
            // took minutes. The bound only needs to separate the two.
            True( elapsed < TimeSpan.FromSeconds( 10 ),
                $"integrity and repair of {itemCount} rows took {elapsed.TotalMilliseconds:F0} ms" );
            Console.WriteLine( $"MEASURE projection integrity+repair {itemCount} rows={elapsed.TotalMilliseconds:F0}ms" );
        }
        finally
        {
            DeleteDatabase( database );
        }

        static async IAsyncEnumerable<IndexedItem> ScaleItems( )
        {
            for ( var index = 0; index < itemCount; index++ )
            {
                yield return new IndexedItem
                {
                    StableId = $"file:scale-{index}",
                    Kind = ResultKinds.File,
                    Title = $"scale-{index}.txt",
                    Path = $"/spec/scale/{index % 50}/scale-{index}.txt",
                    Body = $"scale body {index}",
                    ProviderId = ProviderIds.Files,
                    SourceId = SourceIds.Files
                };
            }

            await Task.CompletedTask;
        }
    }

    // Sibling plan steps under one parent are nested loops in order. A search
    // scan without a rowid or MATCH constraint after another loop runs once
    // per outer row.
    private static bool HasNestedSearchScan( IReadOnlyList<(long Id, long Parent, string Detail)> plan ) =>
        plan.GroupBy( static step => step.Parent ).Any( static group =>
        {
            var loops = group
                .Where( static step =>
                    step.Detail.StartsWith( "SCAN ", StringComparison.Ordinal ) ||
                    step.Detail.StartsWith( "SEARCH ", StringComparison.Ordinal ) )
                .ToList( );
            return loops.Skip( 1 ).Any( static step => IsUnconstrainedSearchScan( step.Detail ) );
        } );
}
