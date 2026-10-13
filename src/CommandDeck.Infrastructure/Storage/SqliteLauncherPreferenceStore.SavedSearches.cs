using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore
{
    public async ValueTask<IReadOnlyList<SavedSearch>> GetSavedSearchesAsync(
        CancellationToken cancellationToken )
    {
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT id, name, expression_json, shortcut, show_on_home, position
            FROM saved_searches
            ORDER BY position, name COLLATE NOCASE, id;
            """;
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        List<SavedSearch> searches = [];
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            var expression = JsonSerializer.Deserialize(
                                 reader.GetString( 2 ), CommandDeckJsonContext.Default.QueryExpression );
            if ( expression is null )
            {
                throw new InvalidOperationException( "A saved search contains no query expression." );
            }

            var unavailable = PrivateSources.ReferencesPrivateSource( expression );
            searches.Add( new SavedSearch
            {
                Id = reader.GetString( 0 ),
                Name = !unavailable ? reader.GetString( 1 )
                    : ClipboardPrivacy.ReferencesHistory( expression ) ? "Unsupported clipboard saved search" : "Unsupported HEY saved search",
                Expression = unavailable ? new QueryExpression( ) : QueryParser.ValidateShape( expression ),
                Shortcut = unavailable || reader.IsDBNull( 3 ) ? null : reader.GetString( 3 ),
                ShowOnHome = !unavailable && reader.GetInt64( 4 ) != 0,
                Position = reader.GetInt32( 5 ),
                Unavailable = unavailable
            } );
        }
        return searches;
    }

    public async ValueTask<IReadOnlyList<SavedSearch>> SaveSavedSearchAsync(
        SavedSearch search,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( search );
        var id = string.IsNullOrWhiteSpace( search.Id )
            ? $"saved-search:{Guid.NewGuid( ):N}"
            : search.Id.Trim( );
        if ( !id.StartsWith( "saved-search:", StringComparison.Ordinal ) || id.Length > 80 )
        {
            throw new InvalidOperationException( "Saved-search IDs are invalid." );
        }
        var name = SavedSearchValidator.ValidateName( search.Name );
        var expression = SavedSearchValidator.ValidateExpression( search.Expression, ContributionSnapshot );
        var shortcut = SavedSearchValidator.NormalizeShortcut( search.Shortcut );
        var expressionJson = JsonSerializer.Serialize(
            expression, CommandDeckJsonContext.Default.QueryExpression );
        var now = _timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( );

        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken )
                .ConfigureAwait( false );
        await EnsureNotClipboardShortcutAsync( connection, transaction, id, shortcut, cancellationToken ).ConfigureAwait( false );

        await using var capacity = connection.CreateCommand( );
        capacity.Transaction = transaction;
        capacity.CommandText = """
            SELECT COUNT(*), EXISTS(SELECT 1 FROM saved_searches WHERE id = $id)
            FROM saved_searches;
            """;
        capacity.Parameters.AddWithValue( "$id", id );
        await using ( var reader = await capacity.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false ) )
        {
            _ = await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );
            if ( reader.GetInt64( 0 ) >= SavedSearchValidator.MaximumSearches &&
                reader.GetInt64( 1 ) == 0 )
            {
                throw new InvalidOperationException(
                    $"Saved searches are limited to {SavedSearchValidator.MaximumSearches} entries." );
            }
        }

        var position = search.Position > 0
            ? search.Position
            : await NextSavedSearchPositionAsync( connection, transaction, cancellationToken )
                .ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO saved_searches(
                id, name, expression_json, shortcut, show_on_home, position,
                created_unix_ms, updated_unix_ms)
            VALUES($id, $name, $expression, $shortcut, $home, $position, $now, $now)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                expression_json = excluded.expression_json,
                shortcut = excluded.shortcut,
                show_on_home = excluded.show_on_home,
                position = excluded.position,
                updated_unix_ms = excluded.updated_unix_ms;
            """;
        command.Parameters.AddWithValue( "$id", id );
        command.Parameters.AddWithValue( "$name", name );
        command.Parameters.AddWithValue( "$expression", expressionJson );
        command.Parameters.AddWithValue( "$shortcut", ( object? ) shortcut ?? DBNull.Value );
        command.Parameters.AddWithValue( "$home", search.ShowOnHome ? 1 : 0 );
        command.Parameters.AddWithValue( "$position", position );
        command.Parameters.AddWithValue( "$now", now );
        try
        {
            await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        catch ( SqliteException exception ) when ( exception.SqliteErrorCode == 19 )
        {
            throw new InvalidOperationException(
                "A saved search already uses that name or shortcut.", exception );
        }
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return await GetSavedSearchesAsync( cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<IReadOnlyList<SavedSearch>> DeleteSavedSearchAsync(
        string id,
        CancellationToken cancellationToken )
    {
        if ( string.IsNullOrWhiteSpace( id ) )
        {
            throw new InvalidOperationException( "A saved-search ID is required." );
        }
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "DELETE FROM saved_searches WHERE id = $id;";
        command.Parameters.AddWithValue( "$id", id );
        if ( await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false ) == 0 )
        {
            throw new InvalidOperationException( $"Saved search '{id}' was not found." );
        }
        return await GetSavedSearchesAsync( cancellationToken ).ConfigureAwait( false );
    }

    private static async ValueTask<int> NextSavedSearchPositionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(position), 0) + 1 FROM saved_searches;";
        return Convert.ToInt32( await command.ExecuteScalarAsync( cancellationToken )
            .ConfigureAwait( false ), System.Globalization.CultureInfo.InvariantCulture );
    }
}
