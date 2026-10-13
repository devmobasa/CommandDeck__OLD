using System.Threading.Channels;

namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{
    public AgentConversationSnapshot GetAgentConversation( ) =>
        Assistant.GetAgentConversation( );

    public AgentConversationSnapshot GetAgentConversation( string clientOwnerId ) =>
        Assistant.GetAgentConversation( clientOwnerId );

    public ChannelReader<AgentStreamEvent> SubscribeAgentEvents( bool completeWhenIdle, out long subscriptionId ) =>
        Assistant.SubscribeAgentEvents( completeWhenIdle, out subscriptionId );

    public void UnsubscribeAgentEvents( long subscriptionId ) =>
        Assistant.UnsubscribeAgentEvents( subscriptionId );

    public ValueTask<AgentConversationSnapshot> StartAgentTurnAsync(
        string text,
        string? backendId,
        string? modelId,
        string? reasoningEffort,
        CancellationToken cancellationToken ) =>
        Assistant.StartAgentTurnAsync( text, backendId, modelId, reasoningEffort, cancellationToken );

    public ValueTask<AgentConversationSnapshot> StartAgentTurnAsync(
        string text,
        string? backendId,
        string? modelId,
        string? reasoningEffort,
        string clientOwnerId,
        CancellationToken cancellationToken ) =>
        Assistant.StartAgentTurnAsync( text, backendId, modelId, reasoningEffort, clientOwnerId, cancellationToken );

    public ValueTask<AgentConversationSnapshot> ResolveAgentPendingAsync(
        string pendingRequestId,
        string decision,
        IReadOnlyDictionary<string, string>? answers,
        CancellationToken cancellationToken ) =>
        Assistant.ResolveAgentPendingAsync( pendingRequestId, decision, answers, cancellationToken );

    public ValueTask<AgentConversationSnapshot> ResolveAgentPendingAsync(
        string pendingRequestId,
        string decision,
        IReadOnlyDictionary<string, string>? answers,
        string clientOwnerId,
        CancellationToken cancellationToken ) =>
        Assistant.ResolveAgentPendingAsync( pendingRequestId, decision, answers, clientOwnerId, cancellationToken );

    public ValueTask PrewarmAgentAsync( CancellationToken cancellationToken ) =>
        Assistant.PrewarmAgentAsync( cancellationToken );

    public ValueTask CancelAgentTurnAsync( CancellationToken cancellationToken ) =>
        Assistant.CancelAgentTurnAsync( cancellationToken );

    public ValueTask ClearAgentAsync( CancellationToken cancellationToken ) =>
        Assistant.ClearAgentAsync( cancellationToken );

    public ValueTask<AgentTrustState> GetAgentTrustAsync( CancellationToken cancellationToken ) =>
        Assistant.GetAgentTrustAsync( cancellationToken );

    public ValueTask<AgentTrustChallenge> CreateAgentTrustChallengeAsync(
        string clientOwnerId,
        int targetLevel,
        CancellationToken cancellationToken ) =>
        Assistant.CreateAgentTrustChallengeAsync( clientOwnerId, targetLevel, cancellationToken );

    public ValueTask<AgentTrustState> ApplyAgentTrustAsync(
        string clientOwnerId,
        string challengeId,
        int level,
        string? phrase,
        string? duration,
        CancellationToken cancellationToken ) =>
        Assistant.ApplyAgentTrustAsync( clientOwnerId, challengeId, level, phrase, duration, cancellationToken );

    public ValueTask<AgentGrantResult> GetAgentGrantsAsync( CancellationToken cancellationToken ) =>
        Assistant.GetAgentGrantsAsync( cancellationToken );

    public ValueTask<AgentGrantResult> AddAgentFolderGrantAsync( string kind, string scope, CancellationToken cancellationToken ) =>
        Assistant.AddAgentFolderGrantAsync( kind, scope, cancellationToken );

    public ValueTask<AgentGrantResult> RemoveAgentGrantAsync( string grantId, CancellationToken cancellationToken ) =>
        Assistant.RemoveAgentGrantAsync( grantId, cancellationToken );

    public ValueTask<AgentGrantResult> ClearAgentGrantsAsync( CancellationToken cancellationToken ) =>
        Assistant.ClearAgentGrantsAsync( cancellationToken );

    public AgentOperationsSnapshot GetAgentOperations( ) =>
        Assistant.GetAgentOperations( );

    public ValueTask<ActionExecutionResult> ExecuteAgentOutputAsync(
        long generation,
        string action,
        string? targetPath,
        string? confirmationToken,
        CancellationToken cancellationToken ) =>
        Assistant.ExecuteAgentOutputAsync( generation, action, targetPath, confirmationToken, cancellationToken );

}
