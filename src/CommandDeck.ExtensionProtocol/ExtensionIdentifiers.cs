namespace CommandDeck.ExtensionProtocol;

// These values are part of the extension wire contract. Keep their spelling stable.
public static class ExtensionMethods
{
    public const string Initialize = "initialize";
    public const string Initialized = "initialized";
    public const string CommandOpen = "command.open";
    public const string CommandEvent = "command.event";
    public const string CommandData = "command.data";
    public const string CommandBroker = "command.broker";
    public const string SearchQuery = "search.query";
    public const string Shutdown = "shutdown";
    public const string CancelRequest = "$/cancelRequest";
    public const string ViewsReplace = "views.replace";
    public const string ViewsPush = "views.push";
    public const string CopyText = "effects.copy-text";
}

public static class ExtensionFeatures
{
    public static readonly System.Collections.Immutable.ImmutableArray<string> Interactive =
    [
        ExtensionFeatures.Commands,
        ExtensionFeatures.NoViewCommands,
        ExtensionFeatures.CopyText,
        ExtensionFeatures.PngAssets,
        ExtensionFeatures.ListView,
        ExtensionFeatures.DetailView,
        ExtensionFeatures.FormView,
        ExtensionFeatures.Settings,
        ExtensionFeatures.Storage,
        ExtensionFeatures.ViewStack,
        ExtensionFeatures.Pagination,
        ExtensionFeatures.CopyTextAction,
        ExtensionFeatures.OpenUrlAction,
        ExtensionFeatures.Brokers
    ];

    public const string ErrorCodes = "errors.codes@1";
    public const string RemoteSearch = "views.remote-search@1";
    public const string Commands = "commands@1";
    public const string NoViewCommands = "commands.no-view@1";
    public const string CopyText = "effects.copy-text@1";
    public const string PngAssets = "assets.png@1";
    public const string ListView = "views.list@1";
    public const string DetailView = "views.detail@1";
    public const string FormView = "views.form@1";
    public const string ViewStack = "views.stack@1";
    public const string Pagination = "views.pagination@1";
    public const string CopyTextAction = "actions.copy-text@1";
    public const string OpenUrlAction = "actions.open-url@1";
    public const string Storage = "storage@1";
    public const string Settings = "settings@1";
    public const string Brokers = "brokers@1";
    public const string Search = "search@1";
    public const string DevelopmentDebug = "development.debug@1";
}

public static class ExtensionPermissionIds
{
    public const string ClipboardWrite = "clipboard.write";
    public const string DesktopOpenUrl = "desktop.open-url";
    public const string StorageRead = "storage.read";
    public const string StorageWrite = "storage.write";
    public const string FilesRead = "files.read";
    public const string HttpRequest = "http.request";
    public const string SecretsUse = "secrets.use";
}

public static class ExtensionViewKinds
{
    public const string List = "list";
    public const string Detail = "detail";
    public const string Form = "form";
}

public static class ExtensionFormFieldKinds
{
    public const string Text = "text";
    public const string Multiline = "multiline";
    public const string Choice = "choice";
    public const string Checkbox = "checkbox";
}

public static class ExtensionPurposes
{
    public const string Interactive = "interactive";
    public const string ScopedSearch = "scoped-search";
    public const string GlobalSearch = "global-search";
}

public static class ExtensionCommandModes
{
    public const string View = "view";
    public const string NoView = "no-view";
}

public static class ExtensionCommandOutcomes
{
    public const string OpenedView = "opened-view";
    public const string Completed = "completed";
}

public static class ExtensionDataOperations
{
    public const string Settings = "settings";
    public const string Read = "read";
    public const string Write = "write";
    public const string Delete = "delete";
}

public static class ExtensionBrokerOperations
{
    public const string ListFiles = "files.list";
    public const string ReadFile = "files.read";
    public const string SendHttp = "http.send";
}

public static class ExtensionActionKinds
{
    public const string Callback = "callback";
    public const string CopyText = "copy-text";
    public const string OpenUrl = "open-url";
}

public static class ExtensionBrokerOutcomes
{
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Unknown = "unknown";
    public const string ConnectionUnavailable = "connection-unavailable";
    public const string RedirectDenied = "redirect-denied";
    public const string CredentialEchoDenied = "credential-echo-denied";
}
