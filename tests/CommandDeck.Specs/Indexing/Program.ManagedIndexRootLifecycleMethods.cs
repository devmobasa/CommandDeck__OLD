using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task IndexedFolderNonOwnerCannotResurrectRemovedRowsAsync( )
    {
        await ExerciseNonOwnerReindexAsync( incremental: true );
        await ExerciseNonOwnerReindexAsync( incremental: false );
    }

    private static async Task ExerciseNonOwnerReindexAsync( bool incremental )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var retained = Directory.CreateDirectory( Path.Combine( root, "retained" ) ).FullName;
            var added = Directory.CreateDirectory( Path.Combine( root, "added" ) ).FullName;
            await File.WriteAllTextAsync( Path.Combine( added, "revokedcontent.txt" ), "revokedcontentsignal" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), retained );
            options = new CommandDeckOptions
            {
                Storage = options.Storage,
                Privacy = options.Privacy,
                Indexing = options.Indexing with { EnableIncrementalIndexing = incremental }
            };
            var config = Path.Combine( root, "config.json" );
            await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using var owner = await CommandDeckRuntime.CreateAsync( config, default );
            await owner.Indexer.ReindexAsync( null, default );
            await using SqliteCatalog persisted = new( options.Storage );
            await using var connection = await persisted.OpenAsync( default );
            await using var command = connection.CreateCommand( );
            command.CommandText = "CREATE TRIGGER block_root_scan BEFORE INSERT ON catalog_items BEGIN SELECT RAISE(ABORT, 'scan paused'); END;";
            await command.ExecuteNonQueryAsync( );
            var listing = await owner.Indexer.ManageRootsAsync( ProtocolOperations.IndexAdd, added, null, null, default );
            True( await WaitUntilAsync( async ( ) => (await owner.Indexer.ManageRootsAsync(
                ProtocolOperations.IndexRoots, null, null, null, default )).Roots.Any( row => row.Status == "Retrying indexing" ), TimeSpan.FromSeconds( 5 ) ) );

            await using var reader = await CommandDeckRuntime.CreateAsync( config, default, includeClipboardHistory: false );
            var readOnly = ( IndexCoordinator ) reader.Indexer;
            await reader.Indexer.ManageRootsAsync( ProtocolOperations.IndexRoots, null, null, null, default );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await readOnly.ManageRootsAsync(
                ProtocolOperations.IndexDisable, null, StableId.Create( "index-root", retained ), null, default ) );
            Equal( 0, (await persisted.GetIndexRootsAsync( default )).DisabledConfiguredPaths.Count );
            await owner.Indexer.ManageRootsAsync( ProtocolOperations.IndexRemove, null,
                listing.Roots.Single( row => row.Path == added ).Id, null, default );
            command.CommandText = "DROP TRIGGER block_root_scan;";
            await command.ExecuteNonQueryAsync( );
            await (( IndexCoordinator ) owner.Indexer).BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            await readOnly.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( 0, (await SearchAsync( persisted, "revokedcontentsignal" )).Count );
            True( !await persisted.RequiresReindexAsync( default ) );
            True( !readOnly.SnapshotWatchedDirectories( ).Contains( added ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task IndexedFolderResumedWorkReconcilesOfflinePolicyAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var retained = Directory.CreateDirectory( Path.Combine( root, "retained" ) ).FullName;
            var added = Directory.CreateDirectory( Path.Combine( root, "added" ) ).FullName;
            Directory.CreateDirectory( Path.Combine( retained, ".git" ) );
            var ignore = Path.Combine( retained, ".gitignore" );
            await File.WriteAllTextAsync( ignore, "" );
            var revoked = Path.Combine( retained, "excludedlater.txt" );
            await File.WriteAllTextAsync( revoked, "offlineexclusionsignal" );
            await File.WriteAllTextAsync( Path.Combine( added, "new.txt" ), "resumedfoldersignal" );
            var database = Path.Combine( root, "catalog.db" );
            CommandDeckOptions Options( )
            {
                var configured = CreateIncrementalIndexOptions( database, retained );
                return new CommandDeckOptions
                {
                    Storage = configured.Storage,
                    Privacy = configured.Privacy,
                    Indexing = configured.Indexing with { EnableIncrementalIndexing = false }
                };
            }

            await using SqliteCatalog catalog = new( Options( ).Storage );
            await catalog.InitializeAsync( default );
            await using var connection = await catalog.OpenAsync( default );
            await using var command = connection.CreateCommand( );
            await using ( IndexCoordinator initial = new( catalog, catalog, Options( ), rootPreferences: catalog.LauncherPreferences ) )
            {
                await initial.ReindexAsync( null, default );
                await catalog.SetFavoriteAsync( (await SearchAsync( catalog, "offlineexclusionsignal" )).Single( ), true, default );
                command.CommandText = "CREATE TRIGGER block_root_scan BEFORE INSERT ON catalog_items BEGIN SELECT RAISE(ABORT, 'scan paused'); END;";
                await command.ExecuteNonQueryAsync( );
                await initial.ManageRootsAsync( ProtocolOperations.IndexAdd, added, null, null, default );
                True( await WaitUntilAsync( async ( ) => (await initial.ManageRootsAsync(
                    ProtocolOperations.IndexRoots, null, null, null, default )).Roots.Any( row => row.Status == "Retrying indexing" ), TimeSpan.FromSeconds( 5 ) ) );
                initial.CancelBackgroundWork( );
            }
            True( await catalog.RequiresReindexAsync( default ) );
            await File.WriteAllTextAsync( ignore, "excludedlater.txt\n" );
            command.CommandText = "DROP TRIGGER block_root_scan;";
            await command.ExecuteNonQueryAsync( );

            for ( var restart = 0; restart < 2; restart++ )
            {
                await using IndexCoordinator restarted = new( catalog, catalog, Options( ), rootPreferences: catalog.LauncherPreferences );
                await restarted.StartAsync( default );
                await restarted.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                await restarted.PolicyReconciliation.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                Equal( 0, (await SearchAsync( catalog, "offlineexclusionsignal" )).Count );
                Equal( 1, (await SearchAsync( catalog, "resumedfoldersignal" )).Count );
                True( !(await catalog.GetHomeResultsAsync( LauncherPreferences.CreateDefault( ), 50, default )).Any( row =>
                    row.Id == StableId.Create( ResultKinds.File, revoked ) ) );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

}
