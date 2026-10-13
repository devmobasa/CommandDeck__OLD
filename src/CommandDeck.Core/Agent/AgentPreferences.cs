namespace CommandDeck;

public sealed class AgentPreferences
{
    public bool Enabled { get; init; }
    public string? BackendId { get; init; }
    public string? ModelId { get; init; }
    public string? ReasoningEffort { get; init; }
    public int TrustLevel { get; init; } = AgentTrustLevels.Default;
    public string ContextPolicy { get; init; } = AgentContextPolicies.ApprovedFolders;
    public bool ShowHiddenModels { get; init; }
    public bool WebSearchEnabled { get; init; }
    public bool UseCodexConfiguration { get; init; }
    public bool AurEnabled { get; init; }
    public int ConversationTtlMinutes { get; init; } = AgentLimits.DefaultConversationTtlMinutes;
    public int TurnTimeoutMinutes { get; init; } = AgentLimits.DefaultTurnTimeoutMinutes;
    public string? TrustDuration { get; init; }
    public long? TrustLeaseExpiresUnixMilliseconds { get; init; }
    public string? LoopbackPreflightProviderId { get; init; }

    public static AgentPreferences CreateDefault( ) => new( );

    public AgentPreferences WithTrust( int level, string? duration, long? leaseExpiresUnixMilliseconds ) =>
        new( )
        {
            Enabled = Enabled,
            BackendId = BackendId,
            ModelId = ModelId,
            ReasoningEffort = ReasoningEffort,
            TrustLevel = level,
            ContextPolicy = ContextPolicy,
            ShowHiddenModels = ShowHiddenModels,
            WebSearchEnabled = WebSearchEnabled,
            UseCodexConfiguration = UseCodexConfiguration,
            AurEnabled = AurEnabled,
            ConversationTtlMinutes = ConversationTtlMinutes,
            TurnTimeoutMinutes = TurnTimeoutMinutes,
            TrustDuration = duration,
            TrustLeaseExpiresUnixMilliseconds = leaseExpiresUnixMilliseconds,
            LoopbackPreflightProviderId = LoopbackPreflightProviderId
        };

    public AgentPreferences WithAuthorityCleared( ) => new( )
    {
        Enabled = Enabled,
        BackendId = BackendId,
        ModelId = ModelId,
        ReasoningEffort = ReasoningEffort,
        TrustLevel = AgentTrustLevels.Default,
        ContextPolicy = AgentContextPolicies.ApprovedFolders,
        ShowHiddenModels = ShowHiddenModels,
        WebSearchEnabled = false,
        UseCodexConfiguration = false,
        AurEnabled = false,
        ConversationTtlMinutes = ConversationTtlMinutes,
        TurnTimeoutMinutes = TurnTimeoutMinutes,
        TrustDuration = null,
        TrustLeaseExpiresUnixMilliseconds = null,
        LoopbackPreflightProviderId = null
    };
}

public sealed class AgentTrustState
{
    public int Level { get; init; }
    public required string LevelName { get; init; }
    public string? Duration { get; init; }
    public long? LeaseExpiresUnixMilliseconds { get; init; }
    public int DefaultLevel { get; init; } = AgentTrustLevels.Default;
    public string? Explanation { get; init; }
}

public sealed class AgentTrustChallenge
{
    public required string Id { get; init; }
    public int TargetLevel { get; init; }
    public required string TargetLevelName { get; init; }
    public long ExpiresUnixMilliseconds { get; init; }
    public bool RequiresPhrase { get; init; }
    public bool RequiresDuration { get; init; }
    public required string Prompt { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public sealed class AgentGrant
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Scope { get; init; }
    public string? Identity { get; init; }
    public required string PayloadDigest { get; init; }
    public required string State { get; init; }
    public string? Label { get; init; }
    public long CreatedUnixMilliseconds { get; init; }
    public long UpdatedUnixMilliseconds { get; init; }
    public long? ExpiresUnixMilliseconds { get; init; }
    public long LastValidatedUnixMilliseconds { get; init; }
}

public sealed class AgentGrantResult
{
    public List<AgentGrant> Grants { get; init; } = [];
}
