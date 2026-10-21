using System.Diagnostics.CodeAnalysis;

namespace CommandDeck;

public static class ResultKinds
{
    public const string Application = "application";
    public const string File = "file";
    public const string Configuration = "configuration";
    public const string Omarchy = "omarchy";
    public const string Window = "window";
    public const string Workspace = "workspace";
    public const string Calculator = "calculator";
    public const string Quicklink = "quicklink";
    public const string CustomCommand = "custom-command";
    public const string AiCommand = "ai-command";
    public const string Status = "status";
    public const string SavedSearch = "saved-search";
    public const string Assistant = "assistant";
    public const string Timezone = "timezone";
    public const string WebSearch = "web-search";
    public const string Emoji = "emoji";
    public const string ExtensionCommand = "extension-command";
    public const string Tab = "tab";
    public const string Mail = "mail";
    public const string Process = "process";
}

public static class SourceIds
{
    public const string Folders = "folders";
    public const string Clipboard = "clipboard";
    public const string Scripts = "scripts";
    public const string Snippets = "snippets";
    public const string Projects = "projects";
    public const string OmarchyControl = "omarchy-control";
    public const string Applications = "applications";
    public const string Files = "files";
    public const string WindowsWorkspaces = "windows-workspaces";
    public const string Configuration = "configuration";
    public const string CommandsQuicklinks = "commands-quicklinks";
    public const string OmarchySystem = "omarchy-system";
    public const string Calculator = "calculator";
    public const string Ai = "ai";
    public const string Status = "status";
    public const string SystemdUnits = "systemd-units";
    public const string Timezone = "timezone";
    public const string WebSearch = "web-search";
    public const string Emoji = "emoji";
    public const string Herdr = "herdr";
    public const string Hey = "hey";
    public const string DesktopControls = "desktop-controls";
    public const string Processes = "processes";

    public static readonly IReadOnlySet<string> All = new HashSet<string>( StringComparer.Ordinal )
    {
        Projects,
        Snippets,
        Scripts,
        Clipboard,
        Folders,
        OmarchyControl,
        Applications,
        Files,
        WindowsWorkspaces,
        Configuration,
        CommandsQuicklinks,
        OmarchySystem,
        Calculator,
        Ai,
        Status,
        SystemdUnits,
        Timezone,
        WebSearch,
        Emoji,
        Herdr,
        Hey,
        DesktopControls,
        Processes
    };
}

public static class ActionKinds
{
    public const string SystemdUnit = "systemd-unit";
    public const string ExtensionOpen = "extension-open";
    public const string BrowseFolder = "browse-folder";
    public const string CollectFile = "collect-file";
    public const string ClipboardHistory = "clipboard-history";
    public const string OpenClipboardHistory = "open-clipboard-history";
    public const string RunScript = "run-script";
    public const string SnippetCopy = "snippet-copy";
    public const string SnippetPreview = "snippet-preview";
    public const string OpenPath = "open-path";
    public const string RevealPath = "reveal-path";
    public const string EditAtLine = "edit-at-line";
    public const string CopyText = "copy-text";
    public const string OpenUrl = "open-url";
    public const string LaunchDesktop = "launch-desktop";
    public const string RunArgv = "run-argv";
    // A literal argv launched detached, like an application; "terminal" opens it in one.
    public const string LaunchArgv = "launch-argv";
    public const string RunTrustedShell = "run-trusted-shell";
    public const string HyprlandDispatch = "hyprland-dispatch";
    public const string HerdrFocus = "herdr-focus";
    public const string BrowseHerdrTabs = "browse-herdr-tabs";
    // Handled by the launcher: lists one Omarchy submenu in the search.
    public const string BrowseOmarchyMenu = "browse-omarchy-menu";
    public const string HerdrCreateTab = "herdr-create-tab";
    public const string HerdrRenameTab = "herdr-rename-tab";
    public const string HerdrOpenProject = "herdr-open-project";
    public const string AiPrompt = "ai-prompt";
    public const string ApplySavedSearch = "apply-saved-search";
    public const string AgentPrompt = "agent-prompt";
    public const string AgentOpen = "agent-open";
    public const string HeyOpen = "hey-open";
    public const string HeySignIn = "hey-sign-in";
    public const string HeyInstall = "hey-install";
    public const string HeyOpenPage = "hey-open-page";
    public const string HeyReadThread = "hey-read-thread";
    public const string HeyTriage = "hey-triage";
    public const string ProcessSignal = "process-signal";
    // Handled by the invocation workflow: forgets one result's accepted uses.
    public const string ResetRanking = "reset-ranking";
    // Handled by the launcher: replaces the query (and its filter chip) and searches.
    public const string ApplyQuery = "apply-query";
}

public sealed class CommandAction
{
    public List<ActionInput> Inputs { get; init; } = [];
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Kind { get; init; }
    public Dictionary<string, string> Arguments { get; init; } = new( StringComparer.Ordinal );
    public bool IsDestructive { get; init; }
    public bool RequiresConfirmation { get; init; }
    // Successful silent actions that need the desktop, such as a screen picker,
    // close the launcher even when the user's ordinary action policy keeps it open.
    public bool CloseAfterAction { get; init; }
    // An action no menu lists: the launcher invokes it itself, such as the undo of
    // a HEY triage change. Null (omitted on the wire) for every listed action.
    public bool? Hidden { get; init; }
}

