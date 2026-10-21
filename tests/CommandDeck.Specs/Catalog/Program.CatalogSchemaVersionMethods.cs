using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task<string> ReadCatalogSchemaStateAsync( string database )
    {
        await using SqliteConnection connection = new( $"Data Source={database};Pooling=False" );
        await connection.OpenAsync( );
        await using var command = connection.CreateCommand( );
        command.CommandText = "SELECT name, sql FROM sqlite_schema ORDER BY type, name;";
        List<string> state = [];
        await using ( var reader = await command.ExecuteReaderAsync( ) )
        {
            while ( await reader.ReadAsync( ) )
            {
                state.Add( reader.GetString( 0 ) + ":" + (reader.IsDBNull( 1 ) ? "" : reader.GetString( 1 )) );
            }
        }

        command.CommandText = "PRAGMA journal_mode;";
        state.Add( ( string ) (await command.ExecuteScalarAsync( ))! );
        return string.Join( "\n", state );
    }

    private const long NewerCatalogSchemaVersion = 999;

    private static async Task CatalogRefusesNewerSchemaAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-newer-schema-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using ( SqliteCatalog current = new( new StorageOptions { DatabasePath = database } ) )
            {
                await current.InitializeAsync( CancellationToken.None );
            }

            var before = await MarkCatalogWrittenByNewerSchemaAsync( database );
            SqliteConnection.ClearAllPools( );
            await using ( SqliteConnection setup = new( $"Data Source={database};Pooling=False" ) )
            {
                await setup.OpenAsync( );
                await using var mode = setup.CreateCommand( );
                mode.CommandText = "PRAGMA journal_mode = DELETE;";
                Equal( "delete", ( string ) (await mode.ExecuteScalarAsync( ))! );
            }
            var schemaBefore = await ReadCatalogSchemaStateAsync( database );

            await using SqliteCatalog older = new( new StorageOptions { DatabasePath = database } );
            InvalidOperationException? refused = null;
            try
            {
                await older.InitializeAsync( CancellationToken.None );
            }
            catch ( InvalidOperationException exception )
            {
                refused = exception;
            }

            if ( refused is null )
            {
                throw new InvalidOperationException( "An older build opened a newer catalog schema." );
            }

            True( refused.Message.Contains( $"schema version {NewerCatalogSchemaVersion}", StringComparison.Ordinal ) &&
                refused.Message.Contains( "Update CommandDeck", StringComparison.Ordinal ),
                refused.Message );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await older.SearchAsync( QueryParser.Parse( "anything" ), 10, CancellationToken.None ) );
            Equal( before, await ReadMigrationLedgerAsync( database ) );
            Equal( schemaBefore, await ReadCatalogSchemaStateAsync( database ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task DoctorReportsNewerCatalogSchemaAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            (var config, var database) = await CreateCliCatalogAsync( root, "CLI notes" );
            var before = await MarkCatalogWrittenByNewerSchemaAsync( database );

            var doctor = await RunCommandDeckProcessAsync( "doctor", "--config", config );
            Equal( 1, doctor.ExitCode );
            True( doctor.StandardError.Contains(
                    $"schema version {NewerCatalogSchemaVersion}",
                    StringComparison.Ordinal ),
                $"doctor names the newer schema: {doctor.StandardError}" );
            Equal( before, await ReadMigrationLedgerAsync( database ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task<string> MarkCatalogWrittenByNewerSchemaAsync( string database )
    {
        SqliteConnection.ClearAllPools( );
        await using ( SqliteConnection connection = new( $"Data Source={database};Pooling=False" ) )
        {
            await connection.OpenAsync( );
            await using var newer = connection.CreateCommand( );
            newer.CommandText = """
                INSERT INTO schema_migrations(version, applied_unix_ms, description)
                VALUES($version, 0, 'Written by a newer CommandDeck');
                """;
            newer.Parameters.AddWithValue( "$version", NewerCatalogSchemaVersion );
            await newer.ExecuteNonQueryAsync( );
        }

        return await ReadMigrationLedgerAsync( database );
    }

    private static async Task<string> ReadMigrationLedgerAsync( string database )
    {
        await using SqliteConnection connection = new( $"Data Source={database};Pooling=False" );
        await connection.OpenAsync( );
        await using var ledger = connection.CreateCommand( );
        ledger.CommandText = """
            SELECT COUNT(*) || ':' || MAX(version) || ':' ||
                   (SELECT COUNT(*) FROM sqlite_master)
            FROM schema_migrations;
            """;

        return ( string ) (await ledger.ExecuteScalarAsync( ))!;
    }
}
