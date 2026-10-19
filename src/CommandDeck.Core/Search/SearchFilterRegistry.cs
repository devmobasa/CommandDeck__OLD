namespace CommandDeck;

public static class SearchFilterRegistry
{
    public const string WebSearchFilterId = "web-search";

    private static readonly FilterDescriptor[] Descriptors =
    [
        Filter("projects", "Projects", "proj:", ProviderIds.Projects, SourceIds.Projects),
        Filter("snippets", "Snippets", "snip:", ProviderIds.Snippets, SourceIds.Snippets),
        Filter("scripts", "Script commands", "script:", ProviderIds.Scripts, SourceIds.Scripts),
        Filter("clipboard", "Clipboard history", "clip:", ProviderIds.Clipboard, SourceIds.Clipboard),
        Filter("folders", "Browse folders", "browse:", ProviderIds.Folders, SourceIds.Folders),
        Filter("omarchy-control", "Omarchy Control", "oma:", ProviderIds.Catalog, SourceIds.OmarchyControl),
        Filter("applications", "Applications", "app:", ProviderIds.Catalog, SourceIds.Applications, ResultKinds.Application),
        Filter("files", "Files", "file:", ProviderIds.Catalog, SourceIds.Files, ResultKinds.File, "text:", "files:"),
        Filter("windows", "Windows", "win:", ProviderIds.Hyprland, SourceIds.WindowsWorkspaces, ResultKinds.Window, "window:"),
        Filter("workspaces", "Workspaces", "workspace:", ProviderIds.Hyprland, SourceIds.WindowsWorkspaces, ResultKinds.Workspace, "ws:", "workspaces:"),
        Filter("herdr", "Herdr", "herdr:", ProviderIds.Herdr, SourceIds.Herdr),
        Filter(SourceIds.Hey, "HEY", "hey:", ProviderIds.Hey, SourceIds.Hey, null, "mail:"),
        Filter("herdr-spaces", "Herdr spaces", "hspace:", ProviderIds.Herdr, SourceIds.Herdr, ResultKinds.Workspace, "herdr-space:", "hspaces:"),
        Filter("herdr-tabs", "Herdr tabs", "htab:", ProviderIds.Herdr, SourceIds.Herdr, ResultKinds.Tab, "herdr-tab:", "htabs:"),
        Filter(HerdrSearch.AttentionFilterId, "Needs attention", "hattention:", ProviderIds.Herdr, SourceIds.Herdr, ResultKinds.Tab, "herdr-attention:"),
        Filter("configuration", "Configuration", "cfg:", ProviderIds.Catalog, SourceIds.Configuration, ResultKinds.Configuration, "config:"),
        Filter("commands", "Commands", "cmd:", ProviderIds.Catalog, SourceIds.CommandsQuicklinks, ResultKinds.CustomCommand),
        Filter("quicklinks", "Quicklinks", "link:", ProviderIds.Catalog, SourceIds.CommandsQuicklinks, ResultKinds.Quicklink),
        Filter("system-menu", "System Menu", "system:", ProviderIds.Catalog, SourceIds.OmarchySystem),
        Filter("ai", "AI", "ai:", ProviderIds.Assistant, SourceIds.Ai, null, "codex:", "ask:", "claude:", "cursor:", "grok:"),
        Filter("services", "Services", "svc:", ProviderIds.SystemdUnits, SourceIds.SystemdUnits, null, "unit:", "service:"),
        Filter("timezone", "Timezone", "tz:", ProviderIds.Timezone, SourceIds.Timezone, null, "time:"),
        Filter(WebSearchFilterId, "Web Search", "web:", ProviderIds.WebSearch, SourceIds.WebSearch),
        Filter("emoji", "Emoji", "emoji:", ProviderIds.Emoji, SourceIds.Emoji, ResultKinds.Emoji, "e:"),
        Filter("desktop-controls", "Desktop controls", "ctl:", ProviderIds.DesktopControls, SourceIds.DesktopControls, null, "control:", "controls:"),
        Filter(ProcessResultIds.FilterId, "Processes", "kill:", ProviderIds.Processes, SourceIds.Processes, ResultKinds.Process, "proc:", "process:", "processes:")
    ];

    private static readonly Dictionary<string, FilterDescriptor> ById = Descriptors
        .ToDictionary( static value => value.Id, StringComparer.OrdinalIgnoreCase );
    private static readonly Dictionary<string, FilterDescriptor> ByToken = Descriptors
        .SelectMany( static value => value.Aliases.Prepend( value.Token ) )
        .ToDictionary(
            static token => token.TrimEnd( ':' ),
            token => Descriptors.First( value =>
                value.Token.Equals( token, StringComparison.OrdinalIgnoreCase ) ||
                value.Aliases.Contains( token, StringComparer.OrdinalIgnoreCase ) ),
            StringComparer.OrdinalIgnoreCase );

    public static IReadOnlyList<FilterDescriptor> All => Descriptors;

    public static FilterDescriptor? FindById( string? id ) =>
        !string.IsNullOrWhiteSpace( id ) && ById.TryGetValue( id, out var value )
            ? value
            : null;

    public static FilterDescriptor? FindByToken( string? token ) =>
        !string.IsNullOrWhiteSpace( token ) &&
        ByToken.TryGetValue( token.Trim( ).TrimEnd( ':' ), out var value )
            ? value
            : null;

    public static bool IsReserved( string value ) =>
        FindByToken( value ) is not null ||
        value.Equals( "home", StringComparison.OrdinalIgnoreCase ) ||
        value.Equals( "settings", StringComparison.OrdinalIgnoreCase );

    public static bool Matches( FilterDescriptor filter, SearchResult result ) =>
        string.Equals( filter.SourceId, result.SourceId, StringComparison.Ordinal ) &&
        (filter.Kind is null || string.Equals( filter.Kind, result.Kind, StringComparison.Ordinal ));

    public static IReadOnlyList<FilterDescriptor> MergeUserAliases(
        IReadOnlyList<FilterAlias> userAliases ) => Descriptors.Select( descriptor => new FilterDescriptor
        {
            Id = descriptor.Id,
            Label = descriptor.Label,
            Token = descriptor.Token,
            ProviderId = descriptor.ProviderId,
            SourceId = descriptor.SourceId,
            Kind = descriptor.Kind,
            Aliases =
            [
                .. descriptor.Aliases,
                .. userAliases
                    .Where(alias => string.Equals(
                        alias.FilterId,
                        descriptor.Id,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(static alias => alias.Alias + ":")
            ]
        } ).ToArray( );

    private static FilterDescriptor Filter(
        string id,
        string label,
        string token,
        string provider,
        string source,
        string? kind = null,
        params string[] aliases ) => new( )
        {
            Id = id,
            Label = label,
            Token = token,
            ProviderId = provider,
            SourceId = source,
            Kind = kind,
            Aliases = [.. aliases]
        };
}
