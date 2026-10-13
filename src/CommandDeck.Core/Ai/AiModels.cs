namespace CommandDeck;

using System.Net;

public static class AiAdapterKinds
{
    public const string Ollama = "ollama";
    public const string OpenAiChatCompletions = "openai-chat-completions";
}

public static class AiLocality
{
    public const string Local = "local";
    public const string Remote = "remote";
}

public static class AiEndpointPolicy
{
    public static bool IsLiteralLoopback( Uri endpoint ) =>
        IPAddress.TryParse( endpoint.Host, out var address ) &&
        IPAddress.IsLoopback( address );
}

public static class AiHealthStates
{
    public const string Disabled = "disabled";
    public const string Configured = "configured";
    public const string Checking = "checking";
    public const string Ready = "ready";
    public const string Unavailable = "unavailable";
}

public static class AiSelectionStates
{
    public const string Ready = "ready";
    public const string Unknown = "unknown";
    public const string Unavailable = "unavailable";
    public const string Disabled = "disabled";
}

public static class AiAttachmentKinds
{
    public const string Text = "text";
    public const string Result = "result";
    public const string File = "file";
}

public static class AiGenerationOutcomes
{
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class AiErrorCategories
{
    public const string Disabled = "disabled";
    public const string InvalidConfiguration = "invalid-config";
    public const string Unsupported = "unsupported";
    public const string Unavailable = "unavailable";
    public const string CredentialMissing = "credential-missing";
    public const string Authentication = "authentication";
    public const string Permission = "permission";
    public const string RedirectRejected = "redirect-rejected";
    public const string RateLimited = "rate-limited";
    public const string ContextLimit = "context-limit";
    public const string Timeout = "timeout";
    public const string Cancelled = "cancelled";
    public const string Network = "network";
    public const string MalformedResponse = "malformed-response";
    public const string UnsupportedProviderOutput = "unsupported-provider-output";
    public const string ResponseTooLarge = "response-too-large";
    public const string PolicyDenied = "policy-denied";
    public const string StalePreflight = "stale-preflight";
    public const string DigestMismatch = "digest-mismatch";
    public const string Busy = "busy";
    public const string SaveConflict = "save-conflict";
    public const string Conflict = "conflict";
    public const string NotFound = "not-found";
    public const string PersistenceFailed = "persistence-failed";
    public const string Internal = "internal";
}

public static class AiLimits
{
    public const int MaximumProviders = 8;
    public const int MaximumModels = 256;
    public const int MaximumModelIdBytes = 128;
    public const int MaximumUserPromptBytes = 64 * 1024;
    public const int MaximumSystemPromptBytes = 16 * 1024;
    public const int MaximumAttachments = 4;
    public const int MaximumAttachmentBytes = 64 * 1024;
    public const int MaximumTransmittedTextBytes = 128 * 1024;
    public const int MaximumProviderPayloadBytes = 256 * 1024;
    public const int MaximumCredentialBytes = 16 * 1024;
    public const int MaximumDiscoveryBytes = 512 * 1024;
    public const int MaximumChunkBytes = 16 * 1024;
    public const int MaximumChunks = 16_384;
    public const int MaximumOutputBytes = 1024 * 1024;
    public const int MaximumTemplates = 32;
}

public sealed class AiProviderCapabilities
{
    public bool Discovery { get; init; }
    public bool Streaming { get; init; }
    public bool Usage { get; init; }
    public bool RateMetadata { get; init; }
    public bool ContextLimit { get; init; }
    public bool Tools { get; init; }
    public required string AdapterContractVersion { get; init; }
}

public sealed class AiProviderDescriptor
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string AdapterKind { get; init; }
    public required string Locality { get; init; }
    public required string Destination { get; init; }
    public bool Enabled { get; init; }
    public required string CredentialState { get; init; }
    public required string HealthState { get; init; }
    public string? ConfiguredModel { get; init; }
    public required AiProviderCapabilities Capabilities { get; init; }
    public AiError? Error { get; init; }
}

