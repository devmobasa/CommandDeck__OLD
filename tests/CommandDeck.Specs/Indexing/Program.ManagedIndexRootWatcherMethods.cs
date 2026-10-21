using System.Collections.Concurrent;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task IndexedFolderEditPreservesUnrelatedOfflinePolicyInputsAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var retained = Directory.CreateDirectory( Path.Combine( root, "retained" ) ).FullName;
            var added = Directory.CreateDirectory( Path.Combine( root, "added" ) ).FullName;
            Directory.CreateDirectory( Path.Combine( retained, ".git" ) );
            var ignore = Path.Combine( retained, ".gitignore" );
            await File.WriteAllTextAsync( ignore, "" );
            await File.WriteAllTextAsync( Path.Combine( retained, "ignoredlater.txt" ), "offlinepolicycontentsignal" );
            var database = Path.Combine( root, "catalog.db" );
            var options = CreateIncrementalIndexOptions( database, retained );
            await using ( SqliteCatalog catalog = new( options.Storage ) )
            await using ( IndexCoordinator indexer = new( catalog, catalog, options, rootPreferences: catalog.LauncherPreferences ) )
            {
                await catalog.InitializeAsync( default );
                await indexer.ReindexAsync( null, default );
                Equal( 1, (await SearchAsync( catalog, "offlinepolicycontentsignal" )).Count );
                await indexer.ManageRootsAsync( ProtocolOperations.IndexAdd, added, null, null, default );
                await indexer.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            }

            await File.WriteAllTextAsync( ignore, "ignoredlater.txt\n" );
            var configured = CreateIncrementalIndexOptions( database, retained );
            var restartOptions = new CommandDeckOptions
            {
                Storage = configured.Storage,
                Privacy = configured.Privacy,
                Indexing = configured.Indexing with { EnableIncrementalIndexing = false }
            };
            await using SqliteCatalog reopened = new( restartOptions.Storage );
            await using IndexCoordinator restarted = new( reopened, reopened, restartOptions, rootPreferences: reopened.LauncherPreferences );
            await restarted.StartAsync( default );
            await restarted.PolicyReconciliation.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( 0, (await SearchAsync( reopened, "offlinepolicycontentsignal" )).Count );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task IndexedFolderFilesystemRootRetainsCoveredRowsAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            Directory.CreateDirectory( root );
            const string file = "/catalog-range-fixture/coveredrow.txt";
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "state", "catalog.db" ), "/" );
            using FileIndexSource files = new( options.Indexing, options.Privacy, options.Storage.DatabasePath );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            await catalog.ApplyPathChangesAsync( [new IndexedPathChange
            {
                Path = file,
                Items = [new IndexedItem
                {
                    StableId = StableId.Create( ResultKinds.File, file ), Kind = ResultKinds.File,
                    Title = "coveredrow.txt", Path = file, SourceId = SourceIds.Files, ProviderId = ProviderIds.Files
                }]
            }], default );
            await catalog.SetFavoriteAsync( (await SearchAsync( catalog, "coveredrow" )).Single( ), true, default );

            var store = ( IIndexRootChangeStore ) catalog.LauncherPreferences;
            await store.CommitRootsAsync( new IndexRootPreferences( ), files.Exclusions, files.Exclusions, false, default );
            Equal( 1, (await SearchAsync( catalog, "coveredrow" )).Count );
            True( (await catalog.GetHomeResultsAsync( LauncherPreferences.CreateDefault( ), 20, default )).Any( row =>
                row.Id == StableId.Create( ResultKinds.File, file ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task IndexedFolderScanPreservesUnrelatedPendingEventsAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var retained = Directory.CreateDirectory( Path.Combine( root, "retained" ) ).FullName;
            var added = Directory.CreateDirectory( Path.Combine( root, "added" ) ).FullName;
            var file = Path.Combine( retained, "queued.txt" );
            await File.WriteAllTextAsync( file, "beforeeditsignal" );
            await File.WriteAllTextAsync( Path.Combine( added, "new.txt" ), "newrootsignal" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), retained,
                debounceMilliseconds: 500, maximumLatencyMilliseconds: 950 );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            await using IndexCoordinator indexer = new( catalog, catalog, options, rootPreferences: catalog.LauncherPreferences );
            await indexer.ReindexAsync( null, default );
            await File.WriteAllTextAsync( file, "aftereditsignal" );
            True( await WaitUntilAsync( ( ) => Task.FromResult( indexer.SnapshotPendingEvents( ).Any( change => change.Path == file ) ), TimeSpan.FromSeconds( 5 ) ) );

            await indexer.ManageRootsAsync( ProtocolOperations.IndexAdd, added, null, null, default );
            await indexer.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( 1, (await SearchAsync( catalog, "newrootsignal" )).Count );
            True( await WaitUntilAsync( async ( ) => (await SearchAsync( catalog, "aftereditsignal" )).Count == 1, TimeSpan.FromSeconds( 5 ) ) );
            Equal( 0, (await SearchAsync( catalog, "beforeeditsignal" )).Count );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

}
