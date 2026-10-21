namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExercisePendingRootMaintenanceAsync( bool repair )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var retained = Directory.CreateDirectory( Path.Combine( root, "retained" ) ).FullName;
            var added = Directory.CreateDirectory( Path.Combine( root, "added" ) ).FullName;
            var pending = Path.Combine( added, "pending.txt" );
            await File.WriteAllTextAsync( pending, "pendingmaintenancesignal" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), retained );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            TrackingSearchCatalog tracking = new( catalog, catalog );
            await using IndexCoordinator indexer = new( tracking, tracking, options, rootPreferences: catalog.LauncherPreferences );
            await indexer.ReindexAsync( null, default );
            var (blocked, release) = tracking.PauseNextReplacement( item => item.Path == pending );
            try
            {
                await indexer.ManageRootsAsync( ProtocolOperations.IndexAdd, added, null, null, default );
                await blocked.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                var scan = indexer.BackgroundReindex;
                var attempts = tracking.ReplaceAllCount;

                var result = repair ? await indexer.RepairAsync( default ) : await indexer.PurgeProjectionAsync( default );
                True( result.Succeeded && result.RebuildRequired );
                release( );
                await scan.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                await indexer.StartAsync( default );
                var listing = await indexer.ManageRootsAsync( ProtocolOperations.IndexRoots, null, null, null, default );
                True( !listing.ReindexPending );
                Equal( attempts, tracking.ReplaceAllCount );
                True( await catalog.RequiresReindexAsync( default ) );
                Equal( 0, (await SearchAsync( catalog, "pendingmaintenancesignal" )).Count );
                var status = await indexer.GetIncrementalStatusAsync( default );
                True( status.ReconciliationRequired );
                Equal( repair ? "catalog-repair" : "catalog-purge", status.ReconciliationReason );

                await File.WriteAllTextAsync( pending, "changedwhileheldsignal" );
                await indexer.ReindexAsync( null, default );
                Equal( 1, (await SearchAsync( catalog, "changedwhileheldsignal" )).Count );
                True( !await catalog.RequiresReindexAsync( default ) );
                True( !(await indexer.GetIncrementalStatusAsync( default )).ReconciliationRequired );
                await File.WriteAllTextAsync( pending, "aftermaintenanceholdsignal" );
                True( await WaitForCatalogResultAsync( catalog, "aftermaintenanceholdsignal",
                    row => row.Id == StableId.Create( ResultKinds.File, pending ), TimeSpan.FromSeconds( 5 ) ) );
            }
            finally
            {
                release( );
                indexer.CancelBackgroundWork( );
                await indexer.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task IndexedFolderEmptyResetRequiresOwnershipAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var retained = Directory.CreateDirectory( Path.Combine( root, "retained" ) ).FullName;
            var added = Directory.CreateDirectory( Path.Combine( root, "added" ) ).FullName;
            var file = Path.Combine( added, "owned.txt" );
            await File.WriteAllTextAsync( file, "ownedrootsignal" );
            var database = Path.Combine( root, "catalog.db" );
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( default );
            await using IndexCoordinator owner = new( catalog, catalog, CreateIncrementalIndexOptions( database, retained ),
                rootPreferences: catalog.LauncherPreferences );
            await owner.ReindexAsync( null, default );
            await using IndexCoordinator reader = new( catalog, catalog, CreateIncrementalIndexOptions( database, retained ),
                rootPreferences: catalog.LauncherPreferences );
            await reader.StartAsync( default );

            // An empty overlay must not admit a non-owner to an unchecked reset.
            // The owner can commit a root at any point after this read.
            Equal( 0, (await catalog.GetIndexRootsAsync( default )).Added.Count );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await reader.ResetPreferencesAsync( default ) );
            await owner.ManageRootsAsync( ProtocolOperations.IndexAdd, added, null, null, default );
            await owner.WaitForBackgroundReindexAsync( default ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await reader.ResetPreferencesAsync( default ) );
            Equal( added, (await catalog.GetIndexRootsAsync( default )).Added.Single( ).Options.Path );
            True( owner.SnapshotWatchedDirectories( ).Contains( added ) );
            Equal( 1, (await SearchAsync( catalog, "ownedrootsignal" )).Count );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }
}
