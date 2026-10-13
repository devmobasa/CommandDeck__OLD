using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed class SqliteAgentPreferenceStore(
    Func<CancellationToken, ValueTask> initialize,
    Func<CancellationToken, ValueTask<SqliteConnection>> open,
    TimeProvider timeProvider ) : IAgentPreferenceStore
{
    private const string AgentPreferencesKey = "agent.preferences";

    public async ValueTask<AgentPreferences> GetAgentPreferencesAsync(
        CancellationToken cancellationToken )
    {
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "SELECT value FROM ai_preferences WHERE key = $key;";
        command.Parameters.AddWithValue( "$key", AgentPreferencesKey );
        var value = await command.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false );
        if ( value is not string json || string.IsNullOrWhiteSpace( json ) )
        {
            return AgentPreferences.CreateDefault( );
        }
        try
        {
            var stored = JsonSerializer.Deserialize(
                json,
                CommandDeckJsonContext.Default.AgentPreferences );
            if ( stored is null )
            {
                return AgentPreferences.CreateDefault( );
            }
            AgentContractValidator.ValidatePreferences( stored );
            return stored;
        }
        catch ( Exception exception ) when ( exception is JsonException or InvalidOperationException )
        {
            return AgentPreferences.CreateDefault( );
        }
    }

    public async ValueTask<AgentPreferences> SetAgentPreferencesAsync(
        AgentPreferences preferences,
        CancellationToken cancellationToken )
    {
        AgentContractValidator.ValidatePreferences( preferences );
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var transaction = connection.BeginTransaction( deferred: false );
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ai_preferences(key, value, updated_unix_ms)
            VALUES($key, $value, $now)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value, updated_unix_ms = excluded.updated_unix_ms;
            """;
        command.Parameters.AddWithValue( "$key", AgentPreferencesKey );
        command.Parameters.AddWithValue(
            "$value",
            JsonSerializer.Serialize( preferences, CommandDeckJsonContext.Default.AgentPreferences ) );
        command.Parameters.AddWithValue( "$now", timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( ) );
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return preferences;
    }

    public async ValueTask<IReadOnlyList<AgentGrant>> GetAgentGrantsAsync(
        CancellationToken cancellationToken )
    {
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT id, kind, scope, identity, payload_digest, state, label,
                   created_unix_ms, updated_unix_ms, expires_unix_ms, last_validated_unix_ms
            FROM agent_grants
            ORDER BY created_unix_ms, id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue( "$limit", AgentLimits.MaximumGrants );
        List<AgentGrant> grants = [];
        await using var reader = await command.ExecuteReaderAsync( cancellationToken )
            .ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            grants.Add( new AgentGrant
            {
                Id = reader.GetString( 0 ),
                Kind = reader.GetString( 1 ),
                Scope = reader.GetString( 2 ),
                Identity = reader.IsDBNull( 3 ) ? null : reader.GetString( 3 ),
                PayloadDigest = reader.GetString( 4 ),
                State = reader.GetString( 5 ),
                Label = reader.IsDBNull( 6 ) ? null : reader.GetString( 6 ),
                CreatedUnixMilliseconds = reader.GetInt64( 7 ),
                UpdatedUnixMilliseconds = reader.GetInt64( 8 ),
                ExpiresUnixMilliseconds = reader.IsDBNull( 9 ) ? null : reader.GetInt64( 9 ),
                LastValidatedUnixMilliseconds = reader.GetInt64( 10 )
            } );
        }
        return grants;
    }

    public async ValueTask<AgentGrant> AddAgentGrantAsync(
        AgentGrant grant,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( grant );
        AiContractValidator.ValidateIdentifier( grant.Id, 96, "grantId" );
        if ( grant.Kind is not (AgentGrantKinds.Folder or AgentGrantKinds.Command) )
        {
            throw new InvalidOperationException( "The grant kind is not supported." );
        }
        AiContractValidator.ValidateUtf8( grant.Scope, 4 * 1024, "grant scope", false, allowLineBreaks: false );
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var transaction = connection.BeginTransaction( deferred: false );
        await using var count = connection.CreateCommand( );
        count.Transaction = transaction;
        count.CommandText = "SELECT COUNT(*) FROM agent_grants;";
        var existing = await count.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false );
        if ( Convert.ToInt64( existing, System.Globalization.CultureInfo.InvariantCulture ) >=
            AgentLimits.MaximumGrants )
        {
            throw new InvalidOperationException( "The grant store is full; revoke a grant first." );
        }
        var now = timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( );
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO agent_grants(
                id, kind, scope, identity, payload_digest, state, label,
                created_unix_ms, updated_unix_ms, expires_unix_ms, last_validated_unix_ms)
            VALUES($id, $kind, $scope, $identity, $digest, $state, $label,
                $now, $now, $expires, $now);
            """;
        command.Parameters.AddWithValue( "$id", grant.Id );
        command.Parameters.AddWithValue( "$kind", grant.Kind );
        command.Parameters.AddWithValue( "$scope", grant.Scope );
        command.Parameters.AddWithValue( "$identity", ( object? ) grant.Identity ?? DBNull.Value );
        command.Parameters.AddWithValue( "$digest", grant.PayloadDigest );
        command.Parameters.AddWithValue( "$state", AgentGrantStates.Active );
        command.Parameters.AddWithValue( "$label", ( object? ) grant.Label ?? DBNull.Value );
        command.Parameters.AddWithValue( "$now", now );
        command.Parameters.AddWithValue(
            "$expires",
            grant.ExpiresUnixMilliseconds.HasValue
                ? grant.ExpiresUnixMilliseconds.Value
                : DBNull.Value );
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        await transaction.CommitAsync( cancellationToken ).ConfigureAwait( false );
        return new AgentGrant
        {
            Id = grant.Id,
            Kind = grant.Kind,
            Scope = grant.Scope,
            Identity = grant.Identity,
            PayloadDigest = grant.PayloadDigest,
            State = AgentGrantStates.Active,
            Label = grant.Label,
            CreatedUnixMilliseconds = now,
            UpdatedUnixMilliseconds = now,
            ExpiresUnixMilliseconds = grant.ExpiresUnixMilliseconds,
            LastValidatedUnixMilliseconds = now
        };
    }

    public async ValueTask<bool> UpdateAgentGrantStateAsync(
        string grantId,
        string state,
        long validatedUnixMilliseconds,
        CancellationToken cancellationToken )
    {
        if ( state is not (AgentGrantStates.Active or AgentGrantStates.Stale or AgentGrantStates.Revoked) )
        {
            throw new InvalidOperationException( "The grant state is not supported." );
        }
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            UPDATE agent_grants
            SET state = $state, updated_unix_ms = $now, last_validated_unix_ms = $validated
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue( "$state", state );
        command.Parameters.AddWithValue( "$now", timeProvider.GetUtcNow( ).ToUnixTimeMilliseconds( ) );
        command.Parameters.AddWithValue( "$validated", validatedUnixMilliseconds );
        command.Parameters.AddWithValue( "$id", grantId );
        return await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false ) > 0;
    }

    public async ValueTask<bool> RemoveAgentGrantAsync( string grantId, CancellationToken cancellationToken )
    {
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "DELETE FROM agent_grants WHERE id = $id;";
        command.Parameters.AddWithValue( "$id", grantId );
        return await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false ) > 0;
    }

    public async ValueTask ClearAgentGrantsAsync( CancellationToken cancellationToken )
    {
        await initialize( cancellationToken ).ConfigureAwait( false );
        await using var connection = await open( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "DELETE FROM agent_grants;";
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
    }
}
