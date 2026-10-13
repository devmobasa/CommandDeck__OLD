namespace CommandDeck;

public sealed partial class AgentConversationCoordinator
{
    // Owns the unrestricted lease: persists the downgrade and revokes any live
    // work the authority no longer grants. Callers inside a turn must use
    // EnforceLeaseAsync instead, because revocation waits for that turn to end.
    public async ValueTask<AgentPreferences> LoadPreferencesAsync( CancellationToken cancellationToken ) =>
        (await LoadSnapshotAsync( cancellationToken ).ConfigureAwait( false )).Preferences;

    public async ValueTask<AgentAuthoritySnapshot> LoadSnapshotAsync( CancellationToken cancellationToken )
    {
        (var snapshot, var leaseExpired) = await _authority.LoadAsync( cancellationToken ).ConfigureAwait( false );
        await RevokeStaleAsync( leaseExpired ? LeaseExpiredReason : AuthorityChangedReason, CancellationToken.None )
            .ConfigureAwait( false );
        return snapshot;
    }

    // Inside a turn: the lease downgrade is persisted and advances the epoch,
    // but revocation is left to maintenance or the next load.
    private async ValueTask<AgentPreferences> EnforceLeaseAsync( CancellationToken cancellationToken ) =>
        (await _authority.LoadAsync( cancellationToken ).ConfigureAwait( false )).Snapshot.Preferences;

    /// <summary>
    /// Ends everything admitted before the current authority epoch: the
    /// conversation and its turn first, then backend processes. Work admitted at
    /// the current epoch survives, so a revocation that runs late never reaches
    /// a newer session. Each epoch is revoked once; later calls return at once.
    /// </summary>
    public ValueTask RevokeStaleAsync( CancellationToken cancellationToken ) =>
        RevokeStaleAsync( AuthorityChangedReason, cancellationToken );

    public async ValueTask RevokeStaleAsync( string reason, CancellationToken cancellationToken )
    {
        var epoch = _authority.Snapshot.SessionEpoch;
        if ( epoch <= Volatile.Read( ref _revokedThroughEpoch ) )
        {
            return;
        }

        Conversation? stale;
        lock ( _sync )
        {
            stale = _conversation is { } conversation && conversation.AuthorityEpoch < epoch ? conversation : null;
        }
        if ( stale is not null )
        {
            // The turn first finishes with the reason; clearing then drops its
            // session, and the stop below ends a turn that ignores cancellation.
            await CancelTurnAsync( stale, reason, cancellationToken ).ConfigureAwait( false );
            await ClearAsync( stale, cancellationToken ).ConfigureAwait( false );
        }
        foreach ( var backend in _backends.All )
        {
            await backend.StopBeforeAsync( epoch, cancellationToken ).ConfigureAwait( false );
        }

        var revoked = Volatile.Read( ref _revokedThroughEpoch );
        while ( revoked < epoch )
        {
            var observed = Interlocked.CompareExchange( ref _revokedThroughEpoch, epoch, revoked );
            if ( observed == revoked )
            {
                break;
            }
            revoked = observed;
        }
    }

    // A session that lost its admission belongs to this preparer alone: dispose
    // it and stop only the process generation it was started on, which does
    // nothing once a newer start has replaced that process.
    private static async ValueTask DiscardSessionAsync( IAgentBackend backend, IAgentSession session )
    {
        var generation = session.ProcessGeneration;
        await session.DisposeAsync( ).ConfigureAwait( false );
        await backend.StopGenerationAsync( generation, CancellationToken.None ).ConfigureAwait( false );
    }
}
