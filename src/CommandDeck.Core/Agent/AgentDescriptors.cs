namespace CommandDeck;

public sealed class AgentBackendCapabilities
{
    public bool Conversation { get; init; }
    public bool Tools { get; init; }
    public bool Reasoning { get; init; }
    public bool ModelDiscovery { get; init; }
    public bool PreflightRequired { get; init; }
    public bool Usage { get; init; }
    public bool Login { get; init; }
    public required string ContractVersion { get; init; }
}

public sealed class AgentBackendDescriptor
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Kind { get; init; }
    public required string ProcessLocality { get; init; }
    public required string InferenceLocality { get; init; }
    public bool Available { get; init; }
    public required string HealthState { get; init; }
    public required string AuthenticationState { get; init; }
    public required AgentBackendCapabilities Capabilities { get; init; }
    public string? Explanation { get; init; }
    public string? ConfiguredModel { get; init; }
    public AiError? Error { get; init; }
}

public sealed class AgentBackendInventory
{
    public List<AgentBackendDescriptor> Backends { get; init; } = [];
    public string? SelectedBackendId { get; init; }
    public bool Enabled { get; init; }
}

public sealed class AgentReasoningEffort
{
    public required string Id { get; init; }
    public string? Description { get; init; }
}

public sealed class AgentModelDescriptor
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public bool Hidden { get; init; }
    public bool IsDefault { get; init; }
    public string? DefaultReasoningEffort { get; init; }
    public List<AgentReasoningEffort> ReasoningEfforts { get; init; } = [];
}

public sealed class AgentModelInventory
{
    public required string BackendId { get; init; }
    public List<AgentModelDescriptor> Models { get; init; } = [];
    public string? DefaultModelId { get; init; }
    public AiError? Error { get; init; }
}

public sealed class AgentLoginState
{
    public required string State { get; init; }
    public string? BackendId { get; init; }
    public string? Method { get; init; }
    // True while the backend waits for a code the user must paste back from
    // the browser (Claude Code). Device-code and polling flows never set it.
    public bool RequiresCode { get; init; }
    public string? UserCode { get; init; }
    public string? VerificationUrl { get; init; }
    public string? InvocationReference { get; set; }
    public long? ExpiresUnixMilliseconds { get; init; }
    public string? Message { get; init; }
    public List<string> SupportedMethods { get; init; } = [];
}

public sealed class AgentHelperStatus
{
    public bool Installed { get; init; }
    public string? Version { get; init; }
    public bool VersionMatches { get; init; }
    public bool PolkitAgentLikely { get; init; }
    public string? Message { get; init; }
}

public sealed class AgentStatus
{
    public bool Enabled { get; init; }
    public AgentBackendDescriptor? Backend { get; init; }
    public required AgentTrustState Trust { get; init; }
    public required AgentLoginState Login { get; init; }
    public required AgentPreferences Preferences { get; init; }
    public bool SandboxAvailable { get; init; }
    public bool TransientUnitAvailable { get; init; }
    // Settable so the CLI, which never starts Codex, can report the binary it resolves.
    public string? BinaryPath { get; set; }
    public string? BinaryVersion { get; set; }
    public bool? BinaryReviewed { get; set; }
    public AgentHelperStatus Helper { get; init; } = new( );
    public AgentConversationSummary? Conversation { get; init; }
    public int PendingRequestCount { get; init; }
    public List<string> Warnings { get; init; } = [];
}
