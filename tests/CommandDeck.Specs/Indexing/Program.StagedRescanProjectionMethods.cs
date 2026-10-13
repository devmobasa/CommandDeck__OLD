using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task StagedRescansKeepTheSearchLinkAndInclusionAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "documents" );
        var nested = Path.Combine( root, "reports" );
        Directory.CreateDirectory( nested );
        var publicFile = Path.Combine( root, "stagepublic.txt" );
        var quarterlyFile = Path.Combine( nested, "quarterlyzeta.txt" );
        await File.WriteAllTextAsync( publicFile, "stagepublicbody marker" );
        await File.WriteAllTextAsync( quarterlyFile, "stagequarterlybody marker" );
        var database = Path.Combine( sandbox, "catalog.db" );

        try
        {
            // Quarterly files keep their names and paths out of search, not their content.
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = database },
                Indexing = new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions
                        {
                            Path = root,
                            FileTypePolicies =
                            [
                                new FileTypeIndexPolicyOptions
                                {
                                    Pattern = "quarterly*",
                                    IncludeFileNameInSearch = false,
                                    IncludeMetadataInSearch = false
                                }
                            ]
                        }
                    ],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false,
                    IncrementalDebounceMilliseconds = 40,
                    IncrementalMaximumLatencyMilliseconds = 300
                }
            };
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.StartAsync( CancellationToken.None );
            await indexer.ReindexAsync( progress: null, CancellationToken.None );

            // A directory, a root, and an overflow rescan all stage their rows.
            await indexer.ReindexAsync(
                new IndexReindexScope { Kind = IndexReindexScopeKinds.Directory, Path = nested },
                progress: null,
                CancellationToken.None );
            var rootId = (await indexer.GetIncrementalStatusAsync( CancellationToken.None )).Roots.Single( ).Id;
            await indexer.ReindexAsync(
                new IndexReindexScope { Kind = IndexReindexScopeKinds.Root, RootId = rootId },
                progress: null,
                CancellationToken.None );
            indexer.ReportWatcherOverflow( root );
            True( await WaitUntilAsync( async ( ) =>
            {
                var status = await indexer.GetIncrementalStatusAsync( CancellationToken.None );
                return !status.ReconciliationRequired && status.PendingEventCount == 0 && status.LastReconciledRootCount == 1;
            }, TimeSpan.FromSeconds( 5 ) ) );

            var integrity = await catalog.CheckIntegrityAsync( CancellationToken.None );
            True( integrity.Succeeded && !integrity.RebuildRequired, string.Join( ' ', integrity.Messages ) );
            Equal( 0L, await CountUnlinkedCatalogRowsAsync( database ) );

            // The excluded name stays out of search, exactly and fuzzily; its
            // content and the included sibling stay in.
            var quarterlyId = StableId.Create( ResultKinds.File, quarterlyFile );
            Equal( "||", await ReadSearchFieldsAsync( database, quarterlyId ) );
            Equal( "0|0", await ReadSearchInclusionAsync( database, quarterlyId ) );
            Equal( 0, (await SearchAsync( catalog, "quarterlyzeta" )).Count );
            Equal( 0, (await SearchAsync( catalog, "quarterlyzetaa" )).Count );
            Equal( quarterlyId, (await SearchAsync( catalog, "stagequarterlybody" )).Single( ).Id );

            // An incremental delete finds the rescanned row's search row by rowid.
            File.Delete( publicFile );
            await catalog.ApplyPathChangesAsync( [new IndexedPathChange { Path = publicFile }], CancellationToken.None );
            Equal( 0, (await SearchAsync( catalog, "stagepublicbody" )).Count );
            True( (await catalog.CheckIntegrityAsync( CancellationToken.None )).Succeeded );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    // Catalog rows whose fts_rowid does not name their own search row.
    private static async Task<long> CountUnlinkedCatalogRowsAsync( string database )
    {
        await using SqliteConnection connection = new( $"Data Source={database};Pooling=False" );
        await connection.OpenAsync( );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT count(*)
            FROM catalog_items i
            LEFT JOIN catalog_fts f ON f.rowid = i.fts_rowid
            WHERE f.stable_id IS NOT i.stable_id;
            """;

        return ( long ) (await command.ExecuteScalarAsync( ))!;
    }

    private static async Task<string> ReadSearchInclusionAsync( string database, string stableId )
    {
        await using SqliteConnection connection = new( $"Data Source={database};Pooling=False" );
        await connection.OpenAsync( );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT title_in_search || '|' || metadata_in_search
            FROM catalog_items WHERE stable_id = $stable_id;
            """;
        command.Parameters.AddWithValue( "$stable_id", stableId );

        return ( string ) (await command.ExecuteScalarAsync( ))!;
    }
}
