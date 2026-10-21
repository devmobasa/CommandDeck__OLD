using System.Text.RegularExpressions;

namespace CommandDeck;

public static class CustomizationLimits
{
    public const int MaximumFavorites = 256;
    public const int MaximumReorderItems = 1024;
}

public sealed class LauncherPreferences
{
    public long RegistryRevision { get; init; }
    public long PreferenceRevision { get; init; }
    public List<SourcePreference> Sources { get; init; } = [];
    public bool HideAfterAction { get; init; } = true;
    public string ClipboardHistoryShortcut { get; init; } = ClipboardShortcut.Default;
    public string WorkspacePreviewMode { get; init; } = WorkspacePreviewModes.Snapshots;
    public bool ReduceMotion { get; init; }
    public bool SearchHomeOnly { get; init; }
    public string? WebFallbackQuicklinkId { get; init; }
    public string PreferredEditor { get; init; } = EditorKinds.Graphical;
    public string HeyOpenTarget { get; init; } = HeyOpenTargets.Web;
    public EditorAdapter GraphicalEditor { get; init; } = EditorAdapter.CreateGraphicalDefault( );
    public EditorAdapter TerminalEditor { get; init; } = EditorAdapter.CreateTerminalDefault( );
    // Settings choices over the configuration's defaults; null follows the
    // configuration (privacy.storeQueryHistory; search.learnFromInvocations
    // with privacy.storeUsageHistory; search.omarchyMenuState;
    // search.ranking.topMatches.enabled).
    public bool? RememberSearches { get; init; }
    public bool? LearnFromUse { get; init; }
    public bool? OmarchyMenuState { get; init; }
    public bool? TopMatches { get; init; }

    public static LauncherPreferences CreateDefault( ) => new( )
    {
        Sources =
        [
            new() { SourceId = SourceIds.Projects, EnabledInSearch = true, EnabledOnHome = false, Position = 13 },
            new() { SourceId = SourceIds.Snippets, EnabledInSearch = true, EnabledOnHome = false, Position = 14 },
            new() { SourceId = SourceIds.Scripts, EnabledInSearch = true, EnabledOnHome = false, Position = 15 },
            new() { SourceId = SourceIds.Clipboard, EnabledInSearch = true, EnabledOnHome = false, Position = 16 },
            new() { SourceId = SourceIds.Folders, EnabledInSearch = true, EnabledOnHome = false, Position = 17 },
            new() { SourceId = SourceIds.OmarchyControl, EnabledInSearch = true, EnabledOnHome = true, Position = 1 },
            new() { SourceId = SourceIds.Applications, EnabledInSearch = true, EnabledOnHome = true, Position = 2 },
            new() { SourceId = SourceIds.Files, EnabledInSearch = true, EnabledOnHome = true, Position = 3 },
            new() { SourceId = SourceIds.WindowsWorkspaces, EnabledInSearch = true, EnabledOnHome = true, Position = 4 },
            new() { SourceId = SourceIds.Herdr, EnabledInSearch = true, EnabledOnHome = false, Position = 5 },
            new() { SourceId = SourceIds.Configuration, EnabledInSearch = true, EnabledOnHome = true, Position = 6 },
            new() { SourceId = SourceIds.CommandsQuicklinks, EnabledInSearch = true, EnabledOnHome = true, Position = 7 },
            new() { SourceId = SourceIds.OmarchySystem, EnabledInSearch = false, EnabledOnHome = false, Position = 8 },
            new() { SourceId = SourceIds.SystemdUnits, EnabledInSearch = true, EnabledOnHome = false, Position = 9 },
            new() { SourceId = SourceIds.Timezone, EnabledInSearch = true, EnabledOnHome = false, Position = 10 },
            new() { SourceId = SourceIds.WebSearch, EnabledInSearch = true, EnabledOnHome = false, Position = 11 },
            new() { SourceId = SourceIds.Emoji, EnabledInSearch = true, EnabledOnHome = false, Position = 12 },
            // Search only means hey: works; HEY never joins universal search or Home.
            new() { SourceId = SourceIds.Hey, EnabledInSearch = true, EnabledOnHome = false, Position = 18 },
            new() { SourceId = SourceIds.DesktopControls, EnabledInSearch = true, EnabledOnHome = false, Position = 19 },
            new() { SourceId = SourceIds.Processes, EnabledInSearch = true, EnabledOnHome = false, Position = 20 }
        ]
    };
}

