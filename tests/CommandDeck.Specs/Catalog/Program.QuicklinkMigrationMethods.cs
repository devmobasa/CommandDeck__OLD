namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task QuicklinkMigrationPreservesExistingStateAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            foreach ( var withIndependentMigration in new[] { false, true } )
            {
                var database = Path.Combine( root, $"{withIndependentMigration}.db" );
                await using ( SqliteCatalog store = new( new StorageOptions { DatabasePath = database } ) )
                {
                    await store.ReplaceAllAsync( QuicklinkSentinelItems( ), null, default );
                    await store.ReplacePreferencesAsync( new LauncherPreferences { Sources = LauncherPreferences.CreateDefault( ).Sources, ReduceMotion = true }, default );
                }
                await ExecuteCatalogSqlAsync( database, """
                    DROP TABLE user_quicklinks;
                    DROP TABLE disabled_configured_quicklinks;
                    DROP INDEX ix_catalog_items_quicklink_alias;
                    DELETE FROM schema_migrations WHERE version=23;
                    """ );
                if ( withIndependentMigration )
                {
                    await ExecuteCatalogSqlAsync( database, """
                        INSERT INTO schema_migrations VALUES(22, 1, 'Independent preferences');
                        CREATE TABLE independent_preferences(id TEXT PRIMARY KEY);
                        INSERT INTO independent_preferences VALUES('keep');
                        """ );
                }

                await using SqliteCatalog upgraded = new( new StorageOptions { DatabasePath = database } );
                await upgraded.ConfigureQuicklinksAsync( [TestQuicklink( )], default );
                True( (await upgraded.GetPreferencesAsync( default )).ReduceMotion );
                True( (await upgraded.SearchAsync( QueryParser.Parse( "app: Unrelated" ), 20, default )).Count > 0 );
                Equal( 1, (await upgraded.GetQuicklinksAsync( default )).Quicklinks.Count );
                True( !await upgraded.RequiresReindexAsync( default ) );
                if ( withIndependentMigration )
                {
                    await using var connection = await upgraded.OpenAsync( default );
                    await using var command = connection.CreateCommand( );
                    command.CommandText = "SELECT id FROM independent_preferences;";
                    Equal( "keep", ( string? ) await command.ExecuteScalarAsync( ) );
                }
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
