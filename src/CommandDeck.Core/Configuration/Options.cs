namespace CommandDeck;

public sealed class CommandDeckOptions
{
    public ClipboardHistoryOptions ClipboardHistory { get; init; } = new( );
    public List<string> ScriptDirectories { get; init; } = ["$XDG_CONFIG_HOME/commanddeck/scripts"];
    public List<SnippetOptions> Snippets { get; init; } = [];
    public List<ProjectOptions> Projects { get; init; } = [];
    public StorageOptions Storage { get; init; } = new( );
    public SearchOptions Search { get; init; } = new( );
    public IndexingOptions Indexing { get; init; } = new( );
    public PrivacyOptions Privacy { get; init; } = new( );
    public UiOptions Ui { get; init; } = new( );
    public List<QuicklinkOptions> Quicklinks { get; init; } = [];
    public List<CustomCommandOptions> Commands { get; init; } = [];
    public AiOptions Ai { get; init; } = new( );
    public AgentOptions Agent { get; init; } = new( );

    public static CommandDeckOptions CreateDefault( ) => new( )
    {
        Quicklinks =
        [
            new QuicklinkOptions
            {
                Id = "arch-wiki",
                Title = "Search Arch Wiki",
                Alias = "aw",
                UrlTemplate = "https://wiki.archlinux.org/index.php?search={query}",
                Icon = "archlinux"
            },
            new QuicklinkOptions
            {
                Id = "github",
                Title = "Search GitHub",
                Alias = "gh",
                UrlTemplate = "https://github.com/search?q={query}",
                Icon = "github"
            }
        ]
    };
}

public sealed class StorageOptions
{
    public string DatabasePath { get; init; } = "$XDG_STATE_HOME/commanddeck/commanddeck.db";
    public int BusyTimeoutMilliseconds { get; init; } = 5_000;
}

public sealed class SearchOptions
{
    public int DefaultLimit { get; init; } = 50;
    public int MaximumLimit { get; init; } = 200;
    public int ProviderTimeoutMilliseconds { get; init; } = 350;
    public bool LearnFromInvocations { get; init; } = true;
    // Run the `when` and `checked` conditions of Omarchy menu rows a search
    // shows, to dim rows whose guard fails and mark checked ones.
    public bool OmarchyMenuState { get; init; } = true;
    public CalculatorOptions Calculator { get; init; } = new( );
    public RankingOptions Ranking { get; init; } = new( );

    // Named web-search engines (U3). Each engine renders a result that opens
    // urlTemplate with the query percent-encoded, and its alias becomes a
    // prefix filter (e.g. `g: rust traits` routes to Google). Set
    // includeInGlobal to also surface an engine for a bare query; the shipped
    // defaults stay alias-first so they complement, rather than replace, the
    // configurable web fallback (WebFallbackQuicklinkId).
    public List<WebEngineOptions> WebEngines { get; init; } =
    [
        new WebEngineOptions
        {
            Id = "google",
            Name = "Google",
            Alias = "g",
            UrlTemplate = "https://www.google.com/search?q={query}",
            IncludeInGlobal = false
        },
        new WebEngineOptions
        {
            Id = "duckduckgo",
            Name = "DuckDuckGo",
            Alias = "ddg",
            UrlTemplate = "https://duckduckgo.com/?q={query}",
            IncludeInGlobal = false
        }
    ];
}

public sealed class CalculatorOptions
{
    public const string UseQalcAuto = "auto";
    public const string UseQalcNever = "never";
    public const string UseQalcAlways = "always";

    public string UseQalc { get; init; } = UseQalcAuto;

    public bool AllowsQalc( ) =>
        !string.Equals( UseQalc, UseQalcNever, StringComparison.OrdinalIgnoreCase );

    public bool RequiresQalc( ) =>
        string.Equals( UseQalc, UseQalcAlways, StringComparison.OrdinalIgnoreCase );
}

public sealed class RankingOptions
{
    public double TitleWeight { get; init; } = 7;
    public double PathWeight { get; init; } = 1.5;
    public double MetadataWeight { get; init; } = 2;
    public double ContentWeight { get; init; } = 1;
    public double ConfigWeight { get; init; } = 4;
    public Dictionary<string, double> SourceWeights { get; init; } =
        new( StringComparer.Ordinal );
    public int MaximumResultsPerSource { get; init; } = 50;
    public int FirstPageSize { get; init; } = 9;
    public int MinimumSourcesOnFirstPage { get; init; } = 2;
    public TopMatchesOptions TopMatches { get; init; } = new( );
    public AdaptiveRankingOptions Adaptive { get; init; } = new( );
    public TimeAwareRankingOptions TimeAware { get; init; } = new( );
    public ContextRankingOptions Context { get; init; } = new( );
}

