using System.Text;
using System.Threading.Channels;

namespace CommandDeck;

public sealed partial class AgentConversationCoordinator
{

    private sealed class Conversation
    {
        public required string Id { get; init; }
        public required string BackendId { get; init; }
        public string? ModelId { get; init; }
        public string? ReasoningEffort { get; init; }
        public int TrustLevel { get; init; }
        public long AuthorityEpoch { get; init; }
        public int TtlMinutes { get; init; }
        public int TurnTimeoutMinutes { get; init; }
        public IAgentSession? Session { get; set; }
        public long Generation { get; init; }
        public List<AgentTranscriptEntry> Entries { get; } = [];
        public StringBuilder ActiveText { get; } = new( );
        public StringBuilder ActiveReasoning { get; } = new( );
        public List<AgentToolActivity> Tools { get; } = [];
        public string ActiveStatus { get; set; } = string.Empty;
        public string State { get; set; } = AgentConversationStates.Idle;
        public long Sequence { get; set; }
        public long ActiveTurnId { get; set; }
        public int TurnCount { get; set; }
        public Task? TurnTask { get; set; }
        public bool TurnFinished { get; set; }
        public bool TurnRunning => TurnTask is { IsCompleted: false } && !TurnFinished;
        public CancellationTokenSource? TurnCancellation { get; set; }
        public PendingRequest? Pending { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public AiUsage? Usage { get; set; }
        public AiError? LastError { get; set; }
        public long OutputGeneration { get; set; }
        public long ContextBytesUsed { get; set; }
        public int ActiveTextBytes { get; set; }
        public int ActiveReasoningBytes { get; set; }
        public int ToolCalls { get; set; }
        public string? CancelReason { get; set; }
        public string? ClientOwnerId { get; set; }
    }

    private sealed record PendingDecision( string Decision, IReadOnlyDictionary<string, string>? Answers );

    private sealed record Subscriber( long Generation, Channel<AgentStreamEvent> Channel );

    private static bool CanResolve( PendingRequest? pending, string? requesterOwnerId ) =>
        pending is not null &&
        (pending.Request.Kind == AgentPendingRequestKinds.Question ||
         string.Equals( pending.OwnerId, requesterOwnerId, StringComparison.Ordinal ));

    private sealed class PendingRequest(
        AgentPendingRequest request,
        DateTimeOffset expiresAt,
        string? ownerId )
    {
        public AgentPendingRequest Request { get; } = request;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public string? OwnerId { get; } = ownerId;
        public TaskCompletionSource<PendingDecision> Completion { get; } =
            new( TaskCreationOptions.RunContinuationsAsynchronously );
    }
}
