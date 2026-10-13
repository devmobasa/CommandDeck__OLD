namespace CommandDeck;

public sealed record ExtensionStoredDataRow( string InstallationKey, long Revision, string ValuesJson );

public sealed partial class SqliteCatalog
{
    public async ValueTask<ExtensionStoredDataRow[]> ReadExtensionStorageAsync( CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "SELECT installation_key, revision, values_json FROM extension_storage;";
        await using var reader = await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        List<ExtensionStoredDataRow> rows = [];
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            rows.Add( new( reader.GetString( 0 ), reader.GetInt64( 1 ), reader.GetString( 2 ) ) );
        }
        return rows.ToArray( );
    }

    public async ValueTask CommitExtensionStorageAsync( ExtensionStoredDataRow row, long expectedRevision, CancellationToken cancellationToken )
    {
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using ( var durability = connection.CreateCommand( ) )
        {
            durability.CommandText = "PRAGMA synchronous = FULL;";
            await durability.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        await using var transaction = connection.BeginTransaction( deferred: false );
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO extension_storage(installation_key, revision, values_json)
                SELECT $key, $revision, $values WHERE $expected = 0
            ON CONFLICT(installation_key) DO UPDATE SET revision = $revision, values_json = $values
                WHERE extension_storage.revision = $expected;
            """;
        // An UPDATE is also needed for a nonzero expected revision: the INSERT's
        // SELECT deliberately produces no row in that case.
        if ( expectedRevision != 0 )
        {
            command.CommandText = "UPDATE extension_storage SET revision = $revision, values_json = $values WHERE installation_key = $key AND revision = $expected;";
        }
        command.Parameters.AddWithValue( "$key", row.InstallationKey );
        command.Parameters.AddWithValue( "$revision", row.Revision );
        command.Parameters.AddWithValue( "$values", row.ValuesJson );
        command.Parameters.AddWithValue( "$expected", expectedRevision );
        if ( await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false ) != 1 )
        {
            throw new InvalidOperationException( "The stored data changed. Reload it before saving." );
        }
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
    }
}
