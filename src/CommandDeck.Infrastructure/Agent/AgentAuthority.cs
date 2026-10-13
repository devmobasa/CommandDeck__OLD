namespace CommandDeck;

/// <summary>A preference record together with the session epoch it was read or written at.</summary>
public sealed record AgentAuthoritySnapshot( AgentPreferences Preferences, long SessionEpoch );

/// <summary>The snapshot after a load, and whether that load ended an expired lease.</summary>
public readonly record struct AgentAuthorityLoad( AgentAuthoritySnapshot Snapshot, bool LeaseExpired );

/// <summary>
/// The single owner of the assistant's authority state: the preference record,
/// including trust level and lease, and the grant set. One gate serializes every
/// read-modify-write, so no writer can persist a value that another writer has
/// since replaced. Nothing here calls back out: callers cancel turns or shut the
/// backend down only after a call returns, so a revocation never waits on this gate.
/// </summary>
/// <remarks>
/// The session epoch advances, inside the gate, on exactly the changes that end
/// live sessions. Work admitted under an older epoch is revoked by the caller
/// once the write returns; work that checks its epoch afterwards is refused.
/// </remarks>
public sealed class AgentAuthority : IAgentAuthorityEpoch, IDisposable
{
    public const string SavingSummary = "Assistant permissions changed while saving; review the settings and save again.";

    private readonly IAgentPreferenceStore _store;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new( 1, 1 );
    private AgentAuthoritySnapshot _snapshot = new( AgentPreferences.CreateDefault( ), SessionEpoch: 1 );

    public AgentAuthority( IAgentPreferenceStore store, TimeProvider? clock = null )
    {
        ArgumentNullException.ThrowIfNull( store );
        _store = store;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The record and epoch this owner last read or wrote; it is never assigned elsewhere.</summary>
    public AgentAuthoritySnapshot Snapshot => Volatile.Read( ref _snapshot );

    public AgentPreferences Current => Snapshot.Preferences;

    public bool IsCurrent( long epoch ) => Snapshot.SessionEpoch == epoch;

    /// <summary>Reads the record and persists the downgrade of an expired unrestricted lease.</summary>
    public async ValueTask<AgentAuthorityLoad> LoadAsync( CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            var preferences = await _store.GetAgentPreferencesAsync( cancellationToken ).ConfigureAwait( false );
            var expired = preferences.TrustLevel == AgentTrustLevels.Unrestricted &&
                preferences.TrustLeaseExpiresUnixMilliseconds is { } expires &&
                expires <= _clock.GetUtcNow( ).ToUnixTimeMilliseconds( );
            if ( expired )
            {
                var downgraded = await WriteUnderGateAsync(
                    preferences, preferences.WithTrust( AgentTrustLevels.Default, null, null ), cancellationToken ).ConfigureAwait( false );
                return new AgentAuthorityLoad( downgraded, LeaseExpired: true );
            }
            var previous = Snapshot;
            // Another runtime can write the shared store. Once this owner
            // observes changed permissions, its old admissions expire too.
            AgentAuthoritySnapshot loaded = new( preferences,
                EndsSessions( previous.Preferences, preferences ) ? previous.SessionEpoch + 1 : previous.SessionEpoch );
            Volatile.Write( ref _snapshot, loaded );
            return new AgentAuthorityLoad( loaded, LeaseExpired: false );
        }
        finally
        {
            _gate.Release( );
        }
    }

    /// <summary>
    /// Writes ordinary settings. Trust level, duration, and lease always come from
    /// the record read inside the gate, so a settings save never writes authority.
    /// A save validated against an epoch that has since changed writes nothing.
    /// </summary>
    public ValueTask<AgentAuthoritySnapshot> UpdateSettingsAsync(
        AgentPreferences settings,
        long expectedEpoch,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( settings );
        return WriteAsync(
            latest => settings.WithTrust( latest.TrustLevel, latest.TrustDuration, latest.TrustLeaseExpiresUnixMilliseconds ),
            clearGrants: false,
            expectedEpoch,
            cancellationToken );
    }

