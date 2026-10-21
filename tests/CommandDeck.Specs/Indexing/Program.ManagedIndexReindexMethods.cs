namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task IndexedFolderPurgeSurvivesPendingReindexRestartAsync( bool reboot )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var removed = Directory.CreateDirectory( Path.Combine( root, "removed" ) ).FullName;
            var retained = Directory.CreateDirectory( Path.Combine( root, "retained" ) ).FullName;
            var revoked = Path.Combine( removed, "revoked.txt" );
            var pending = Path.Combine( retained, "pending.txt" );
            await File.WriteAllTextAsync( revoked, "revokedbeforecompletion" );
            await File.WriteAllTextAsync( pending, "recovery alpha marker" );
            var database = Path.Combine( root, "catalog.db" );
            CommandDeckOptions Options( ) => CreateIncrementalIndexOptions( database, removed, retained );

            await using SqliteCatalog catalog = new( Options( ).Storage );
            await catalog.InitializeAsync( default );
            TrackingSearchCatalog tracking = new( catalog, catalog );
            await using ( IndexCoordinator first = new( tracking, tracking, Options( ), rootPreferences: catalog.LauncherPreferences ) )
            {
                await first.ReindexAsync( null, default );
                await catalog.SetFavoriteAsync( (await SearchAsync( catalog, "revokedbeforecompletion" )).Single( ), true, default );
                var (blocked, release) = tracking.PauseNextReplacement( item => item.Path == pending );
                try
                {
                    var response = await first.ManageRootsAsync( ProtocolOperations.IndexDisable, null,
                        StableId.Create( "index-root", removed ), null, default );
                    True( response.ReindexPending );
                    await blocked.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                    Equal( 0, (await SearchAsync( catalog, "revokedbeforecompletion" )).Count );
                    True( !(await catalog.GetHomeResultsAsync( LauncherPreferences.CreateDefault( ), 50, default ))
                        .Any( row => row.Id == StableId.Create( ResultKinds.File, revoked ) ) );
                    True( await catalog.RequiresReindexAsync( default ) );
                    first.CancelBackgroundWork( );
                    await first.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                }
                finally
                {
                    release( );
                }
            }

            // A lost window or torn tail cannot be certified by the queued
            // folder scan. Metadata comparison cannot reveal this content edit.
            await RewritePreservingMetadataAsync( pending, "recovery bravo marker" );
            if ( reboot )
            {
                await File.WriteAllTextAsync( database + ".index-window", "open|previous-boot" );
            }
            else
            {
                await File.AppendAllTextAsync( database + ".index-events", "torn-tail" );
            }

            // Inspect durable rows before any runtime can finish the pending scan.
            await using SqliteCatalog reopened = new( Options( ).Storage );
            await reopened.InitializeAsync( default );
            Equal( 0, (await SearchAsync( reopened, "revokedbeforecompletion" )).Count );
            True( (await reopened.GetIndexRootsAsync( default )).DisabledConfiguredPaths.Contains( removed ) );
            True( await reopened.RequiresReindexAsync( default ) );
            await using IndexCoordinator restarted = new( reopened, reopened, Options( ), rootPreferences: reopened.LauncherPreferences );
            await restarted.StartAsync( default );
            await restarted.WaitForBackgroundReindexAsync( default ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
            True( !await reopened.RequiresReindexAsync( default ) );
            Equal( 0, (await SearchAsync( reopened, "revokedbeforecompletion" )).Count );
            Equal( 1, (await SearchAsync( reopened, "recovery bravo" )).Count );
            Equal( 0, (await SearchAsync( reopened, "recovery alpha" )).Count );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task CancelledFullReindexKeepsIncrementalEventsAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var documents = Directory.CreateDirectory( Path.Combine( root, "documents" ) ).FullName;
            var file = Path.Combine( documents, "existing.txt" );
            await File.WriteAllTextAsync( file, "beforecancelledscan" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), documents );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            TrackingSearchCatalog tracking = new( catalog, catalog );
            await using IndexCoordinator indexer = new( tracking, tracking, options );
            await indexer.ReindexAsync( null, default );
            var (blocked, release) = tracking.PauseNextReplacement( item => item.Path == file );
            using CancellationTokenSource cancellation = new( );
            var scan = indexer.ReindexAsync( null, cancellation.Token ).AsTask( );
            try
            {
                await blocked.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                cancellation.Cancel( );
                await ThrowsAsync<OperationCanceledException>( ( ) => scan );
            }
            finally
            {
                release( );
            }
            await File.WriteAllTextAsync( file, "aftercancelledscan" );
            True( await WaitForCatalogResultAsync( catalog, "aftercancelledscan",
                row => row.Id == StableId.Create( ResultKinds.File, file ), TimeSpan.FromSeconds( 3 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task IndexedFolderEditSupersedesStartupRecoveryAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var retained = Directory.CreateDirectory( Path.Combine( root, "retained" ) ).FullName;
            var added = Directory.CreateDirectory( Path.Combine( root, "added" ) ).FullName;
            var file = Path.Combine( retained, "existing.txt" );
            await File.WriteAllTextAsync( file, "initialrecoverysignal" );
            var database = Path.Combine( root, "catalog.db" );
            CommandDeckOptions Options( ) => CreateIncrementalIndexOptions( database, retained );
            await using SqliteCatalog catalog = new( Options( ).Storage );
            await catalog.InitializeAsync( default );
            await using ( IndexCoordinator seed = new( catalog, catalog, Options( ), rootPreferences: catalog.LauncherPreferences ) )
            {
                await seed.ReindexAsync( null, default );
            }
            await File.AppendAllTextAsync( database + ".index-events", "torn-tail" );
            TrackingSearchCatalog tracking = new( catalog, catalog );
            var (blocked, release) = tracking.PauseNextReplacement( item => item.Path == file );
            await using IndexCoordinator restarted = new( tracking, tracking, Options( ), rootPreferences: catalog.LauncherPreferences );
            try
            {
                await restarted.StartAsync( default );
                await blocked.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                await restarted.ManageRootsAsync( ProtocolOperations.IndexAdd, added, null, null, default )
                    .AsTask( ).WaitAsync( TimeSpan.FromSeconds( 2 ) );
            }
            finally
            {
                release( );
            }
            await restarted.WaitForBackgroundReindexAsync( default ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
            await File.WriteAllTextAsync( file, "incrementalafterrecoverysignal" );
            True( await WaitForCatalogResultAsync( catalog, "incrementalafterrecoverysignal",
                row => row.Id == StableId.Create( ResultKinds.File, file ), TimeSpan.FromSeconds( 3 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task IndexedFolderEditsReindexAllRootsAndRebuildWatchesAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var retained = Directory.CreateDirectory( Path.Combine( root, "retained" ) ).FullName;
            var added = Directory.CreateDirectory( Path.Combine( root, "added" ) ).FullName;
            var deep = Directory.CreateDirectory( Path.Combine( added, "existing", "deep" ) ).FullName;
            var existing = Path.Combine( deep, "existing.txt" );
            var blocker = Path.Combine( retained, "blocker.txt" );
            await File.WriteAllTextAsync( blocker, "beforefullreindex" );
            await File.WriteAllTextAsync( existing, "addeddeepcontentsignal" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), retained );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            TrackingSearchCatalog tracking = new( catalog, catalog );
            await using IndexCoordinator indexer = new( tracking, tracking, options, rootPreferences: catalog.LauncherPreferences );
            await indexer.ReindexAsync( null, default );
            var generations = tracking.ReplaceAllCount;
            var (blocked, release) = tracking.PauseNextReplacement( item => item.Path == blocker );
            try
            {
                await indexer.ManageRootsAsync( ProtocolOperations.IndexAdd, added, null, null, default );
                await blocked.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                // Repeated edits cancel this generation, and the final scan must
                // use the final effective roots instead of publishing old rows.
                await indexer.ManageRootsAsync( ProtocolOperations.IndexDisable, null,
                    StableId.Create( "index-root", retained ), null, default ).AsTask( ).WaitAsync( TimeSpan.FromSeconds( 2 ) );
                await indexer.ManageRootsAsync( ProtocolOperations.IndexEnable, null,
                    StableId.Create( "index-root", retained ), null, default ).AsTask( ).WaitAsync( TimeSpan.FromSeconds( 2 ) );
            }
            finally
            {
                release( );
            }
            await indexer.WaitForBackgroundReindexAsync( default ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
            True( tracking.ReplaceAllCount > generations );
            Equal( 1, (await SearchAsync( catalog, "addeddeepcontentsignal" )).Count );
            True( indexer.SnapshotWatchedDirectories( ).Contains( deep ) );
            await File.WriteAllTextAsync( existing, "watchedafterrootedit" );
            True( await WaitForCatalogResultAsync( catalog, "watchedafterrootedit", row => row.Id == StableId.Create( ResultKinds.File, existing ),
                TimeSpan.FromSeconds( 3 ) ) );
            Equal( 0, (await SearchAsync( catalog, "addeddeepcontentsignal" )).Count );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }
}