public sealed class TopMatchesOptions
{
    public bool Enabled { get; init; } = true;
}

public sealed class TimeAwareRankingOptions
{
    public bool Enabled { get; init; } = true;
    public double ApplicationMultiplier { get; init; } = 1;
    public double FileMultiplier { get; init; } = 0.9;
    public double WorkspaceMultiplier { get; init; } = 1.1;
    public double ActionMultiplier { get; init; } = 0.8;
}

public sealed class ContextRankingOptions
{
    public bool Enabled { get; init; } = true;
    public double ActiveApplicationBoost { get; init; } = 0.05;
    public double ActiveWorkspaceBoost { get; init; } = 0.05;
    public double CurrentDirectoryBoost { get; init; } = 0.04;
    public double SelectedProjectBoost { get; init; } = 0.06;
}

public sealed class AdaptiveRankingOptions
{
    public bool Enabled { get; init; } = true;
    public int RetentionDays { get; init; } = 730;
    public int MaximumEvents { get; init; } = 10_000;
}

public sealed class IndexingOptions
{
    public List<IndexRootOptions> Roots { get; init; } =
    [
        new IndexRootOptions { Path = "~", IncludeHidden = true, IncludeContentInSearch = false },
        new IndexRootOptions { Path = "~/Documents", IncludeHidden = true },
        new IndexRootOptions { Path = "~/.config/omarchy", IncludeHidden = true },
        new IndexRootOptions { Path = "~/.config/hypr", IncludeHidden = true }
    ];

    public long MaximumTextFileBytes { get; init; } = 2 * 1024 * 1024;
    public long MaximumDocumentFileBytes { get; init; } = 20 * 1024 * 1024;
    public int MaximumExtractedBytes { get; init; } = 2 * 1024 * 1024;
    public bool IndexDesktopApplications { get; init; } = true;
    public bool IndexOmarchyCommands { get; init; } = true;
    public bool EnablePdfExtraction { get; init; }
    public bool EnablePandocExtraction { get; init; }
    public int ExtractorTimeoutMilliseconds { get; init; } = 8_000;
    public long ExtractorMemoryLimitBytes { get; init; } = 256L * 1024 * 1024;
    public int ExtractorStandardErrorBytes { get; init; } = 32 * 1024;
    public int MaximumConcurrentExtractors { get; init; } = 2;
    public bool EnableIncrementalIndexing { get; init; } = true;
    public int IncrementalDebounceMilliseconds { get; init; } = 120;
    public int IncrementalMaximumLatencyMilliseconds { get; init; } = 750;
    public int IncrementalEventBatchLimit { get; init; } = 2_048;
    public bool EnableScheduledMaintenance { get; init; } = true;
    public int MaintenanceIntervalMinutes { get; init; } = 360;
}

public sealed class IndexRootOptions
{
    public required string Path { get; init; }
    public bool IncludeHidden { get; init; }
    public bool IncludeFileNameInSearch { get; init; } = true;
    public bool IncludeMetadataInSearch { get; init; } = true;
    public bool IncludeContentInSearch { get; init; } = true;
    public bool FollowSymbolicLinks { get; init; }
    public bool ExcludeGeneratedDirectories { get; init; } = true;
    public bool RespectGitIgnore { get; init; } = true;
    public List<string> IncludeGlobs { get; init; } = [];
    public List<string> ExcludeGlobs { get; init; } = [];
    public List<FileTypeIndexPolicyOptions> FileTypePolicies { get; init; } = [];
}

public sealed class FileTypeIndexPolicyOptions
{
    public required string Pattern { get; init; }
    public bool? IncludeFileNameInSearch { get; init; }
    public bool? IncludeMetadataInSearch { get; init; }
    public bool? IncludeContentInSearch { get; init; }
    public long? MaximumFileBytes { get; init; }
    public int? MaximumExtractedBytes { get; init; }
    public int? ExtractorTimeoutMilliseconds { get; init; }
    public long? ExtractorMemoryLimitBytes { get; init; }
}

public sealed class PrivacyOptions
{
    public bool StoreQueryHistory { get; init; }
    public int QueryHistoryRetentionDays { get; init; } = 90;
    public int MaximumQueryHistoryEntries { get; init; } = 1_000;
    public bool StoreUsageHistory { get; init; } = true;
    public bool PersistClipboard { get; init; }
    public List<string> NeverIndex { get; init; } =
    [
        "~/.ssh",
        "~/.gnupg",
        "~/.password-store",
        "~/.local/share/keyrings",
        "~/.local/share/kwalletd",
        "**/.env",
        "**/.env.*",
        "**/*credentials*",
        "**/*secret*",
        "**/*.pem",
        "**/*.key",
        "**/*.p12",
        "**/*.pfx",
        "**/Cookies",
        "**/Login Data"
    ];
}