public static class EditorKinds
{
    public const string Graphical = "graphical";
    public const string Terminal = "terminal";
}

public sealed class EditorAdapter
{
    public required string Executable { get; init; }
    public List<string> Arguments { get; init; } = [];
    public bool Terminal { get; init; }

    public static EditorAdapter CreateGraphicalDefault( ) => new( )
    {
        Executable = "code",
        Arguments = ["--goto", "{path}:{line}:{column}"]
    };

    public static EditorAdapter CreateTerminalDefault( ) => new( )
    {
        Executable = "nvim",
        Arguments = ["+call cursor({line},{column})", "{path}"],
        Terminal = true
    };
}

public static partial class EditorAdapterValidator
{
    private static readonly string[] Placeholders = ["{path}", "{line}", "{column}", "{key}"];

    [GeneratedRegex( "\\{[^{}]+\\}", RegexOptions.CultureInvariant )]
    private static partial Regex PlaceholderRegex( );

    public static EditorAdapter Validate( EditorAdapter adapter, bool terminal )
    {
        ArgumentNullException.ThrowIfNull( adapter );
        var executable = adapter.Executable.Trim( );
        if ( executable.Length is < 1 or > 512 || executable.Any( char.IsControl ) )
        {
            throw new InvalidOperationException(
                "Editor executable must be 1-512 characters without controls." );
        }
        if ( adapter.Arguments is null || adapter.Arguments.Count > 16 ||
            adapter.Arguments.Any( static value => value is null || value.Length > 512 ||
                value.Any( char.IsControl ) ) )
        {
            throw new InvalidOperationException(
                "Editor adapters accept at most 16 bounded argv templates." );
        }
        if ( !adapter.Arguments.Any( static value =>
            value.Contains( "{path}", StringComparison.Ordinal ) ) )
        {
            throw new InvalidOperationException(
                "Editor arguments must contain the {path} placeholder." );
        }
        foreach ( var argument in adapter.Arguments )
        {
            foreach ( Match match in PlaceholderRegex( ).Matches( argument ) )
            {
                if ( !Placeholders.Contains( match.Value, StringComparer.Ordinal ) )
                {
                    throw new InvalidOperationException(
                        $"Unknown editor placeholder '{match.Value}'." );
                }
            }
        }
        return new EditorAdapter
        {
            Executable = executable,
            Arguments = [.. adapter.Arguments],
            Terminal = terminal
        };
    }

    public static IReadOnlyList<string> Expand(
        EditorAdapter adapter,
        string path,
        string line,
        string column,
        string key ) => adapter.Arguments.Select( argument => argument
            .Replace( "{path}", path, StringComparison.Ordinal )
            .Replace( "{line}", line, StringComparison.Ordinal )
            .Replace( "{column}", column, StringComparison.Ordinal )
            .Replace( "{key}", key, StringComparison.Ordinal ) ).ToArray( );
}

public sealed class SourcePreference
{
    public required string SourceId { get; init; }
    public bool EnabledInSearch { get; init; }
    public bool EnabledOnHome { get; init; }
    public int Position { get; init; }
}

public sealed class SourceDescriptor
{
    public bool Available { get; init; } = true;
    public required string Id { get; init; }
    public required string Label { get; init; }

    // Freedesktop icon name. The daemon owns it for the same reason it owns
    // Label: the shell must not invent presentation for backend identifiers.
    public string? Icon { get; init; }

    // False for a source whose rows never appear on Home (HEY mail); Settings
    // shows no Home switch for it and a save cannot turn one on.
    public bool HomeAvailable { get; init; } = true;
}

