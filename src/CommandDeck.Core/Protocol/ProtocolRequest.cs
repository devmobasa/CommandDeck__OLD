
namespace CommandDeck;


public sealed class ProtocolRequest
{
    public ClipboardRequest? Clipboard { get; init; }
    public string? HerdrWorkspaceId { get; init; }
    public string? OmarchyMenuId { get; init; }
    public string? BrowseDirectory { get; init; }
    public int BrowseOffset { get; init; }
    public int Version { get; init; } = ProtocolLimits.CurrentVersion;
    public long RequestId { get; init; }
    public required string Operation { get; init; }
    public string? Query { get; init; }
    public int Limit { get; init; } = 50;
    public int Offset { get; init; }
    public string? ResultId { get; init; }
    public string? TargetResultId { get; init; }
    public string? InvocationReference { get; init; }
    public string? ActionId { get; init; }
    public Dictionary<string, string>? InputValues { get; init; }
    public string? ProviderId { get; init; }
    public string? ModelId { get; init; }
    public bool Refresh { get; init; }
    public AiDraft? AiDraft { get; init; }
    public string? PreflightId { get; init; }
    public string? PayloadDigest { get; init; }
    public long Generation { get; init; }
    public string? SessionId { get; init; }
    public string? OutputAction { get; init; }
    public string? ConfirmationToken { get; init; }
    public AiTemplate? AiTemplate { get; init; }
    public string? AiTemplateId { get; init; }
    public AiProviderOptions? AiProvider { get; init; }
    public long? ExpectedRevision { get; init; }
    public long DraftRevision { get; init; }
    public bool Create { get; init; }
    public bool SelectAfterSave { get; init; }
    public string? LoginCode { get; init; }
    public string? ActiveApplication { get; init; }
    public string? ActiveWorkspace { get; init; }
    public string? CurrentDirectory { get; init; }
    public string? SelectedProject { get; init; }
    public bool Confirmed { get; init; }
    public long TargetRequestId { get; init; }
    public QueryExpression Expression { get; init; } = new( );
    public string? Scope { get; init; }
    public string? Path { get; init; }
    public string? RootId { get; init; }
    public IndexRootPreferences? IndexRoots { get; init; }
    public bool IncludeShortcuts { get; init; }
    public bool? Enabled { get; init; }
    public string? Alias { get; init; }
    public string? TargetFilterId { get; init; }
    public string? CustomizationId { get; init; }
    public ExtensionSessionInput? ExtensionInput { get; init; }
    public ExtensionManagementRequest? Extension { get; init; }
    public SourcePreferencePatch? SourcePatch { get; init; }
    public LauncherPreferences? Preferences { get; init; }
    public List<string> StableIds { get; init; } = [];
    public long HistoryId { get; init; }
    public QuicklinkOptions? Quicklink { get; init; }
    public string? QuicklinkId { get; init; }
    public SavedSearch? SavedSearch { get; init; }
    public string? SavedSearchId { get; init; }
    public string? BackendId { get; init; }
    public string? ReasoningEffort { get; init; }
    public bool IncludeHidden { get; init; }
    public AgentPreferences? AgentPreferences { get; init; }
    public string? LoginMethod { get; init; }
    public AiCredentialOptions? Credential { get; init; }
    public string? ConversationId { get; init; }
    public long TurnId { get; init; }
    public string? PendingRequestId { get; init; }
    public string? Decision { get; init; }
    public Dictionary<string, string>? Answers { get; init; }
    public string? ChallengeId { get; init; }
    public string? Phrase { get; init; }
    public int? TrustLevel { get; init; }
    public string? TrustDuration { get; init; }
    public string? GrantId { get; init; }
    public string? GrantKind { get; init; }
    public string? GrantScope { get; init; }
    public List<string> PackageNames { get; init; } = [];
    public string? PackageOperation { get; init; }
    public string? PreviewId { get; init; }
    public string? OperationId { get; init; }
}
