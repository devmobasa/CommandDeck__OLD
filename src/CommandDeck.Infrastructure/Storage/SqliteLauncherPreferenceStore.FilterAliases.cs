using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore
{
    public async ValueTask<IReadOnlyList<FilterAlias>> GetFilterAliasesAsync(
        CancellationToken cancellationToken )
    {
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT alias, filter_id
            FROM filter_aliases
            ORDER BY alias COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        List<FilterAlias> aliases = [];
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            aliases.Add( new FilterAlias
            {
                Alias = reader.GetString( 0 ),
                FilterId = reader.GetString( 1 )
            } );
        }
        return aliases;
    }

    public async ValueTask<IReadOnlyList<FilterAlias>> SetFilterAliasAsync(
        string aliasToken,
        string? filterId,
        CancellationToken cancellationToken )
    {
        var normalizedAlias = FilterAliasValidator.NormalizeAndValidate( aliasToken );
        if ( filterId is not null && ContributionSnapshot.FindByToken( normalizedAlias ) is not null )
        {
            throw new InvalidOperationException( "The filter alias conflicts with a registered source token." );
        }
        string? normalizedFilter = null;
        if ( filterId is not null )
        {
            normalizedFilter = ContributionSnapshot.FindById( filterId )?.Id;
            if ( normalizedFilter is null )
            {
                throw new InvalidOperationException( "The filter is unknown." );
            }
        }

        var now = _timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( );
        await using var connection = await OpenAsync( cancellationToken )
            .ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken )
                .ConfigureAwait( false );

        if ( normalizedFilter is null )
        {
            await using var delete = connection.CreateCommand( );
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM filter_aliases WHERE alias = $alias COLLATE NOCASE;";
            delete.Parameters.AddWithValue( "$alias", normalizedAlias );
            if ( await delete.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false ) == 0 )
            {
                throw new InvalidOperationException(
                    $"Filter alias '{normalizedAlias}' was not found." );
            }
        }
        else
        {
            await EnsureFilterAliasCapacityAsync(
                connection,
                transaction,
                normalizedAlias,
                cancellationToken ).ConfigureAwait( false );
            await using var existing = connection.CreateCommand( );
            existing.Transaction = transaction;
            existing.CommandText =
                "SELECT filter_id FROM filter_aliases WHERE alias = $alias COLLATE NOCASE;";
            existing.Parameters.AddWithValue( "$alias", normalizedAlias );
            var assigned = ( string? ) await existing.ExecuteScalarAsync( cancellationToken )
                .ConfigureAwait( false );
            if ( assigned is not null && !string.Equals(
                assigned,
                normalizedFilter,
                StringComparison.OrdinalIgnoreCase ) )
            {
                throw new InvalidOperationException(
                    $"Filter alias '{normalizedAlias}' is already assigned to '{assigned}'." );
            }

            await using var upsert = connection.CreateCommand( );
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO filter_aliases(alias, filter_id, created_unix_ms, updated_unix_ms)
                VALUES($alias, $filter, $now, $now)
                ON CONFLICT(alias) DO UPDATE SET updated_unix_ms = excluded.updated_unix_ms;
                """;
            upsert.Parameters.AddWithValue( "$alias", normalizedAlias );
            upsert.Parameters.AddWithValue( "$filter", normalizedFilter );
            upsert.Parameters.AddWithValue( "$now", now );
            await upsert.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return await GetFilterAliasesAsync( cancellationToken ).ConfigureAwait( false );
    }

    private static async ValueTask EnsureFilterAliasCapacityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string alias,
        CancellationToken cancellationToken )
    {
        await using var count = connection.CreateCommand( );
        count.Transaction = transaction;
        count.CommandText = """
            SELECT
                COUNT(*),
                EXISTS(SELECT 1 FROM filter_aliases WHERE alias = $alias COLLATE NOCASE)
            FROM filter_aliases;
            """;
        count.Parameters.AddWithValue( "$alias", alias );
        await using var reader = await count.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        _ = await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );
        if ( reader.GetInt64( 0 ) >= FilterAliasValidator.MaximumAliases && reader.GetInt64( 1 ) == 0 )
        {
            throw new InvalidOperationException(
                $"Filter aliases are limited to {FilterAliasValidator.MaximumAliases} entries." );
        }
    }
}