public static class LauncherSourceRegistry
{
    private static readonly SourceDescriptor[] ConfigurableDescriptors =
    [
        new() { Id = SourceIds.Projects, Label = "Projects", Icon = "folder-development" },
        new() { Id = SourceIds.Snippets, Label = "Snippets", Icon = "edit-paste" },
        new() { Id = SourceIds.Scripts, Label = "Script commands", Icon = "utilities-terminal" },
        new() { Id = SourceIds.Clipboard, Label = "Clipboard history", Icon = "edit-paste" },
        new() { Id = SourceIds.Folders, Label = "Browse folders", Icon = "folder" },
        new() { Id = SourceIds.OmarchyControl, Label = "Omarchy Control", Icon = "emblem-system" },
        new() { Id = SourceIds.Applications, Label = "Applications", Icon = "applications-other" },
        new() { Id = SourceIds.Files, Label = "Files", Icon = "folder" },
        new() { Id = SourceIds.WindowsWorkspaces, Label = "Windows & Workspaces", Icon = "user-desktop" },
        new() { Id = SourceIds.Herdr, Label = "Herdr", Icon = "utilities-terminal" },
        new() { Id = SourceIds.Hey, Label = "HEY", Icon = "internet-mail", HomeAvailable = false },
        new() { Id = SourceIds.Configuration, Label = "Configuration", Icon = "preferences-system" },
        new() { Id = SourceIds.CommandsQuicklinks, Label = "Commands & Quicklinks", Icon = "utilities-terminal" },
        new() { Id = SourceIds.OmarchySystem, Label = "System Menu", Icon = "applications-system" },
        new() { Id = SourceIds.SystemdUnits, Label = "Services", Icon = "applications-system" },
        new() { Id = SourceIds.Timezone, Label = "Timezone", Icon = "preferences-system-time" },
        new() { Id = SourceIds.WebSearch, Label = "Web Search", Icon = "internet-web-browser" },
        new() { Id = SourceIds.Emoji, Label = "Emoji", Icon = "face-smile" },
        new() { Id = SourceIds.DesktopControls, Label = "Desktop controls", Icon = "preferences-desktop" },
        new() { Id = SourceIds.Processes, Label = "Processes", Icon = "utilities-system-monitor" }
    ];

    public static IReadOnlyList<SourceDescriptor> Configurable => ConfigurableDescriptors;

    public static string Label( string sourceId ) =>
        ConfigurableDescriptors.FirstOrDefault( value => value.Id == sourceId )?.Label ??
        sourceId switch
        {
            SourceIds.Calculator => "Calculator",
            SourceIds.Ai => "AI",
            _ => "Results"
        };
}

public sealed class FilterDescriptor
{
    public bool Available { get; init; } = true;
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Token { get; init; }
    public List<string> Aliases { get; init; } = [];
    public string? ProviderId { get; init; }
    public string? SourceId { get; init; }
    public string? Kind { get; init; }
}

public sealed class ShortcutBinding
{
    public required string SurfaceId { get; init; }
    public required string Label { get; init; }
    public List<string> Chords { get; init; } = [];
    public required string Status { get; init; }
    public string? Detail { get; init; }
    public bool IsAvailable { get; init; }
}

public sealed class ShortcutInventory
{
    public bool Available { get; init; }
    public string? Detail { get; init; }
    public List<string> Chords { get; init; } = [];
}

public static class ShortcutStatuses
{
    public const string Configured = "configured";
    public const string NotConfigured = "not-configured";
    public const string Unavailable = "unavailable";
}

public sealed class QuicklinkChoice
{
    public required string Id { get; init; }
    public required string Title { get; init; }
}

public sealed class CustomizationSummary
{
    public required string Id { get; init; }
    public required string StableId { get; init; }
    public required string Title { get; init; }
    public required string SourceId { get; init; }
    public string? Icon { get; init; }
    public bool IsFavorite { get; init; }
    public List<string> Aliases { get; init; } = [];
    public int Position { get; init; }
    public bool IsAvailable { get; init; }
}

