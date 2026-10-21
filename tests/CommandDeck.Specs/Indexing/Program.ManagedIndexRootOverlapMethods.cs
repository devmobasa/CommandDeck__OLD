namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task IndexedFolderOverlapAndRestartAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var parent = Directory.CreateDirectory( Path.Combine( root, "files" ) ).FullName;
            var nested = Directory.CreateDirectory( Path.Combine( parent, "nested" ) ).FullName;
            var outside = Directory.CreateDirectory( Path.Combine( root, "outside" ) ).FullName;
            await File.WriteAllTextAsync( Path.Combine( nested, "nestedrow.txt" ), "nestedcontentsignal" );
            await File.WriteAllTextAsync( Path.Combine( nested, ".hidden.txt" ), "hiddencontentsignal" );
            await File.WriteAllTextAsync( Path.Combine( outside, "escapedrow.txt" ), "escapedcontentsignal" );
            Directory.CreateSymbolicLink( Path.Combine( nested, "escape" ), outside );
            var database = Path.Combine( root, "catalog.db" );
            CommandDeckOptions Options( )
            {
                var options = CreateIncrementalIndexOptions( database, parent );
                options.Indexing.Roots =
                [
                    new IndexRootOptions { Path = parent, IncludeContentInSearch = false },
                    new IndexRootOptions { Path = parent + "/./" }
                ];
                return options;
            }

            var currentOptions = Options( );
            FilePreviewService previews = new( currentOptions.Indexing, currentOptions.Privacy );
            var file = Path.Combine( nested, "nestedrow.txt" );
            string configuredId;
            await using ( SqliteCatalog catalog = new( currentOptions.Storage ) )
            await using ( IndexCoordinator indexer = new( catalog, catalog, currentOptions, rootPreferences: catalog.LauncherPreferences ) )
            {
                await catalog.InitializeAsync( default );
                var listed = await indexer.ManageRootsAsync( ProtocolOperations.IndexRoots, null, null, null, default );
                configuredId = listed.Roots.Single( ).Id;
                True( !(await previews.ReadAsync( file, default )).Succeeded );
                var added = await indexer.ManageRootsAsync( ProtocolOperations.IndexAdd, nested, null, null, default );
                await indexer.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                var addedId = added.Roots.Single( value => value.Source == "added" ).Id;
                Equal( 1, (await SearchAsync( catalog, "nestedcontentsignal" )).Count );
                True( (await previews.ReadAsync( file, default )).Succeeded );
                Equal( 0, (await SearchAsync( catalog, "hiddencontentsignal" )).Count );
                Equal( 0, (await SearchAsync( catalog, "escapedrow" )).Count );

                await indexer.ManageRootsAsync( ProtocolOperations.IndexRemove, null, addedId, null, default );
                await indexer.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                Equal( 1, (await SearchAsync( catalog, "nestedrow" )).Count );
                Equal( 0, (await SearchAsync( catalog, "nestedcontentsignal" )).Count );
                True( !(await previews.ReadAsync( file, default )).Succeeded );
                await indexer.ManageRootsAsync( ProtocolOperations.IndexDisable, null, configuredId, null, default );
                Equal( 0, indexer.SnapshotWatchedDirectories( ).Count );
                Equal( 0, (await SearchAsync( catalog, "nestedrow" )).Count );
            }

            await using SqliteCatalog reopened = new( Options( ).Storage );
            await reopened.InitializeAsync( default );
            await using IndexCoordinator restarted = new( reopened, reopened, Options( ), rootPreferences: reopened.LauncherPreferences );
            var afterRestart = await restarted.ManageRootsAsync( ProtocolOperations.IndexRoots, null, null, null, default );
            True( !afterRestart.Roots.Single( ).Enabled );
            Equal( 0, restarted.SnapshotWatchedDirectories( ).Count );
            await restarted.ManageRootsAsync( ProtocolOperations.IndexEnable, null, configuredId, null, default );
            await restarted.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( 1, (await SearchAsync( reopened, "nestedrow" )).Count );
            Equal( 0, (await SearchAsync( reopened, "nestedcontentsignal" )).Count );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }
}
