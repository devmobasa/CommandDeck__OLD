namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task QuicklinkMigrationPreservesExistingStateAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            foreach ( var withIndexedFolders in new[] { false, true } )
            {
                var database = Path.Combine( root, $"{withIndexedFolders}.db" );
                await using ( SqliteCatalog store = new( new StorageOptions { DatabasePath = database } ) )
                {
                    await store.ReplaceAllAsync( QuicklinkSentinelItems( ), null, default );
                    await store.ReplacePreferencesAsync( new LauncherPreferences { Sources = LauncherPreferences.CreateDefault( ).Sources, ReduceMotion = true }, default );
                }
                await ExecuteCatalogSqlAsync( database, """
                    DROP TABLE user_quicklinks;
                    DROP TABLE disabled_configured_quicklinks;
                    DROP INDEX ix_catalog_items_quicklink_alias;
                    DELETE FROM schema_migrations WHERE version >= 22;
                    DROP TABLE added_index_roots;
                    DROP TABLE disabled_index_roots;
                    """ );
                if ( withIndexedFolders )
                {
                    await ExecuteCatalogSqlAsync( database, """
                        INSERT INTO schema_migrations VALUES(22, 1, 'Store indexed folder preferences');
                        CREATE TABLE added_index_roots(path TEXT PRIMARY KEY, created_unix_ms INTEGER NOT NULL, options_json TEXT NOT NULL) WITHOUT ROWID;
                        CREATE TABLE disabled_index_roots(path TEXT PRIMARY KEY) WITHOUT ROWID;
                        INSERT INTO added_index_roots VALUES('/kept', 1234, '{"path":"/kept","includeHidden":true}');
                        INSERT INTO disabled_index_roots VALUES('/disabled');
                        """ );
                }

                await using SqliteCatalog upgraded = new( new StorageOptions { DatabasePath = database } );
                await upgraded.ConfigureQuicklinksAsync( [TestQuicklink( )], default );
                True( (await upgraded.GetPreferencesAsync( default )).ReduceMotion );
                True( (await upgraded.SearchAsync( QueryParser.Parse( "app: Unrelated" ), 20, default )).Count > 0 );
                Equal( 1, (await upgraded.GetQuicklinksAsync( default )).Quicklinks.Count );
                True( !await upgraded.RequiresReindexAsync( default ) );
                if ( !withIndexedFolders )
                {
                    Equal( 0, (await upgraded.GetIndexRootsAsync( default )).Added.Count );
                }
                if ( withIndexedFolders )
                {
                    var folders = await upgraded.GetIndexRootsAsync( default );
                    Equal( "/kept", folders.Added.Single( ).Options.Path );
                    True( folders.Added.Single( ).Options.IncludeHidden );
                    Equal( 1234L, folders.Added.Single( ).CreatedAt.ToUnixTimeMilliseconds( ) );
                    Equal( "/disabled", folders.DisabledConfiguredPaths.Single( ) );
                }
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