// The configuration's values for the preferences that may follow it, so
// Settings can show what an unset choice means.
public sealed class PreferenceDefaults
{
    public bool RememberSearches { get; init; }
    public bool LearnFromUse { get; init; }
    public bool OmarchyMenuState { get; init; }
    public bool TopMatches { get; init; } = true;
}

public sealed class PreferencesSnapshot
{
    public required LauncherPreferences Preferences { get; init; }
    public PreferenceDefaults? Defaults { get; init; }
    public List<CustomizationSummary> Customizations { get; init; } = [];
    public List<ShortcutBinding> Shortcuts { get; init; } = [];
    public int TotalCustomizationCount { get; init; }
    public bool CustomizationsTruncated { get; init; }
}

public sealed class BootstrapSnapshot
{
    public required LauncherPreferences Preferences { get; init; }
    public List<SourceDescriptor> Sources { get; init; } = [];
    public List<FilterDescriptor> Filters { get; init; } = [];
    public List<FilterAlias> FilterAliases { get; init; } = [];
    public List<SavedSearch> SavedSearches { get; init; } = [];
    public List<QuicklinkChoice> FallbackQuicklinks { get; init; } = [];
    public bool SupportsCustomization { get; init; } = true;
    public bool SupportsShortcutInspection { get; init; } = true;
}

public sealed class FilterAlias
{
    public required string Alias { get; init; }
    public required string FilterId { get; init; }
}

public sealed class FilterAliasResult
{
    public List<FilterAlias> Aliases { get; init; } = [];
    public List<FilterDescriptor> Filters { get; init; } = [];
    public string? Message { get; init; }
}

public static partial class FilterAliasValidator
{
    public const int MaximumAliases = 64;

    [GeneratedRegex( "^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$", RegexOptions.CultureInvariant )]
    private static partial Regex ValidAliasRegex( );

    public static string NormalizeAndValidate( string alias )
    {
        var value = alias.Trim( ).TrimEnd( ':' );
        if ( !ValidAliasRegex( ).IsMatch( value ) )
        {
            throw new InvalidOperationException(
                "Filter aliases must be 1-32 ASCII letters, digits, dots, underscores, or hyphens." );
        }
        if ( SearchFilterRegistry.IsReserved( value ) )
        {
            throw new InvalidOperationException(
                $"Filter alias '{value}' conflicts with built-in launcher syntax." );
        }
        return value.ToLowerInvariant( );
    }

    public static string ValidateFilterId( string filterId )
    {
        var result = SearchFilterRegistry.FindById( filterId )?.Id;
        if ( result is null )
        {
            throw new InvalidOperationException( $"Unknown filter '{filterId}'." );
        }

        return result;
    }
}

public sealed class CustomizationResult
{
    public required string StableId { get; init; }
    public bool IsFavorite { get; init; }
    public List<string> Aliases { get; init; } = [];
    public bool IsHidden { get; init; }
    public string? Message { get; init; }
}

public sealed class CustomizationExport
{
    public int OmittedClipboardSearches { get; init; }
    public int Version { get; init; } = 3;
    public required LauncherPreferences Preferences { get; init; }
    public List<CustomizationSummary> Customizations { get; init; } = [];
    public List<HiddenItemSummary> HiddenItems { get; init; } = [];
    public List<FilterAlias> FilterAliases { get; init; } = [];
    public List<SavedSearch> SavedSearches { get; init; } = [];
    public AiSelection? AiSelection { get; init; }
    public List<AiTemplate> AiTemplates { get; init; } = [];
}

public static partial class AliasValidator
{
    [GeneratedRegex( "^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$", RegexOptions.CultureInvariant )]
    private static partial Regex ValidAliasRegex( );

    public static string NormalizeAndValidate( string alias )
    {
        var value = alias.Trim( );
        if ( !ValidAliasRegex( ).IsMatch( value ) )
        {
            throw new InvalidOperationException(
                "Aliases must be 1-32 ASCII letters, digits, dots, underscores, or hyphens." );
        }

        if ( SearchFilterRegistry.IsReserved( value ) )
        {
            throw new InvalidOperationException( $"Alias '{value}' is reserved by launcher syntax." );
        }

        return value;
    }
}
