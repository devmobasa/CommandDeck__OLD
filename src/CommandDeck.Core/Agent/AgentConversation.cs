namespace CommandDeck;

public sealed class AgentConversationSummary
{
    public required string ConversationId { get; init; }
    public required string BackendId { get; init; }
    public string? ModelId { get; init; }
    public string? ReasoningEffort { get; init; }
    public required string State { get; init; }
    public int TurnCount { get; init; }
    public long UpdatedUnixMilliseconds { get; init; }
    public long? ExpiresUnixMilliseconds { get; init; }
    public string? PendingRequestId { get; init; }
    public int TrustLevel { get; init; }
}

public sealed class AgentTranscriptEntry
{
    public required string Role { get; init; }
    public required string Text { get; init; }
    public long TurnId { get; init; }
    public bool Truncated { get; init; }
}

public sealed class AgentToolActivity
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required string State { get; init; }
    public long TurnId { get; init; }
}

public sealed class AgentContextRequest
{
    public required string Operation { get; init; }
    public required string Path { get; init; }
    public string? Query { get; init; }
    public long MaximumBytes { get; init; }
    public string? ProposedFolder { get; init; }
    public bool ProtectedTarget { get; init; }
}

public sealed class AgentActionCard
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string Risk { get; init; }
    public string? ResultTitle { get; init; }
    public string? ActionTitle { get; init; }
    public string? ActionKind { get; init; }
    public string? Executable { get; init; }
    public List<string> Argv { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public string? EnvironmentProfile { get; init; }
    public Dictionary<string, string> Environment { get; init; } = new( StringComparer.Ordinal );
    public bool Destructive { get; init; }
    public bool CanRemember { get; init; }
    public List<DetailField> Details { get; init; } = [];
    public string? Rationale { get; init; }
}

public sealed class AgentQuestion
{
    public required string Id { get; init; }
    public string? Header { get; init; }
    public required string Prompt { get; init; }
    public List<string> Options { get; init; } = [];
    public bool AllowFreeText { get; init; } = true;
}

public sealed class AgentPendingRequest
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public long TurnId { get; init; }
    public required string Prompt { get; init; }
    public string? Detail { get; init; }
    public List<AgentQuestion> Questions { get; init; } = [];
    public AgentContextRequest? Context { get; init; }
    public AgentActionCard? Action { get; init; }
    public List<string> Decisions { get; init; } = [];
    public long ExpiresUnixMilliseconds { get; init; }
}

public sealed class AgentTurnResult
{
    public long TurnId { get; init; }
    public required string Outcome { get; init; }
    public long FinalOutputBytes { get; init; }
    public AiError? Error { get; init; }
    public List<CommandAction> OutputActions { get; init; } = [];
    public long OutputGeneration { get; init; }
}

public sealed class AgentStreamEvent
{
    public required string ConversationId { get; init; }
    public long TurnId { get; init; }
    public long Generation { get; init; }
    public long Sequence { get; init; }
    public required string Kind { get; init; }
    public string? Status { get; init; }
    public string? Text { get; init; }
    public AgentToolActivity? Tool { get; init; }
    public AgentPendingRequest? Request { get; init; }
    public AiUsage? Usage { get; init; }
    public AgentTurnResult? Done { get; init; }
}

public sealed class AgentConversationSnapshot
{
    public AgentConversationSummary? Summary { get; init; }
    public List<AgentTranscriptEntry> Entries { get; init; } = [];
    public string? ActiveText { get; init; }
    public string? ActiveReasoning { get; init; }
    public string? ActiveStatus { get; init; }
    public List<AgentToolActivity> Tools { get; init; } = [];
    public AgentPendingRequest? Pending { get; init; }
    public long Generation { get; init; }
    public long LastSequence { get; init; }
    public long ActiveTurnId { get; init; }
    public AiUsage? Usage { get; init; }
    public AiError? LastError { get; init; }
    public long OutputGeneration { get; init; }
    public List<CommandAction> OutputActions { get; init; } = [];
}

public sealed class AgentActionDraft
{
    public required string Kind { get; init; }
    public string? ResultId { get; init; }
    public string? ActionId { get; init; }
    public List<string> Argv { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public string? Rationale { get; init; }
    public string? PackageOperation { get; init; }
    public List<string> PackageNames { get; init; } = [];
}

public sealed class AgentOperationSummary
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string State { get; init; }
    public required string Title { get; init; }
    public string? Message { get; init; }
    public long UpdatedUnixMilliseconds { get; init; }
    public int Progress { get; init; }
}

public sealed class AgentOperationsSnapshot
{
    public List<AgentOperationSummary> Operations { get; init; } = [];
}
