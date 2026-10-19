using Microsoft.Data.Sqlite;

namespace CommandDeck;

// The only writer of `usage`, `invocation_events` and `query_history`. Catalog
// searches read `usage` for frecency; generation, preferences and reset never
// write these tables, so no transaction here spans another store's rows.
internal sealed class SqliteUsageHistoryStore(
    Func<CancellationToken, ValueTask> initialize,
    Func<CancellationToken, ValueTask<SqliteConnection>> open,
    Func<CancellationToken, ValueTask<IReadOnlyList<FilterAlias>>> filterAliases,
    Func<ContributionRegistrySnapshot> contributions,
    TimeProvider timeProvider,
    RankingOptions ranking,
    bool learnFromInvocations,
    PrivacyOptions privacy ) : IUsageHistoryStore
{
    private bool LearnsUsage => learnFromInvocations && ranking.Adaptive.Enabled && privacy.StoreUsageHistory;

    // Usage older than the adaptive retention no longer counts. Catalog searches
    // that join `usage` for frecency apply the same cutoff.
    internal static long AdaptiveCutoffUnixMilliseconds( long nowUnixMilliseconds, RankingOptions ranking ) =>
        nowUnixMilliseconds - checked(
            ( long ) ranking.Adaptive.RetentionDays * 24 * 60 * 60 * 1000);

    public async ValueTask RecordInvocationAsync(
        string resultId,
        string actionId,
        string? query,
        CancellationToken cancellationToken )
    {
        var recordUsage = LearnsUsage;
        var recordQuery = privacy.StoreQueryHistory &&
                          !string.IsNullOrWhiteSpace( query );
        if ( !recordUsage && !recordQuery )
        {
            return;
        }

        if ( resultId.StartsWith( "clipboard:", StringComparison.Ordinal ) ||
            await IsPrivateQueryAsync( query, cancellationToken ).ConfigureAwait( false ) )
        {
            return;
        }

        await initialize( cancellationToken ).ConfigureAwait( false );
        var now = timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        // Microsoft.Data.Sqlite and SQLite's busy handler both wait for locks
        // synchronously; cancellation alone cannot enforce the runtime's two-second
        // completion scope. Bound both waits to one second before BEGIN IMMEDIATE so
        // transaction acquisition and its commands leave headroom inside that scope.
        // This is local to invocation bookkeeping; the next open of this pooled
        // connection restores the configured busy timeout.
        connection.DefaultTimeout = 1;
        await using ( var busyTimeout = connection.CreateCommand( ) )
        {
            busyTimeout.CommandText = "PRAGMA busy_timeout = 1000;";
            await busyTimeout.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }

        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken ).ConfigureAwait( false );

        if ( recordUsage )
        {
            await PurgeExpiredLearningAsync( connection, transaction, now, cancellationToken )
                .ConfigureAwait( false );
            await RecordAdaptiveUsageAsync(
                connection,
                transaction,
                resultId,
                actionId,
                now,
                cancellationToken ).ConfigureAwait( false );
        }
        if ( recordQuery )
        {
            await RecordQueryHistoryAsync(
                connection,
                transaction,
                resultId,
                actionId,
                query!,
                now,
                cancellationToken ).ConfigureAwait( false );
        }

        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
    }

    // A query that names a private source, directly or through a filter alias,
    // is never recorded. One that no longer parses is treated as private.
    private async ValueTask<bool> IsPrivateQueryAsync( string? query, CancellationToken cancellationToken )
    {
        if ( string.IsNullOrWhiteSpace( query ) )
        {
            return false;
        }

        await initialize( cancellationToken ).ConfigureAwait( false );
        var aliases = await filterAliases( cancellationToken ).ConfigureAwait( false );
        var aliasMap = aliases.ToDictionary( static alias => alias.Alias, static alias => alias.FilterId, StringComparer.OrdinalIgnoreCase );
        try
        {
            return PrivateSources.ReferencesPrivateSource( QueryParser.Parse( query, aliases: aliasMap, registry: contributions( ) ) );
        }
        catch ( InvalidOperationException )
        {
            return true;
        }
    }

    private async ValueTask RecordAdaptiveUsageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string resultId,
        string actionId,
        long now,
        CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO usage(stable_id, use_count, last_used_unix_ms)
            VALUES($stable_id, 1, $now)
            ON CONFLICT(stable_id) DO UPDATE SET
                use_count = use_count + 1,
                last_used_unix_ms = excluded.last_used_unix_ms;
            INSERT INTO invocation_events(stable_id, action_id, query_text, invoked_unix_ms)
            VALUES($stable_id, $action_id, NULL, $now);
            DELETE FROM invocation_events
            WHERE id IN (
                SELECT id FROM invocation_events
                ORDER BY invoked_unix_ms DESC, id DESC
                LIMIT -1 OFFSET $maximum_events
            );
            """;
        command.Parameters.AddWithValue( "$stable_id", resultId );
        command.Parameters.AddWithValue( "$action_id", actionId );
        command.Parameters.AddWithValue( "$now", now );
        command.Parameters.AddWithValue( "$maximum_events", ranking.Adaptive.MaximumEvents );
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }

    private async ValueTask RecordQueryHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string resultId,
        string actionId,
        string query,
        long now,
        CancellationToken cancellationToken )
    {
        var cutoff = now - checked(
            ( long ) privacy.QueryHistoryRetentionDays * 24 * 60 * 60 * 1000);
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM query_history WHERE invoked_unix_ms < $cutoff;
            INSERT INTO query_history(query_text, stable_id, action_id, invoked_unix_ms)
            VALUES($query, $stable_id, $action_id, $now);
            DELETE FROM query_history
            WHERE id IN (
                SELECT id FROM query_history
                ORDER BY invoked_unix_ms DESC, id DESC
                LIMIT -1 OFFSET $maximum_entries
            );
            """;
        command.Parameters.AddWithValue( "$cutoff", cutoff );
        command.Parameters.AddWithValue( "$query", query );
        command.Parameters.AddWithValue( "$stable_id", resultId );
        command.Parameters.AddWithValue( "$action_id", actionId );
        command.Parameters.AddWithValue( "$now", now );
        command.Parameters.AddWithValue(
            "$maximum_entries",
            privacy.MaximumQueryHistoryEntries );
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<RankingLearningSnapshot> GetRankingLearningAsync(
        int limit,
        CancellationToken cancellationToken )
    {
        await initialize( cancellationToken ).ConfigureAwait( false );
        var boundedLimit = Math.Clamp( limit, 1, 256 );
        var cutoff = AdaptiveCutoffUnixMilliseconds( );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT stable_id, action_id, query_text, invoked_unix_ms,
                   (SELECT COUNT(*) FROM invocation_events WHERE invoked_unix_ms >= $cutoff)
            FROM invocation_events
            WHERE invoked_unix_ms >= $cutoff
            ORDER BY invoked_unix_ms DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue( "$cutoff", cutoff );
        command.Parameters.AddWithValue( "$limit", boundedLimit );
        List<RankingLearningEvent> events = [];
        long total = 0;
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            total = reader.GetInt64( 4 );
            events.Add( new RankingLearningEvent
            {
                StableId = reader.GetString( 0 ),
                ActionId = reader.GetString( 1 ),
                Query = reader.IsDBNull( 2 ) ? null : reader.GetString( 2 ),
                InvokedUnixMilliseconds = reader.GetInt64( 3 )
            } );
        }

        return new RankingLearningSnapshot
        {
            Enabled = LearnsUsage,
            RetentionDays = ranking.Adaptive.RetentionDays,
            MaximumEvents = ranking.Adaptive.MaximumEvents,
            TotalEvents = total,
            Truncated = total > events.Count,
            Events = events
        };
    }

    public async ValueTask<QueryHistorySnapshot> GetQueryHistoryAsync(
        int limit,
        CancellationToken cancellationToken )
    {
        await initialize( cancellationToken ).ConfigureAwait( false );
        var boundedLimit = Math.Clamp( limit, 1, 256 );
        var cutoff = timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( ) - checked(
            ( long ) privacy.QueryHistoryRetentionDays * 24 * 60 * 60 * 1000);
        await using var connection = await open( cancellationToken )
            .ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken )
                .ConfigureAwait( false );
        await using ( var purge = connection.CreateCommand( ) )
        {
            purge.Transaction = transaction;
            purge.CommandText = "DELETE FROM query_history WHERE invoked_unix_ms < $cutoff;";
            purge.Parameters.AddWithValue( "$cutoff", cutoff );
            await purge.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, query_text, stable_id, action_id, invoked_unix_ms,
                   (SELECT COUNT(*) FROM query_history)
            FROM query_history
            ORDER BY invoked_unix_ms DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue( "$limit", boundedLimit );
        List<QueryHistoryEntry> entries = [];
        long total = 0;
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            total = reader.GetInt64( 5 );
            entries.Add( new QueryHistoryEntry
            {
                Id = reader.GetInt64( 0 ),
                Query = reader.GetString( 1 ),
                StableId = reader.GetString( 2 ),
                ActionId = reader.GetString( 3 ),
                InvokedUnixMilliseconds = reader.GetInt64( 4 )
            } );
        }
        await reader.DisposeAsync( ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return new QueryHistorySnapshot
        {
            Enabled = privacy.StoreQueryHistory,
            RetentionDays = privacy.QueryHistoryRetentionDays,
            MaximumEntries = privacy.MaximumQueryHistoryEntries,
            TotalEntries = total,
            Truncated = total > entries.Count,
            Entries = entries
        };
    }

    public async ValueTask<bool> DeleteQueryHistoryAsync(
        long id,
        CancellationToken cancellationToken )
    {
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken )
            .ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "DELETE FROM query_history WHERE id = $id;";
        command.Parameters.AddWithValue( "$id", id );
        return await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false ) == 1;
    }

    public async ValueTask ResetQueryHistoryAsync( CancellationToken cancellationToken )
    {
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken )
            .ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "DELETE FROM query_history;";
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<IReadOnlyDictionary<string, UsageSignal>> GetUsageSignalsAsync(
        IReadOnlyList<string> stableIds,
        CancellationToken cancellationToken )
    {
        var ids = stableIds
            .Where( static value => !string.IsNullOrWhiteSpace( value ) )
            .Distinct( StringComparer.Ordinal )
            .Take( 500 )
            .ToArray( );
        if ( ids.Length == 0 || !LearnsUsage )
        {
            return new Dictionary<string, UsageSignal>( StringComparer.Ordinal );
        }
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken )
            .ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        var parameters = string.Join( ", ", ids.Select( ( _, index ) => $"$usage_id_{index}" ) );
        command.CommandText = $"""
            SELECT stable_id, use_count, last_used_unix_ms
            FROM usage
            WHERE stable_id IN ({parameters})
              AND last_used_unix_ms >= $cutoff;
            """;
        for ( var index = 0; index < ids.Length; index++ )
        {
            command.Parameters.AddWithValue( $"$usage_id_{index}", ids[index] );
        }
        command.Parameters.AddWithValue( "$cutoff", AdaptiveCutoffUnixMilliseconds( ) );
        Dictionary<string, UsageSignal> signals = new( StringComparer.Ordinal );
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            signals[reader.GetString( 0 )] = new UsageSignal( reader.GetInt64( 1 ), reader.GetInt64( 2 ) );
        }
        return signals;
    }

    public async ValueTask ResetRankingLearningAsync( CancellationToken cancellationToken )
    {
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken )
                .ConfigureAwait( false );
        await using ( var command = connection.CreateCommand( ) )
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM invocation_events; DELETE FROM usage;";
            await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<bool> ResetRankingForAsync( string stableId, CancellationToken cancellationToken )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( stableId );

        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var transaction =
            ( SqliteTransaction ) await connection.BeginTransactionAsync( cancellationToken )
                .ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM invocation_events WHERE stable_id = $stable_id;
            DELETE FROM usage WHERE stable_id = $stable_id;
            """;
        command.Parameters.AddWithValue( "$stable_id", stableId );
        var removed = await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return removed > 0;
    }

    private async ValueTask PurgeExpiredLearningAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long now,
        CancellationToken cancellationToken )
    {
        var cutoff = AdaptiveCutoffUnixMilliseconds( now, ranking );
        await using var purge = connection.CreateCommand( );
        purge.Transaction = transaction;
        purge.CommandText = """
            DELETE FROM invocation_events WHERE invoked_unix_ms < $cutoff;
            DELETE FROM usage WHERE last_used_unix_ms < $cutoff;
            """;
        purge.Parameters.AddWithValue( "$cutoff", cutoff );
        await purge.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }

    private long AdaptiveCutoffUnixMilliseconds( ) =>
        AdaptiveCutoffUnixMilliseconds( timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( ), ranking );
}
