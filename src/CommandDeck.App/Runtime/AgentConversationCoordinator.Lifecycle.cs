using System.Threading.Channels;

namespace CommandDeck;

public sealed partial class AgentConversationCoordinator
{

    public ChannelReader<AgentStreamEvent> Subscribe( bool completeWhenIdle, out long subscriptionId )
    {
        var channel = Channel.CreateBounded<AgentStreamEvent>(
            new BoundedChannelOptions( 256 )
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            } );
        lock ( _sync )
        {
            var active = _conversation?.TurnRunning == true;
            // A turn-start stream subscribes before preparation. While idle it
            // follows the next admitted turn, including a replacement session.
            var generation = !completeWhenIdle && !active ? 0 : _conversation?.Generation ?? _generation + 1;
            subscriptionId = ++_subscriberSequence;
            if ( completeWhenIdle && !active )
            {
                channel.Writer.TryComplete( );
            }
            else
            {
                _subscribers[subscriptionId] = new Subscriber( generation, channel );
            }
        }
        return channel.Reader;
    }

    public void Unsubscribe( long subscriptionId )
    {
        if ( _subscribers.TryRemove( subscriptionId, out var subscriber ) )
        {
            subscriber.Channel.Writer.TryComplete( );
        }
    }

    public async ValueTask<AgentConversationSnapshot> ResolvePendingAsync(
        string pendingRequestId,
        string decision,
        IReadOnlyDictionary<string, string>? answers,
        CancellationToken cancellationToken )
        => await ResolvePendingAsync(
            pendingRequestId,
            decision,
            answers,
            clientOwnerId: null,
            cancellationToken ).ConfigureAwait( false );

    public async ValueTask<AgentConversationSnapshot> ResolvePendingAsync(
        string pendingRequestId,
        string decision,
        IReadOnlyDictionary<string, string>? answers,
        string? clientOwnerId,
        CancellationToken cancellationToken )
    {
        PendingRequest? pending;
        lock ( _sync )
        {
            pending = _conversation?.Pending;
            if ( pending is null || pending.Request.Id != pendingRequestId ||
                !CanResolve( pending, clientOwnerId ) )
            {
                throw new InvalidOperationException( "The pending request is unknown or already resolved." );
            }
        }
        if ( decision == AgentRequestDecisions.CancelTurn )
        {
            pending.Completion.TrySetResult( new PendingDecision( decision, answers ) );
            await CancelTurnAsync( "Cancelled by the user.", cancellationToken ).ConfigureAwait( false );
            return Snapshot( clientOwnerId );
        }
        if ( !pending.Request.Decisions.Contains( decision, StringComparer.Ordinal ) &&
            pending.Request.Kind != AgentPendingRequestKinds.Question )
        {
            throw new InvalidOperationException( "That decision is not available for this request." );
        }
        pending.Completion.TrySetResult( new PendingDecision( decision, answers ) );
        return Snapshot( clientOwnerId );
    }

    public ValueTask CancelTurnAsync( string reason, CancellationToken cancellationToken ) =>
        CancelTurnAsync( expected: null, reason, cancellationToken );

    // With an expected conversation, cancels only while that one is still current.
    private async ValueTask CancelTurnAsync( Conversation? expected, string reason, CancellationToken cancellationToken )
    {
        Conversation? conversation;
        CancellationTokenSource? cancellation;
        Task? task;
        lock ( _sync )
        {
            conversation = _conversation;
            if ( expected is not null && !ReferenceEquals( conversation, expected ) )
            {
                return;
            }
            cancellation = conversation?.TurnCancellation;
            task = conversation?.TurnTask;
            if ( conversation is not null )
            {
                conversation.CancelReason = reason;
            }
        }
        if ( conversation is null || cancellation is null || task is null || task.IsCompleted )
        {
            return;
        }
        await cancellation.CancelAsync( ).ConfigureAwait( false );
        try
        {
            await task.WaitAsync( TimeSpan.FromSeconds( 10 ), cancellationToken ).ConfigureAwait( false );
        }
        catch ( TimeoutException )
        {
        }
    }

    public ValueTask ClearAsync( CancellationToken cancellationToken ) =>
        ClearAsync( expected: null, cancellationToken );

    // With an expected conversation, clears only while that one is still current.
    private async ValueTask ClearAsync( Conversation? expected, CancellationToken cancellationToken )
    {
        Conversation? conversation;
        Task? turnTask;
        lock ( _sync )
        {
            conversation = _conversation;
            if ( expected is not null && !ReferenceEquals( conversation, expected ) )
            {
                return;
            }
            turnTask = conversation?.TurnTask;
            if ( conversation is not null )
            {
                conversation.CancelReason = "Cleared.";
                conversation.Pending?.Completion.TrySetResult(
                    new PendingDecision( AgentRequestDecisions.Deny, null ) );
            }
            _conversation = null;
            _generation++;
        }
        _outputs.Clear( );
        if ( conversation?.TurnCancellation is { } cancellation )
        {
            await cancellation.CancelAsync( ).ConfigureAwait( false );
        }
        CompleteSubscribers( conversation?.Generation ?? -1 );
        if ( turnTask is { IsCompleted: false } )
        {
            try
            {
                await turnTask.WaitAsync( TimeSpan.FromSeconds( 10 ), cancellationToken ).ConfigureAwait( false );
            }
            catch ( TimeoutException )
            {
            }
        }
        if ( conversation?.Session is not null )
        {
            await conversation.Session.DisposeAsync( ).ConfigureAwait( false );
        }
    }

    public async ValueTask ShutdownBackendAsync( CancellationToken cancellationToken )
    {
        await ClearAsync( cancellationToken ).ConfigureAwait( false );
        foreach ( var backend in _backends.All )
        {
            await backend.StopAsync( cancellationToken ).ConfigureAwait( false );
        }
    }

    public async ValueTask SweepAsync( CancellationToken cancellationToken )
    {
        // Maintenance enforces the unrestricted lease itself, so an active turn
        // cannot outlive it while no client request or new turn arrives.
        await LoadPreferencesAsync( cancellationToken ).ConfigureAwait( false );

        bool expired;
        lock ( _sync )
        {
            expired = _conversation is { } conversation && !conversation.TurnRunning &&
                conversation.ExpiresAt is { } expires && expires <= _clock.GetUtcNow( );
        }
        if ( expired )
        {
            // At the conversation TTL, drop only the transcript and its session;
            // the sandboxed process stays warm so the next prompt skips cold
            // start. A heavier ShutdownBackendAsync would kill the process too.
            await ClearAsync( cancellationToken ).ConfigureAwait( false );
        }
        bool reclaimBackend;
        lock ( _sync )
        {
            // Reclaim the warm process only when no conversation is live and it
            // has sat idle past the keep-alive window. Guarding on a null
            // conversation guarantees we never stop a process under a live turn.
            reclaimBackend = _conversation is null && _backends.All.Any( static backend => backend.IsRunning ) &&
                _clock.GetUtcNow( ) - _lastActivityUtc > TimeSpan.FromMinutes( AgentLimits.DefaultBackendKeepAliveMinutes );
        }
        if ( reclaimBackend )
        {
            // Hold the same gate PrepareConversation uses so a delayed
            // StartSession cannot be stopped after it has begun. Recheck once
            // the gate is held: an incoming session may have been assigned
            // while this sweep waited.
            await _prepareGate.WaitAsync( cancellationToken ).ConfigureAwait( false );
            try
            {
                lock ( _sync )
                {
                    reclaimBackend = _conversation is null && _backends.All.Any( static backend => backend.IsRunning ) &&
                        _clock.GetUtcNow( ) - _lastActivityUtc > TimeSpan.FromMinutes( AgentLimits.DefaultBackendKeepAliveMinutes );
                }
                if ( reclaimBackend )
                {
                    foreach ( var backend in _backends.All.Where( static backend => backend.IsRunning ) )
                    {
                        await backend.StopAsync( cancellationToken ).ConfigureAwait( false );
                    }
                }
            }
            finally
            {
                _prepareGate.Release( );
            }
        }
        lock ( _sync )
        {
            if ( _conversation?.Pending is { } pending && pending.ExpiresAt <= _clock.GetUtcNow( ) )
            {
                pending.Completion.TrySetResult( new PendingDecision( AgentRequestDecisions.Deny, null ) );
            }
        }
    }
}
