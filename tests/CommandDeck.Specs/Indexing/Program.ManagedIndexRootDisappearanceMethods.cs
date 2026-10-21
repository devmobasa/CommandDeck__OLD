namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExerciseUnavailableRootLinkAsync( bool configured, bool retargeted )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "Native symlink root watches require Linux." );
        }

        var root = CreateIncrementalSpecRoot( );
        try
        {
            var target = Directory.CreateDirectory( Path.Combine( root, "target" ) ).FullName;
            var replacement = Directory.CreateDirectory( Path.Combine( root, "replacement" ) ).FullName;
            var link = Path.Combine( root, "link" );
            Directory.CreateSymbolicLink( link, target );
            await File.WriteAllTextAsync( Path.Combine( target, "original.txt" ), "symlinkrootsignal" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), link );
            if ( !configured )
            {
                options.Indexing.Roots = [];
            }
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            await using IndexCoordinator indexer = new( catalog, catalog, options, rootPreferences: catalog.LauncherPreferences );
            await indexer.ReindexAsync( null, default );
            var listing = await indexer.ManageRootsAsync( configured ? ProtocolOperations.IndexRoots : ProtocolOperations.IndexAdd,
                configured ? null : link, null, null, default );
            await indexer.WaitForBackgroundReindexAsync( default ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
            True( indexer.SnapshotWatchedDirectories( ).Contains( link ) );
            Equal( 1, (await indexer.GetIncrementalStatusAsync( default )).WatchedRootCount );
            Equal( 1, (await SearchAsync( catalog, "symlinkrootsignal" )).Count );

            Directory.Delete( link );
            if ( retargeted )
            {
                Directory.CreateSymbolicLink( link, replacement );
            }
            var result = await indexer.ManageRootsAsync( configured ? ProtocolOperations.IndexDisable : ProtocolOperations.IndexRemove,
                null, listing.Roots.Single( ).Id, null, default );
            await indexer.WaitForBackgroundReindexAsync( default ).WaitAsync( TimeSpan.FromSeconds( 5 ) );

            True( result.Roots.All( row => !row.Enabled ) );
            Equal( 0, (await indexer.GetIncrementalStatusAsync( default )).WatchedRootCount );
            Equal( 0, indexer.SnapshotWatchedDirectories( ).Count );
            Equal( 0, (await SearchAsync( catalog, "symlinkrootsignal" )).Count );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

}