public sealed class UiOptions
{
    public string PreferredBinding { get; init; } = "ALT+SPACE";
    public bool AlsoBindSuperSpace { get; init; }
    public string MonitorPolicy { get; init; } = "focused";
    public int DebounceMilliseconds { get; init; } = 45;
    public int Width { get; init; } = 760;
    public int MaximumVisibleRows { get; init; } = 9;
}

public sealed class QuicklinkOptions
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string UrlTemplate { get; init; }
    public string? Alias { get; init; }
    public string? Icon { get; init; }
}

public sealed class WebEngineOptions
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string UrlTemplate { get; init; }
    public required string Alias { get; init; }
    public bool IncludeInGlobal { get; init; } = true;
}

public sealed class CustomCommandOptions
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public List<string> Arguments { get; init; } = [];
    public List<string> Keywords { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public bool RequiresConfirmation { get; init; }
    public string? Icon { get; init; }
}

public sealed class AiOptions
{
    public bool Enabled { get; init; }
    public List<AiProviderOptions> Providers { get; init; } =
    [
        new AiProviderOptions
        {
            Id = "ollama",
            DisplayName = "Ollama",
            Kind = "ollama",
            BaseUrl = "http://127.0.0.1:11434",
            Model = "qwen3:8b"
        },
        new AiProviderOptions
        {
            Id = "cloud",
            DisplayName = "OpenAI",
            Kind = "openai-chat-completions",
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-5-mini",
            AllowRemoteEndpoint = true,
            Credential = new AiCredentialOptions
            {
                Kind = AiCredentialKinds.Environment,
                Name = "OPENAI_API_KEY"
            },
            Enabled = false
        }
    ];
}

public sealed class AiProviderOptions
{
    public required string Id { get; init; }
    public string? DisplayName { get; init; }
    public required string Kind { get; init; }
    public required string BaseUrl { get; init; }
    public required string Model { get; init; }
    public AiCredentialOptions Credential { get; init; } = new( );
    public bool AllowRemoteEndpoint { get; init; }
    public bool Enabled { get; init; } = true;
    public long? ContextLimit { get; init; }
}

public static class AiCredentialKinds
{
    public const string None = "none";
    public const string Environment = "environment";
    public const string Systemd = "systemd";
}

public sealed class AiCredentialOptions
{
    public string Kind { get; init; } = AiCredentialKinds.None;
    public string? Name { get; init; }
}

public sealed class AgentOptions
{
    public string? CodexBinaryPath { get; init; }
    // Optional absolute overrides for the CLI agent backends; PATH discovery is
    // the default. Each CLI signs into a CommandDeck-owned home under
    // stateDirectory, never the user's own ~/.claude, ~/.cursor, or ~/.grok.
    public string? ClaudeBinaryPath { get; init; }
    public string? CursorBinaryPath { get; init; }
    public string? GrokBinaryPath { get; init; }
    public string BubblewrapPath { get; init; } = "/usr/bin/bwrap";
    public string SystemdRunPath { get; init; } = "/usr/bin/systemd-run";
    public string RuntimeDirectory { get; init; } = "$XDG_RUNTIME_DIR/commanddeck";
    public string StateDirectory { get; init; } = "$XDG_STATE_HOME/commanddeck";
    // 0.152.1 is the reviewed version (CodexAppServerContract.ReviewedVersion).
    // The floor rejects CLIs that predate the App Server protocol; the ceiling
    // spans the rest of the 0.x line so routine Codex updates run rather than
    // hard-failing. Versions above the reviewed one are admitted but marked
    // unreviewed (status and Doctor say so). A 1.0 major bump falls outside the
    // range and re-trips the guard for a fresh schema review.
    public int MinimumCodexMajorVersion { get; init; }
    public int MinimumCodexMinorVersion { get; init; } = 152;
    public int MinimumCodexPatchVersion { get; init; } = 1;
    public int MaximumCodexMajorVersion { get; init; }
    public int MaximumCodexMinorVersion { get; init; } = 999;
    public int MaximumCodexPatchVersion { get; init; } = 999;
    public AiCredentialOptions ApiKeyCredential { get; init; } = new( )
    {
        Kind = AiCredentialKinds.Environment,
        Name = "OPENAI_API_KEY"
    };

    // Off by default, so every command the assistant runs is confirmed. When on, a
    // direct invocation of a known read-only tool (AgentLauncherTools.ClassifyCommandRisk)
    // runs without a per-command card; shells and every other command still confirm.
    public bool AutoApproveReadOnlyCommands { get; init; }
}