public sealed class AiModelDescriptor
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Owner { get; init; }
    public long? ContextLimit { get; init; }
    public bool Available { get; init; } = true;
}

public sealed class AiSelection
{
    public string? ProviderId { get; init; }
    public string? ModelId { get; init; }
    public required string State { get; init; }
    public string? Explanation { get; init; }
}

public sealed class AiAttachmentDraft
{
    public required string Kind { get; init; }
    public string? Text { get; init; }
    public string? ResultId { get; init; }
    public string? DisplayName { get; init; }
}

public sealed class AiDraft
{
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public string? WorkflowId { get; init; }
    public string? TemplateId { get; init; }
    public required string UserText { get; init; }
    public List<AiAttachmentDraft> Attachments { get; init; } = [];
}

public sealed class AiPreflightAttachment
{
    public required string Kind { get; init; }
    public required string DisplayName { get; init; }
    public required string Text { get; init; }
    public int Utf8Bytes { get; init; }
}

public sealed class AiPreflight
{
    public required string Id { get; init; }
    public long Generation { get; init; }
    public long ExpiresUnixMilliseconds { get; init; }
    public required string PayloadDigest { get; init; }
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public required string Destination { get; init; }
    public required string Locality { get; init; }
    public required string SystemMessage { get; init; }
    public required string UserMessage { get; init; }
    public List<AiPreflightAttachment> Attachments { get; init; } = [];
    public required string ProviderRequestPreview { get; init; }
    public int PayloadBytes { get; init; }
    public required string CredentialSource { get; init; }
}

public sealed class AiChunk
{
    public long Generation { get; init; }
    public int Sequence { get; init; }
    public required string Text { get; init; }
    public int CumulativeOutputBytes { get; init; }
}

public sealed class AiUsage
{
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public long? ContextLimit { get; init; }
    public long? RateLimit { get; init; }
    public long? RateRemaining { get; init; }
    public long? RateResetUnixMilliseconds { get; init; }
    public decimal? EstimatedCost { get; init; }
    public string? CostCurrency { get; init; }
    public string? CostSource { get; init; }
}

public sealed class AiDone
{
    public long Generation { get; init; }
    public required string Outcome { get; init; }
    public required string FinishCategory { get; init; }
    public int FinalOutputBytes { get; init; }
    public AiUsage? Usage { get; init; }
    public AiError? Error { get; init; }
    public List<CommandAction> OutputActions { get; init; } = [];
}

public sealed class AiError
{
    public required string Category { get; init; }
    public required string Summary { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public string? ProviderId { get; init; }
}

public sealed class AiProviderStreamEvent
{
    public string? Text { get; init; }
    public bool Terminal { get; init; }
    public string? FinishCategory { get; init; }
    public AiUsage? Usage { get; init; }
}

public sealed class AiPreparedProviderRequest
{
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public required string Destination { get; init; }
    public required byte[] Body { get; init; }
}

public sealed class AiOperationDeadlines
{
    public TimeSpan LocalDiscovery { get; init; } = TimeSpan.FromSeconds( 3 );
    public TimeSpan RemoteDiscovery { get; init; } = TimeSpan.FromSeconds( 10 );
    public TimeSpan OverallGeneration { get; init; } = TimeSpan.FromMinutes( 10 );
}

public sealed class AiProviderInventory
{
    public List<AiProviderDescriptor> Providers { get; init; } = [];
}

public sealed class AiModelInventory
{
    public required string ProviderId { get; init; }
    public List<AiModelDescriptor> Models { get; init; } = [];
    public AiError? Error { get; init; }
}

public sealed class AiTemplate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string WorkflowKind { get; init; }
    public string SystemTemplate { get; init; } = string.Empty;
    public required string UserTemplate { get; init; }
    public List<string> Parameters { get; init; } = [];
    public int Position { get; init; }
}

public sealed class AiTemplateResult
{
    public List<AiTemplate> Templates { get; init; } = [];
}
