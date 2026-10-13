namespace CommandDeck;

public sealed class AgentToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string InputSchemaJson { get; init; }
}

public sealed class AgentSessionOptions
{
    public string? ModelId { get; init; }
    public string? ReasoningEffort { get; init; }
    public int TrustLevel { get; init; } = AgentTrustLevels.Default;
    public bool WebSearchEnabled { get; init; }
    public required string DeveloperInstructions { get; init; }
    public List<AgentToolDefinition> Tools { get; init; } = [];
    public int TurnTimeoutMinutes { get; init; } = AgentLimits.DefaultTurnTimeoutMinutes;

    // The authority the trust level and web search above were read under. A
    // backend checks it again where it launches or reuses a process.
    public AgentAdmission? Admission { get; init; }
}

public sealed class AgentTurnInput
{
    public required string Text { get; init; }
    public string? ModelId { get; init; }
    public string? ReasoningEffort { get; init; }
}

public sealed class AgentToolCall
{
    public required string CallId { get; init; }
    public required string Name { get; init; }
    public required string ArgumentsJson { get; init; }
}

public sealed class AgentToolResult
{
    public required string ContentText { get; init; }
    public bool Success { get; init; } = true;

    public static AgentToolResult Ok( string content ) => new( ) { ContentText = content };

    public static AgentToolResult Denied( string reason ) => new( )
    {
        ContentText = reason,
        Success = false
    };
}

public sealed class AgentUserInputRequest
{
    public List<AgentQuestion> Questions { get; init; } = [];
}

public sealed class AgentUserInputResult
{
    public Dictionary<string, string> Answers { get; init; } = new( StringComparer.Ordinal );
    public bool Declined { get; init; }
}

public interface IAgentTurnHost
{
    ValueTask OnStatusAsync( string status, CancellationToken cancellationToken );
    ValueTask OnTextAsync( string delta, CancellationToken cancellationToken );
    ValueTask OnReasoningAsync( string delta, CancellationToken cancellationToken );
    ValueTask OnUsageAsync( AiUsage usage, CancellationToken cancellationToken );
    ValueTask<AgentToolResult> OnToolCallAsync( AgentToolCall toolCall, CancellationToken cancellationToken );
    ValueTask<AgentUserInputResult> OnUserInputAsync(
        AgentUserInputRequest request,
        CancellationToken cancellationToken );
}

public interface IAgentSession : IAsyncDisposable
{
    string SessionId { get; }
    string? ModelId { get; }
    string? ReasoningEffort { get; }

    /// <summary>
    /// True once the backing process has exited or its transport has faulted.
    /// A faulted session can never run another turn and must be replaced.
    /// </summary>
    bool IsFaulted { get; }

    /// <summary>
    /// The backend process generation this session runs on, captured when the
    /// backend picked that process. Cleanup stops this generation only, so it
    /// never reaches a process that has since replaced it.
    /// </summary>
    string ProcessGeneration { get; }
    ValueTask RunTurnAsync(
        AgentTurnInput input,
        IAgentTurnHost host,
        CancellationToken cancellationToken );
    ValueTask InterruptAsync( CancellationToken cancellationToken );
}

public interface IAgentBackend : IAsyncDisposable
{
    string Id { get; }
    // True while the sandboxed process is spawned and healthy. The coordinator
    // reads this to keep a warm process alive past the conversation transcript.
    bool IsRunning { get; }
    AgentBackendDescriptor Describe( );
    ValueTask<AgentModelInventory> DiscoverModelsAsync(
        bool includeHidden,
        CancellationToken cancellationToken );
    ValueTask<AgentLoginState> GetLoginStateAsync( CancellationToken cancellationToken );
    ValueTask<AgentLoginState> StartLoginAsync(
        string method,
        AiCredentialOptions? credential,
        CancellationToken cancellationToken );
    ValueTask<AgentLoginState> CancelLoginAsync( CancellationToken cancellationToken );
    ValueTask<AgentLoginState> LogoutAsync( CancellationToken cancellationToken );
    ValueTask<IAgentSession> StartSessionAsync(
        AgentSessionOptions options,
        CancellationToken cancellationToken );
    ValueTask StopAsync( CancellationToken cancellationToken );

    // Revocation: stops work admitted before the authority epoch. Work admitted
    // at or after it belongs to the newer authority and keeps running.
    ValueTask StopBeforeAsync( long authorityEpoch, CancellationToken cancellationToken );

    // Cleanup for a session that lost its admission: stops the process only
    // while it is still that generation.
    ValueTask StopGenerationAsync( string processGeneration, CancellationToken cancellationToken );
}

/// <summary>
/// Backends whose browser sign-in ends with a code the user pastes back into
/// CommandDeck (Claude Code). Device-code and polling flows do not implement it.
/// </summary>
public interface IAgentCodeLoginBackend
{
    ValueTask<AgentLoginState> CompleteLoginAsync( string code, CancellationToken cancellationToken );
}

public interface IAgentPreferenceStore
{
    ValueTask<AgentPreferences> GetAgentPreferencesAsync( CancellationToken cancellationToken );
    ValueTask<AgentPreferences> SetAgentPreferencesAsync(
        AgentPreferences preferences,
        CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<AgentGrant>> GetAgentGrantsAsync( CancellationToken cancellationToken );
    ValueTask<AgentGrant> AddAgentGrantAsync( AgentGrant grant, CancellationToken cancellationToken );
    ValueTask<bool> UpdateAgentGrantStateAsync(
        string grantId,
        string state,
        long validatedUnixMilliseconds,
        CancellationToken cancellationToken );
    ValueTask<bool> RemoveAgentGrantAsync( string grantId, CancellationToken cancellationToken );
    ValueTask ClearAgentGrantsAsync( CancellationToken cancellationToken );
}
