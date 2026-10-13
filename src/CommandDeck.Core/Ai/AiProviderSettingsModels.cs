namespace CommandDeck;

public static class AiProviderSettingsSources
{
    public const string LegacyConfiguration = "legacy-configuration";
    public const string ManagedDocument = "managed-document";
}

public static class AiProviderTestOutcomes
{
    public const string Models = "models";
    public const string Empty = "empty";
    public const string Unsupported = "unsupported";
    public const string Failed = "failed";
}

public static class AiProviderSettingsLimits
{
    public const int SchemaVersion = 1;
    public const int MaximumDocumentBytes = 64 * 1024;
    public const int MaximumLoginCodeCharacters = 256;
}

/// <summary>
/// The daemon-owned, non-secret provider document. It carries credential
/// references only; provider/model selection and templates stay in SQLite.
/// </summary>
public sealed class AiProviderSettingsDocument
{
    public int SchemaVersion { get; init; } = AiProviderSettingsLimits.SchemaVersion;
    public long Revision { get; init; }
    public bool Enabled { get; init; }
    public List<AiProviderOptions> Providers { get; init; } = [];
}

public sealed class AiProviderPreset
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Kind { get; init; }
    public required string BaseUrl { get; init; }
    public string CredentialKind { get; init; } = AiCredentialKinds.None;
    public string? CredentialName { get; init; }
    public bool RemoteEndpoint { get; init; }
    public required string Description { get; init; }
}

public static class AiProviderPresets
{
    public static IReadOnlyList<AiProviderPreset> All { get; } =
    [
        new AiProviderPreset
        {
            Id = "ollama",
            DisplayName = "Ollama",
            Kind = AiAdapterKinds.Ollama,
            BaseUrl = "http://127.0.0.1:11434",
            Description = "Local Ollama server. Models must be installed in Ollama separately."
        },
        new AiProviderPreset
        {
            Id = "openai",
            DisplayName = "OpenAI",
            Kind = AiAdapterKinds.OpenAiChatCompletions,
            BaseUrl = "https://api.openai.com/v1",
            CredentialKind = AiCredentialKinds.Environment,
            CredentialName = "OPENAI_API_KEY",
            RemoteEndpoint = true,
            Description = "OpenAI Chat Completions. Requires an API key reference and remote consent."
        },
        new AiProviderPreset
        {
            Id = "xai-grok",
            DisplayName = "xAI Grok",
            Kind = AiAdapterKinds.OpenAiChatCompletions,
            BaseUrl = "https://api.x.ai/v1",
            CredentialKind = AiCredentialKinds.Environment,
            CredentialName = "XAI_API_KEY",
            RemoteEndpoint = true,
            Description = "xAI Grok models through the OpenAI-compatible Chat Completions endpoint."
        }
    ];
}

/// <summary>
/// Effective provider settings as reported to management clients. Unlike the
/// inventory, this includes editable definitions with credential reference
/// names; it never includes credential values.
/// </summary>
public sealed class AiProviderSettings
{
    public bool Supported { get; init; } = true;
    public bool Enabled { get; init; }
    public required string Source { get; init; }
    public long Revision { get; init; }
    public bool LegacyIgnored { get; init; }
    public List<AiProviderOptions> Providers { get; init; } = [];
    public AiSelection? Selection { get; init; }
    public List<AiProviderPreset> Presets { get; init; } = [];
    public List<string> ReservedIds { get; init; } = [];
    public AiError? Error { get; init; }
}

public sealed class AiProviderSaveResult
{
    public required string ProviderId { get; init; }
    public required AiProviderSettings Settings { get; init; }
    public bool SelectionRequested { get; init; }
    public bool SelectionApplied { get; init; }
    public AiSelection? Selection { get; init; }
    public AiError? SelectionError { get; init; }
}

public sealed class AiProviderTestResult
{
    public required string ProviderId { get; init; }
    public long DraftRevision { get; init; }
    public required string Outcome { get; init; }
    public required string Destination { get; init; }
    public required string Locality { get; init; }
    public required string CredentialState { get; init; }
    public List<AiModelDescriptor> Models { get; init; } = [];
    public AiError? Error { get; init; }
}
