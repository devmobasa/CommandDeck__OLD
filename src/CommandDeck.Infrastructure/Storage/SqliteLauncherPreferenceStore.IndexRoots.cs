using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore
{
    public async ValueTask<IndexRootPreferences> GetIndexRootsAsync( CancellationToken cancellationToken )
    {
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "SELECT options_json, created_unix_ms FROM added_index_roots ORDER BY path;";
        IndexRootPreferences result = new( );
        await using ( var reader = await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
            {
                var options = JsonSerializer.Deserialize( reader.GetString( 0 ), CommandDeckJsonContext.Default.IndexRootOptions );
                if ( options is null )
                {
                    throw new InvalidOperationException( "Stored index root options are missing." );
                }

                result.Added.Add( new AddedIndexRoot
                {
                    Options = options,
                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds( reader.GetInt64( 1 ) )
                } );
            }
        }

        command.CommandText = "SELECT path FROM disabled_index_roots ORDER BY path;";
        await using var disabled = await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        while ( await disabled.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            result.DisabledConfiguredPaths.Add( disabled.GetString( 0 ) );
        }
        return result;
    }

    public async ValueTask ReplaceIndexRootsAsync( IndexRootPreferences preferences, CancellationToken cancellationToken )
    {
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction = ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken ).ConfigureAwait( false );
        await WriteIndexRootsAsync( connection, transaction, preferences, cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
    }

    private static async ValueTask WriteIndexRootsAsync( SqliteConnection connection, SqliteTransaction transaction,
        IndexRootPreferences preferences, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( preferences );
        preferences.Validate( );
        var disabled = preferences.DisabledConfiguredPaths.Select( IndexRootPaths.Normalize ).Distinct( StringComparer.Ordinal ).ToArray( );

        await SqliteCatalog.ExecuteNonQueryAsync( connection, transaction,
            "DELETE FROM added_index_roots; DELETE FROM disabled_index_roots;", cancellationToken ).ConfigureAwait( false );
        foreach ( var root in preferences.Added.DistinctBy( static root => IndexRootPaths.Normalize( root.Options.Path ) ) )
        {
            await using var command = connection.CreateCommand( );
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO added_index_roots(path, created_unix_ms, options_json) VALUES ($path, $created, $options);";
            command.Parameters.AddWithValue( "$path", IndexRootPaths.Normalize( root.Options.Path ) );
            command.Parameters.AddWithValue( "$created", root.CreatedAt.ToUnixTimeMilliseconds( ) );
            command.Parameters.AddWithValue( "$options", JsonSerializer.Serialize( root.Options with { Path = IndexRootPaths.Normalize( root.Options.Path ) }, CommandDeckJsonContext.Default.IndexRootOptions ) );
            await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        foreach ( var path in disabled )
        {
            await using var command = connection.CreateCommand( );
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO disabled_index_roots(path) VALUES ($path);";
            command.Parameters.AddWithValue( "$path", path );
            await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

    }
}
