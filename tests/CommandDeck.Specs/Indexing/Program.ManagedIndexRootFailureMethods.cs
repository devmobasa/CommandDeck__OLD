using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static Task IndexedFolderFailedPublicationPurgesSearchAndHomeAsync( ) => ExerciseFailedRootCommitAsync( failPurge: false );

    private static Task IndexedFolderPurgeFailureRollsBackPreferencesAsync( ) => ExerciseFailedRootCommitAsync( failPurge: true );

    private static async Task ExerciseFailedRootCommitAsync( bool failPurge, bool limitContent = false )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var parent = Directory.CreateDirectory( Path.Combine( root, "files" ) ).FullName;
            var nested = Directory.CreateDirectory( Path.Combine( parent, "nested" ) ).FullName;
            var unrelated = Directory.CreateDirectory( Path.Combine( root, "unrelated" ) ).FullName;
            var retained = Path.Combine( nested, "retainedrow.txt" );
            var revoked = Path.Combine( nested, ".revokedrow.txt" );
            await File.WriteAllTextAsync( retained, "revokedcontentsignal" );
            await File.WriteAllTextAsync( revoked, "hiddencontentsignal" );
            await File.WriteAllTextAsync( Path.Combine( unrelated, "unrelatedrow.txt" ), "unchangedcontentsignal" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), parent );
            options.Indexing.Roots =
            [
                new IndexRootOptions
                {
                    Path = parent, IncludeContentInSearch = limitContent,
                    FileTypePolicies = limitContent ? [new FileTypeIndexPolicyOptions { Pattern = "*.txt", MaximumFileBytes = 1 }] : []
                },
                new IndexRootOptions { Path = nested, IncludeHidden = true },
                new IndexRootOptions { Path = unrelated }
            ];
            await using SqliteCatalog catalog = new( options.Storage );
            await using IndexCoordinator indexer = new( catalog, catalog, options, rootPreferences: catalog.LauncherPreferences );
            await catalog.InitializeAsync( default );
            await indexer.ReindexAsync( null, default );
            var roots = await indexer.ManageRootsAsync( ProtocolOperations.IndexRoots, null, null, null, default );
            foreach ( var query in new[] { "retainedrow", "revokedrow" } )
            {
                await catalog.SetFavoriteAsync( (await SearchAsync( catalog, query )).Single( ), true, default );
            }
            True( (await catalog.GetHomeResultsAsync( LauncherPreferences.CreateDefault( ), 50, default )).Any( row => row.Id == StableId.Create( ResultKinds.File, revoked ) ) );
            await using var connection = await catalog.OpenAsync( default );
            await using var fail = connection.CreateCommand( );
            if ( failPurge )
            {
                fail.CommandText = """
                    CREATE TRIGGER fail_root_purge BEFORE DELETE ON catalog_items
                    BEGIN SELECT RAISE(ABORT, 'blocked root purge'); END;
                    """;
                await fail.ExecuteNonQueryAsync( );
                var nestedId = roots.Roots.Single( row => row.Path == nested ).Id;
                await ThrowsAsync<SqliteException>( async ( ) => await indexer.ManageRootsAsync(
                    ProtocolOperations.IndexDisable, null, nestedId, null, default ) );
                Equal( 0, (await catalog.GetIndexRootsAsync( default )).DisabledConfiguredPaths.Count );
                Equal( 1, (await SearchAsync( catalog, "hiddencontentsignal" )).Count );
                fail.CommandText = "DROP TRIGGER fail_root_purge;";
                await fail.ExecuteNonQueryAsync( );
                return;
            }
            fail.CommandText = """
                CREATE TRIGGER fail_root_publication BEFORE INSERT ON catalog_items
                BEGIN SELECT RAISE(ABORT, 'blocked root publication'); END;
                """;
            await fail.ExecuteNonQueryAsync( );

            await indexer.ManageRootsAsync( ProtocolOperations.IndexDisable, null,
                roots.Roots.Single( row => row.Path == nested ).Id, null, default );
            True( await WaitUntilAsync( async ( ) => (await indexer.ManageRootsAsync(
                ProtocolOperations.IndexRoots, null, null, null, default )).Roots.Any( row => row.Status == "Retrying indexing" ), TimeSpan.FromSeconds( 5 ) ) );

            Equal( 0, (await SearchAsync( catalog, "revokedrow" )).Count );
            Equal( 0, (await SearchAsync( catalog, "revokedcontentsignal" )).Count );
            Equal( StableId.Create( ResultKinds.File, retained ), (await SearchAsync( catalog, "retainedrow" )).Single( ).Id );
            Equal( 1, (await SearchAsync( catalog, "unchangedcontentsignal" )).Count );
            var home = await catalog.GetHomeResultsAsync( LauncherPreferences.CreateDefault( ), 50, default );
            True( !home.Any( row => row.Id == StableId.Create( ResultKinds.File, revoked ) ) );
            True( home.Any( row => row.Id == StableId.Create( ResultKinds.File, retained ) ) );
            True( await catalog.RequiresReindexAsync( default ) );

            fail.CommandText = "DROP TRIGGER fail_root_publication;";
            await fail.ExecuteNonQueryAsync( );
            await indexer.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            True( !await catalog.RequiresReindexAsync( default ) );
            Equal( StableId.Create( ResultKinds.File, retained ), (await SearchAsync( catalog, "retainedrow" )).Single( ).Id );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

}
