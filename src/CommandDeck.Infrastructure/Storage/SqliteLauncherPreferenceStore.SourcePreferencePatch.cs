using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore
{
    public async ValueTask<LauncherPreferences> PatchSourcePreferencesAsync( SourcePreferencePatch patch, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( patch );
        if ( patch.Changes is not { Count: > 0 and <= 32 } || patch.Changes.Any( change => change is null ) ||
             patch.Changes.Select( change => change.SourceId ).Distinct( StringComparer.Ordinal ).Count( ) != patch.Changes.Count )
        {
            throw new InvalidOperationException( "Source patches require 1–32 unique source changes." );
        }
        var preferences = await GetPreferencesAsync( cancellationToken ).ConfigureAwait( false );
        var sources = preferences.Sources.ToList( );
        foreach ( var change in patch.Changes )
        {
            var index = sources.FindIndex( source => source.SourceId == change.SourceId );
            if ( index < 0 )
            {
                throw new InvalidOperationException( "The source is unknown. Refresh Settings." );
            }
            var current = sources[index];
            sources[index] = new SourcePreference
            {
                SourceId = current.SourceId,
                Position = current.Position,
                EnabledInSearch = change.EnabledInSearch ?? current.EnabledInSearch,
                EnabledOnHome = change.EnabledOnHome ?? current.EnabledOnHome
            };
            if ( change.Position is { } position )
            {
                if ( position < 1 || position > sources.Count )
                {
                    throw new InvalidOperationException( "The source position is invalid." );
                }
                var moved = sources[index];
                sources.RemoveAt( index );
                sources.Insert( position - 1, moved );
            }
        }
        EnsureHomeAvailable( sources );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction = connection.BeginTransaction( deferred: false );
        await EnsureRegistryRevisionAsync( connection, transaction, patch.RegistryRevision, cancellationToken ).ConfigureAwait( false );
        await using var revision = connection.CreateCommand( );
        revision.Transaction = transaction;
        revision.CommandText = "SELECT value FROM launcher_preferences WHERE key = 'source_preferences_revision';";
        var stored = ( string? ) await revision.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false ) ?? "0";
        if ( stored != patch.PreferenceRevision.ToString( System.Globalization.CultureInfo.InvariantCulture ) || preferences.PreferenceRevision != patch.PreferenceRevision )
        {
            throw new InvalidOperationException( "Source preferences changed. Refresh Settings and try again." );
        }
        for ( var index = 0; index < sources.Count; index++ )
        {
            await using var update = connection.CreateCommand( );
            update.Transaction = transaction;
            update.CommandText = """
                INSERT INTO source_preferences(source_id, enabled_in_search, enabled_on_home, position, updated_unix_ms)
                VALUES($id, $search, $home, $position, $now)
                ON CONFLICT(source_id) DO UPDATE SET enabled_in_search = excluded.enabled_in_search,
                    enabled_on_home = excluded.enabled_on_home, position = excluded.position, updated_unix_ms = excluded.updated_unix_ms;
                """;
            update.Parameters.AddWithValue( "$id", sources[index].SourceId );
            update.Parameters.AddWithValue( "$search", sources[index].EnabledInSearch ? 1 : 0 );
            update.Parameters.AddWithValue( "$home", sources[index].EnabledOnHome ? 1 : 0 );
            update.Parameters.AddWithValue( "$position", index + 1 );
            update.Parameters.AddWithValue( "$now", _timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( ) );
            await update.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        await IncrementSourcePreferenceRevisionAsync( connection, transaction, cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return await GetPreferencesAsync( cancellationToken ).ConfigureAwait( false );
    }

    private static async ValueTask EnsureRegistryRevisionAsync( SqliteConnection connection, SqliteTransaction transaction, long expected, CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM extension_registry WHERE id = 1;";
        var actual = ( long? ) await command.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false ) ?? 0;
        if ( actual != expected )
        {
            throw new InvalidOperationException( "The source registry changed. Refresh Settings and try again." );
        }
    }

    private static async ValueTask IncrementSourcePreferenceRevisionAsync( SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO launcher_preferences(key, value, updated_unix_ms) VALUES('source_preferences_revision', '1', 0)
            ON CONFLICT(key) DO UPDATE SET value = CAST(value AS INTEGER) + 1;
            """;
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }
}
