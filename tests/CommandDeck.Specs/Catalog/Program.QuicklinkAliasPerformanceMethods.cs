using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task QuicklinkAliasLookupWorkIsBoundedAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-alias-work-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.ConfigureQuicklinksAsync( [TestQuicklink( )], default );

            var instructions = 0;
            SqliteLauncherPreferenceStore preferences = new( catalog.InitializeAsync, async token =>
            {
                var connection = await catalog.OpenAsync( token );
                SQLitePCL.raw.sqlite3_progress_handler( connection.Handle, 1, _ => { instructions++; return 0; }, null );
                return connection;
            }, TimeProvider.System, ( ) => catalog.ContributionSnapshot, catalog.AiPreferences,
                ( ) => [], ( _, _, _ ) => ValueTask.CompletedTask );

            async Task<int> MeasureAsync( string alias, string? expectedId )
            {
                instructions = 0;
                var result = await preferences.FindAliasAsync( alias, default );
                Equal( expectedId, result?.Id );
                return instructions;
            }

            var missBefore = await MeasureAsync( "absent", null );
            var hitBefore = await MeasureAsync( "docs", "quicklink:docs" );

            await using ( var connection = await catalog.OpenAsync( default ) )
            {
                SQLitePCL.raw.sqlite3_progress_handler( connection.Handle, 0, null, null );
                await using var command = connection.CreateCommand( );
                command.CommandText = """
                    WITH RECURSIVE rows(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM rows WHERE n<100000)
                    INSERT INTO catalog_items(stable_id,kind,title,provider_id,source_id)
                    SELECT 'file:'||n,'file','Unrelated '||n,'catalog','files' FROM rows;
                    """;
                await command.ExecuteNonQueryAsync( );
            }

            var missAfter = await MeasureAsync( "absent", null );
            var hitAfter = await MeasureAsync( "docs", "quicklink:docs" );

            True( missAfter <= missBefore + 10, $"alias miss work grew from {missBefore} to {missAfter} instructions" );
            True( hitAfter <= hitBefore + 10, $"alias hit work grew from {hitBefore} to {hitAfter} instructions" );
            Console.WriteLine( $"Alias VM instructions: miss {missBefore}/{missAfter}, hit {hitBefore}/{hitAfter} (0/100000 unrelated rows)." );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            DeleteDatabase( database );
        }
    }
}