    /// <summary>Disables the assistant: clears every grant and all authority in one step.</summary>
    public ValueTask<AgentAuthoritySnapshot> DisableAsync( AgentPreferences settings, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( settings );
        if ( settings.Enabled )
        {
            throw new ArgumentException( "Disable requires a disabled preference record.", nameof( settings ) );
        }
        return WriteAsync( _ => settings.WithAuthorityCleared( ), clearGrants: true, expectedEpoch: null, cancellationToken );
    }

    /// <summary>Resets the assistant to its defaults: disabled, no trust beyond the default, and no grants.</summary>
    public ValueTask<AgentAuthoritySnapshot> ResetAsync( CancellationToken cancellationToken ) =>
        WriteAsync( _ => AgentPreferences.CreateDefault( ), clearGrants: true, expectedEpoch: null, cancellationToken );

    /// <summary>Applies an accepted trust change to the current record.</summary>
    public ValueTask<AgentAuthoritySnapshot> SetTrustAsync(
        int level,
        string? duration,
        long? leaseExpiresUnixMilliseconds,
        CancellationToken cancellationToken ) =>
        WriteAsync(
            latest => latest.WithTrust( level, duration, leaseExpiresUnixMilliseconds ),
            clearGrants: false,
            expectedEpoch: null,
            cancellationToken );

    public ValueTask<IReadOnlyList<AgentGrant>> GetGrantsAsync( CancellationToken cancellationToken ) =>
        _store.GetAgentGrantsAsync( cancellationToken );

    public async ValueTask<AgentGrant> AddGrantAsync( AgentGrant grant, CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            return await _store.AddAgentGrantAsync( grant, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask<bool> MarkGrantStateAsync(
        string grantId,
        string state,
        long validatedUnixMilliseconds,
        CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            return await _store.UpdateAgentGrantStateAsync( grantId, state, validatedUnixMilliseconds, cancellationToken )
                .ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask<bool> RemoveGrantAsync( string grantId, CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            return await _store.RemoveAgentGrantAsync( grantId, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask ClearGrantsAsync( CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            await _store.ClearAgentGrantsAsync( cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
        }
    }

    public void Dispose( ) => _gate.Dispose( );

    // The changes that end live sessions; before the epoch existed, each one
    // shut the backend down.
    private static bool EndsSessions( AgentPreferences before, AgentPreferences after ) =>
        before.TrustLevel != after.TrustLevel ||
        before.Enabled && !after.Enabled ||
        before.WebSearchEnabled != after.WebSearchEnabled ||
        before.UseCodexConfiguration != after.UseCodexConfiguration;

    private async ValueTask<AgentAuthoritySnapshot> WriteAsync(
        Func<AgentPreferences, AgentPreferences> change,
        bool clearGrants,
        long? expectedEpoch,
        CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( expectedEpoch is { } expected && Snapshot.SessionEpoch != expected )
            {
                throw new AiOperationException( AiErrorCategories.Conflict, safeSummary: SavingSummary );
            }
            var latest = await _store.GetAgentPreferencesAsync( cancellationToken ).ConfigureAwait( false );
            if ( clearGrants )
            {
                await _store.ClearAgentGrantsAsync( cancellationToken ).ConfigureAwait( false );
            }
            return await WriteUnderGateAsync( latest, change( latest ), cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
        }
    }

    private async ValueTask<AgentAuthoritySnapshot> WriteUnderGateAsync(
        AgentPreferences before,
        AgentPreferences next,
        CancellationToken cancellationToken )
    {
        var saved = await _store.SetAgentPreferencesAsync( next, cancellationToken ).ConfigureAwait( false );
        var epoch = Snapshot.SessionEpoch;
        AgentAuthoritySnapshot written = new( saved,
            EndsSessions( before, saved ) || EndsSessions( Snapshot.Preferences, saved ) ? epoch + 1 : epoch );
        Volatile.Write( ref _snapshot, written );
        return written;
    }
}
