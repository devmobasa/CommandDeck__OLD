using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task IndexedFolderSymlinkReportsPendingAndFailedWorkAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var retained = Directory.CreateDirectory( Path.Combine( root, "retained" ) ).FullName;
            var target = Directory.CreateDirectory( Path.Combine( root, "target" ) ).FullName;
            var link = Path.Combine( root, "link" );
            Directory.CreateSymbolicLink( link, target );
            var file = Path.Combine( target, "pending.txt" );
            await File.WriteAllTextAsync( file, "symlinkpendingsignal" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), retained );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            TrackingSearchCatalog tracking = new( catalog, catalog );
            await using IndexCoordinator indexer = new( tracking, tracking, options, rootPreferences: catalog.LauncherPreferences );
            await indexer.ReindexAsync( null, default );
            var (visited, release) = tracking.PauseNextReplacement( item => item.Path == file );
            try
            {
                var added = await indexer.ManageRootsAsync( ProtocolOperations.IndexAdd, link + "/", null, null, default );
                var id = added.Roots.Single( row => row.Path == link ).Id;
                await visited.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                var pending = await indexer.ManageRootsAsync( ProtocolOperations.IndexRoots, null, null, null, default );
                True( pending.Roots.Single( row => row.Id == id ).Status.StartsWith( "Indexing ·", StringComparison.Ordinal ) );

                await using var connection = await catalog.OpenAsync( default );
                await using var fail = connection.CreateCommand( );
                fail.CommandText = """
                    CREATE TRIGGER fail_symlink_publication BEFORE INSERT ON catalog_items
                    BEGIN SELECT RAISE(ABORT, 'blocked symlink publication'); END;
                    """;
                await fail.ExecuteNonQueryAsync( );
                release( );
                True( await WaitUntilAsync( async ( ) => (await indexer.ManageRootsAsync(
                    ProtocolOperations.IndexRoots, null, null, null, default )).Roots.Single( row => row.Id == id ).Status == "Retrying indexing",
                    TimeSpan.FromSeconds( 5 ) ) );

                fail.CommandText = "DROP TRIGGER fail_symlink_publication;";
                await fail.ExecuteNonQueryAsync( );
                await indexer.WaitForBackgroundReindexAsync( default ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
                Equal( 1, (await SearchAsync( catalog, "symlinkpendingsignal" )).Count );
                var completed = await indexer.ManageRootsAsync( ProtocolOperations.IndexRoots, null, null, null, default );
                Equal( "Watching", completed.Roots.Single( row => row.Id == id ).Status );
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

    private static async Task IndexedFolderTrailingSeparatorRestoresWatchesAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var parent = Directory.CreateDirectory( Path.Combine( root, "parent" ) ).FullName;
            var nested = Directory.CreateDirectory( Path.Combine( parent, "nested" ) ).FullName;
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), parent + "/" );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            await using IndexCoordinator indexer = new( catalog, catalog, options, rootPreferences: catalog.LauncherPreferences );
            await indexer.ReindexAsync( null, default );

            foreach ( var add in new[] { true, false } )
            {
                var listing = await indexer.ManageRootsAsync( ProtocolOperations.IndexRoots, null, null, null, default );
                await indexer.ManageRootsAsync( add ? ProtocolOperations.IndexAdd : ProtocolOperations.IndexRemove,
                    add ? nested : null, add ? null : listing.Roots.Single( row => row.Path == nested ).Id, null, default );
                await indexer.WaitForBackgroundReindexAsync( default ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
                True( indexer.SnapshotWatchedDirectories( ).Contains( parent ) );

                var signal = add ? "afteraddsignal" : "afterremovesignal";
                foreach ( var directory in new[] { parent, nested } )
                {
                    var file = Path.Combine( directory, signal + ".txt" );
                    await File.WriteAllTextAsync( file, signal );
                    True( await WaitForCatalogResultAsync( catalog, signal, row => row.Id == StableId.Create( ResultKinds.File, file ), TimeSpan.FromSeconds( 5 ) ) );
                }
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }
}
