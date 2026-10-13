using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ScopedRescansStreamWithoutHoldingTheSubtreeAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "documents" );
        var subtree = Path.Combine( root, "large" );
        Directory.CreateDirectory( subtree );

        try
        {
            // 160 bodies of 160 KiB: about 50 MiB as strings if held at once.
            const int fileCount = 160;
            var body = string.Join( ' ', Enumerable.Repeat( "streamed", 160 * 1024 / 9 ) );
            for ( var index = 0; index < fileCount; index++ )
            {
                var directory = Path.Combine( subtree, $"part-{index % 8}" );
                Directory.CreateDirectory( directory );
                await File.WriteAllTextAsync( Path.Combine( directory, $"note-{index:D3}.txt" ), body );
            }

            var options = CreateIncrementalIndexOptions( Path.Combine( sandbox, "catalog.db" ), root );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.StartAsync( CancellationToken.None );
            await indexer.ReindexAsync( progress: null, CancellationToken.None );

            var baseline = GC.GetTotalMemory( forceFullCollection: true );
            var peak = baseline;
            var rescanning = 0;
            var samplesDuringRescan = 0;
            using CancellationTokenSource sampling = new( );
            var sampler = Task.Run( async ( ) =>
            {
                while ( !sampling.IsCancellationRequested )
                {
                    var live = GC.GetTotalMemory( forceFullCollection: true );
                    if ( Volatile.Read( ref rescanning ) == 1 )
                    {
                        peak = Math.Max( peak, live );
                        samplesDuringRescan++;
                    }

                    await Task.Delay( 5, CancellationToken.None );
                }
            }, CancellationToken.None );

            Volatile.Write( ref rescanning, 1 );
            var report = await indexer.ReindexAsync(
                new IndexReindexScope { Kind = IndexReindexScopeKinds.Directory, Path = subtree },
                progress: null,
                CancellationToken.None );
            Volatile.Write( ref rescanning, 0 );
            await sampling.CancelAsync( );
            await sampler;

            // Holding the subtree kept about 50 MiB live until the catalog write.
            Equal( ( long ) fileCount, report.Indexed );
            Equal( 10, (await SearchAsync( catalog, "streamed" )).Count );
            True( samplesDuringRescan >= 3 );
            True( peak - baseline < 16 * 1024 * 1024 );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task ScopedAndOverflowRescansKeepFullScanRowsAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "root" );
        var nested = Path.Combine( root, "a", "b" );

        try
        {
            CreateTraversalPolicyFixture( root );
            await File.WriteAllTextAsync( Path.Combine( root, "notes.txt" ), "equivalence marker" );
            Directory.CreateDirectory( Path.Combine( root, "config" ) );
            await File.WriteAllTextAsync( Path.Combine( root, "config", "app.json" ), """{"window":{"width":800}}""" );
            await File.WriteAllTextAsync( Path.Combine( root, "config", "app.toml" ), "[theme]\nname = \"dark\"\n" );
            await File.WriteAllTextAsync( Path.Combine( nested, ".nested-hidden.txt" ), "nested hidden marker" );

            (var fixtureIndexing, var privacy) = TraversalPolicyOptions( root );
            var outer = fixtureIndexing.Roots[0];
            var database = Path.Combine( sandbox, "catalog.db" );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = database },
                Privacy = privacy,
                Indexing = new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions
                        {
                            Path = root,
                            IncludeContentInSearch = true,
                            ExcludeGlobs = outer.ExcludeGlobs,
                            IncludeGlobs = outer.IncludeGlobs
                        },
                        new IndexRootOptions { Path = nested, IncludeHidden = true, IncludeContentInSearch = true }
                    ],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false,
                    IncrementalDebounceMilliseconds = 40,
                    IncrementalMaximumLatencyMilliseconds = 300
                }
            };
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            List<string> expected;
            await using ( IndexCoordinator indexer = new( catalog, catalog, options ) )
            {
                await indexer.StartAsync( CancellationToken.None );
                await indexer.ReindexAsync( progress: null, CancellationToken.None );
                expected = await SnapshotCatalogRowsAsync( database );
                True( expected.Any( row => row.Contains( ".nested-hidden.txt", StringComparison.Ordinal ) ) );
                True( expected.Any( row => row.Contains( "window.width", StringComparison.Ordinal ) ) );

                var status = await indexer.GetIncrementalStatusAsync( CancellationToken.None );
                foreach ( var rootStatus in status.Roots )
                {
                    await indexer.ReindexAsync(
                        new IndexReindexScope { Kind = IndexReindexScopeKinds.Root, RootId = rootStatus.Id },
                        progress: null,
                        CancellationToken.None );
                    AssertSameRows( expected, await SnapshotCatalogRowsAsync( database ), $"root {rootStatus.Path}" );
                }

                foreach ( var directory in new[] { Path.Combine( root, "a" ), Path.Combine( root, "repo" ), root } )
                {
                    await indexer.ReindexAsync(
                        new IndexReindexScope { Kind = IndexReindexScopeKinds.Directory, Path = directory },
                        progress: null,
                        CancellationToken.None );
                    AssertSameRows( expected, await SnapshotCatalogRowsAsync( database ), $"directory {directory}" );
                }

                indexer.ReportWatcherOverflow( root );
                // Rebuilding the outer tree also interrupts its nested root;
                // one recovery scan covers both gaps and preserves every row.
                True( await WaitUntilAsync( async ( ) =>
                {
                    var current = await indexer.GetIncrementalStatusAsync( CancellationToken.None );
                    return !current.ReconciliationRequired && current.PendingEventCount == 0 && current.LastReconciledRootCount == 2;
                }, TimeSpan.FromSeconds( 5 ) ) );
                AssertSameRows( expected, await SnapshotCatalogRowsAsync( database ), "overflow" );
            }

            // A catalog that cannot stage a rescan receives the same rows at once.
            TrackingSearchCatalog tracking = new( catalog, catalog );
            await using ( IndexCoordinator fallback = new( tracking, tracking, options ) )
            {
                await fallback.ReindexAsync(
                    new IndexReindexScope { Kind = IndexReindexScopeKinds.Directory, Path = root },
                    progress: null,
                    CancellationToken.None );
                True( tracking.ApplyPathChangesCount > 0 );
                AssertSameRows( expected, await SnapshotCatalogRowsAsync( database ), "materialized fallback" );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task<List<string>> SnapshotCatalogRowsAsync( string database )
    {
        List<string> rows = [];
        await using SqliteConnection connection = new( $"Data Source={database};Mode=ReadOnly;Pooling=False" );
        await connection.OpenAsync( );
        foreach ( var table in new[] { "catalog_items", "catalog_fts" } )
        {
            await using var command = connection.CreateCommand( );
            command.CommandText = $"SELECT * FROM {table};";
            await using var reader = await command.ExecuteReaderAsync( );
            // Search rowids are storage links, renumbered by every publication;
            // the rows they link are compared instead.
            var columns = Enumerable.Range( 0, reader.FieldCount )
                .Where( index => reader.GetName( index ) != "fts_rowid" )
                .ToArray( );
            while ( await reader.ReadAsync( ) )
            {
                var values = columns
                    .Select( index => reader.IsDBNull( index ) ? "<null>" : Convert.ToString( reader.GetValue( index ), System.Globalization.CultureInfo.InvariantCulture ) );
                rows.Add( $"{table}|{string.Join( '|', values )}" );
            }
        }

        rows.Sort( StringComparer.Ordinal );
        return rows;
    }

    private static void AssertSameRows( List<string> expected, List<string> actual, string scope )
    {
        var missing = expected.Except( actual, StringComparer.Ordinal ).FirstOrDefault( );
        var extra = actual.Except( expected, StringComparer.Ordinal ).FirstOrDefault( );
        Equal( $"{scope}: {expected.Count} rows", $"{scope}: {actual.Count} rows" );
        Equal( $"{scope}: missing <none>", $"{scope}: missing {missing ?? "<none>"}" );
        Equal( $"{scope}: extra <none>", $"{scope}: extra {extra ?? "<none>"}" );
    }
}
