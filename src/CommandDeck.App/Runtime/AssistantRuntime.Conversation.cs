using System.Threading.Channels;

namespace CommandDeck;

internal sealed partial class AssistantRuntime
{

    public AgentConversationSnapshot GetAgentConversation( ) => Agent.Snapshot( );

    public AgentConversationSnapshot GetAgentConversation( string clientOwnerId ) =>
        Agent.Snapshot( clientOwnerId );

    public ChannelReader<AgentStreamEvent> SubscribeAgentEvents( bool completeWhenIdle, out long subscriptionId ) =>
        Agent.Subscribe( completeWhenIdle, out subscriptionId );

    public void UnsubscribeAgentEvents( long subscriptionId ) => Agent.Unsubscribe( subscriptionId );

    public async ValueTask<AgentConversationSnapshot> StartAgentTurnAsync(
        string text,
        string? backendId,
        string? modelId,
        string? reasoningEffort,
        CancellationToken cancellationToken )
    {
        var snapshot = await Agent.StartTurnAsync( text, backendId, modelId, reasoningEffort, cancellationToken )
            .ConfigureAwait( false );
        return snapshot;
    }

    public ValueTask<AgentConversationSnapshot> StartAgentTurnAsync(
        string text,
        string? backendId,
        string? modelId,
        string? reasoningEffort,
        string clientOwnerId,
        CancellationToken cancellationToken ) =>
        Agent.StartTurnAsync(
            text, backendId, modelId, reasoningEffort, clientOwnerId, cancellationToken );

    public ValueTask<AgentConversationSnapshot> ResolveAgentPendingAsync(
        string pendingRequestId,
        string decision,
        IReadOnlyDictionary<string, string>? answers,
        CancellationToken cancellationToken ) =>
        Agent.ResolvePendingAsync( pendingRequestId, decision, answers, cancellationToken );

    public ValueTask<AgentConversationSnapshot> ResolveAgentPendingAsync(
        string pendingRequestId,
        string decision,
        IReadOnlyDictionary<string, string>? answers,
        string clientOwnerId,
        CancellationToken cancellationToken ) =>
        Agent.ResolvePendingAsync(
            pendingRequestId, decision, answers, clientOwnerId, cancellationToken );

    public ValueTask PrewarmAgentAsync( CancellationToken cancellationToken ) => Agent.PrewarmAsync( cancellationToken );

    public ValueTask CancelAgentTurnAsync( CancellationToken cancellationToken ) =>
        Agent.CancelTurnAsync( "Cancelled by the user.", cancellationToken );

    public ValueTask ClearAgentAsync( CancellationToken cancellationToken ) => Agent.ClearAsync( cancellationToken );

    public async ValueTask<AgentTrustState> GetAgentTrustAsync( CancellationToken cancellationToken ) =>
        AgentTrustService.Describe( await RefreshAgentPreferenceCacheAsync( cancellationToken ).ConfigureAwait( false ) );

    public async ValueTask<AgentTrustChallenge> CreateAgentTrustChallengeAsync(
        string clientOwnerId,
        int targetLevel,
        CancellationToken cancellationToken )
    {
        var preferences = await RefreshAgentPreferenceCacheAsync( cancellationToken ).ConfigureAwait( false );
        if ( !preferences.Enabled )
        {
            throw new InvalidOperationException( "Enable the assistant before changing trust." );
        }
        if ( targetLevel == AgentTrustLevels.Unrestricted && !File.Exists( _agentOptions.SystemdRunPath ) )
        {
            throw new InvalidOperationException( "Unrestricted mode requires systemd-run for the transient user unit." );
        }
        return _trust!.CreateChallenge( clientOwnerId, targetLevel, preferences );
    }

    public async ValueTask<AgentTrustState> ApplyAgentTrustAsync(
        string clientOwnerId,
        string challengeId,
        int level,
        string? phrase,
        string? duration,
        CancellationToken cancellationToken )
    {
        var saved = await _trust!.ApplyAsync( clientOwnerId, challengeId, level, phrase, duration, cancellationToken )
            .ConfigureAwait( false );
        // A level change advanced the epoch; end what was admitted before it.
        await Agent.RevokeStaleAsync( CancellationToken.None ).ConfigureAwait( false );
        return AgentTrustService.Describe( saved );
    }

    public async ValueTask<AgentGrantResult> GetAgentGrantsAsync( CancellationToken cancellationToken ) =>
        new( )
        {
            Grants = [.. await _authority!.GetGrantsAsync( cancellationToken ).ConfigureAwait( false )]
        };

    public async ValueTask<AgentGrantResult> AddAgentFolderGrantAsync( string kind, string scope, CancellationToken cancellationToken )
    {
        if ( kind != AgentGrantKinds.Folder )
        {
            throw new InvalidOperationException( "Only folder grants can be added directly." );
        }
        var canonical = AgentContextBroker.Canonicalize( scope, _contextBroker!.HomeDirectory );
        await _contextBroker.GrantFolderAsync( canonical, cancellationToken ).ConfigureAwait( false );
        return await GetAgentGrantsAsync( cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<AgentGrantResult> RemoveAgentGrantAsync( string grantId, CancellationToken cancellationToken )
    {
        await _authority!.RemoveGrantAsync( grantId, cancellationToken ).ConfigureAwait( false );
        return await GetAgentGrantsAsync( cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<AgentGrantResult> ClearAgentGrantsAsync( CancellationToken cancellationToken )
    {
        await _authority!.ClearGrantsAsync( cancellationToken ).ConfigureAwait( false );
        return await GetAgentGrantsAsync( cancellationToken ).ConfigureAwait( false );
    }

    public AgentOperationsSnapshot GetAgentOperations( ) => new( ) { Operations = [.. Agent.Operations( )] };

    public ValueTask<ActionExecutionResult> ExecuteAgentOutputAsync(
        long generation,
        string action,
        string? targetPath,
        string? confirmationToken,
        CancellationToken cancellationToken ) => _aiOutputs.ExecuteAsync(
            AgentConversationCoordinator.OutputOwnerId,
            generation,
            action,
            targetPath,
            confirmationToken,
            cancellationToken );
}