/// <summary>
/// The hidden action that undoes a visible one is named <c>undo:&lt;action id&gt;</c>
/// on the same row; after the visible action succeeds, the launcher offers it.
/// </summary>
public static class UndoActions
{
    public const string Prefix = "undo:";

    public static string For( string actionId ) => Prefix + actionId;
}

public sealed class SearchResult
{
    public SearchResult( )
    {
    }

    [SetsRequiredMembers]
    public SearchResult( SearchResult source )
    {
        ArgumentNullException.ThrowIfNull( source );
        Id = source.Id;
        Kind = source.Kind;
        Title = source.Title;
        Subtitle = source.Subtitle;
        Icon = source.Icon;
        DetailMarkdown = source.DetailMarkdown;
        Details = source.Details;
        ProviderId = source.ProviderId;
        SourceId = source.SourceId;
        Section = source.Section;
        IsFavorite = source.IsFavorite;
        Aliases = source.Aliases;
        CanCustomize = source.CanCustomize;
        Score = source.Score;
        MatchedRanges = source.MatchedRanges;
        Ranking = source.Ranking;
        Actions = source.Actions;
        Metadata = source.Metadata;
        SavedSearch = source.SavedSearch;
        InvocationReference = source.InvocationReference;
    }

    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Icon { get; init; }
    public string? DetailMarkdown { get; init; }
    public List<DetailField> Details { get; init; } = [];
    public string ProviderId { get; init; } = "unknown";
    public required string SourceId { get; init; }
    public ResultSection? Section { get; init; }
    public bool IsFavorite { get; init; }
    public List<string> Aliases { get; init; } = [];
    public bool CanCustomize { get; init; }
    public double Score { get; init; }
    public List<MatchedRange> MatchedRanges { get; init; } = [];
    public RankingExplanation Ranking { get; init; } = new( );
    public List<CommandAction> Actions { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = new( StringComparer.Ordinal );
    public string? InvocationReference { get; init; }
    public SavedSearch? SavedSearch { get; init; }
}

public static class SearchResultCapabilities
{
    public static bool IsExecutable( SearchResult result ) => result.Actions.Count > 0;
}

public sealed record DetailField( string Label, string Value, bool Monospace = false );

public sealed class FilePreview
{
    public string? ImageSource { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public int StartLine { get; init; } = 1;
    public int MatchLine { get; init; }
    public int MatchColumn { get; init; }
    public int MatchIndex { get; init; }
    public int MatchCount { get; init; }
    public bool ScanTruncated { get; init; }
    public string? OpenResultId { get; set; }
    public string? OpenInvocationReference { get; set; }

    public required string Text { get; init; }
    public required string Language { get; init; }
    public bool Truncated { get; init; }
}

public sealed class FilePreviewOutcome
{
    public FilePreview? Preview { get; init; }
    public string? Error { get; init; }

    public bool Succeeded => Preview is not null;

    public static FilePreviewOutcome Success( FilePreview preview ) => new( )
    {
        Preview = preview
    };

    public static FilePreviewOutcome Failure( string error ) => new( )
    {
        Error = error
    };
}

public readonly record struct MatchedRange( int Start, int Length );

public sealed class SearchRequest
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPrivacyClassified { get; internal set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool ContainsPrivateQuery { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? SearchOwner { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public long SearchGeneration { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    // Null preserves direct-provider fallback; an empty snapshot enables no sources.
    public IReadOnlyList<string>? EnabledSearchSources { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public ContributionRegistrySnapshot? ContributionSnapshot { get; init; }
    public string? HerdrWorkspaceId { get; init; }
    // Lists this Omarchy submenu's entries instead of searching everything.
    public string? OmarchyMenuId { get; init; }
    public string? BrowseDirectory { get; init; }
    public int BrowseOffset { get; init; }
    public string Query { get; init; } = string.Empty;
    public QueryExpression Expression { get; init; } = new( );
    public int Limit { get; init; } = 50;
    public string? ActiveApplication { get; init; }
    public string? ActiveWorkspace { get; init; }
    public string? CurrentDirectory { get; init; }
    public string? SelectedProject { get; init; }
}

public sealed class IndexedItem
{
    public required string StableId { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Path { get; init; }
    public string? Icon { get; init; }
    public string? StartupWmClass { get; init; }
    public string? Body { get; init; }
    public string? ConfigKeys { get; init; }
    public string? PayloadJson { get; init; }
    public long? SizeBytes { get; init; }
    public long? ModifiedUnixMilliseconds { get; init; }
    public string ProviderId { get; init; } = "index";
    public required string SourceId { get; init; }
    public string? ExtractorVersion { get; init; }
    public bool IncludeTitleInSearch { get; init; } = true;
    public bool IncludeMetadataInSearch { get; init; } = true;
    public List<IndexFieldPolicy> IndexPolicies { get; init; } = [];
}

public sealed class IndexedPathChange
{
    public required string Path { get; init; }
    public bool RemoveDescendants { get; init; }
    public IReadOnlyList<IndexedItem> Items { get; init; } = [];
}

// The file metadata a catalog row was indexed from.
public sealed record IndexedFileState( string Path, long? SizeBytes, long? ModifiedUnixMilliseconds );

public sealed class ResultSection
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public int Order { get; init; }
}
