namespace CommandDeck;

public static partial class ProtocolOperations
{
    public const string FilesSelect = "files.select";
    public const string FilesClear = "files.clear";
    public const string FilesBatch = "files.batch";
    public const string Search = "search";
    public const string Suggest = "suggest";
    public const string Invoke = "invoke";
    public const string Preview = "preview";
    public const string Status = "status";
    public const string Reindex = "reindex";
    public const string IndexPause = "index.pause";
    public const string IndexResume = "index.resume";
    public const string AiProvidersGet = "ai.providers.get";
    public const string AiModelsGet = "ai.models.get";
    public const string AiSelectionGet = "ai.selection.get";
    public const string AiSelectionUpdate = "ai.selection.update";
    public const string AiTemplatesGet = "ai.templates.get";
    public const string AiTemplatesSave = "ai.templates.save";
    public const string AiTemplatesDelete = "ai.templates.delete";
    public const string AiPreflight = "ai.preflight";
    public const string AiStart = "ai.start";
    public const string AiOutput = "ai.output";
    public const string AiClear = "ai.clear";
    public const string AiSettingsGet = "ai.settings.get";
    public const string AiSettingsUpdate = "ai.settings.update";
    public const string AiProvidersSave = "ai.providers.save";
    public const string AiProvidersDelete = "ai.providers.delete";
    public const string AiProvidersTest = "ai.providers.test";
    public const string Ping = "ping";
    public const string Cancel = "cancel";
    public const string ExtensionsManage = "extensions.manage";
    public const string ExtensionOpen = "extension.open";
    public const string ExtensionPresent = "extension.present";
    public const string ExtensionAction = "extension.action";
    public const string ExtensionClose = "extension.close";
    public const string Bootstrap = "bootstrap";
    public const string PreferencesGet = "preferences.get";
    public const string PreferencesUpdate = "preferences.update";
    public const string FavoriteSet = "favorite.set";
    public const string FavoriteRemoveOrphan = "favorite.remove-orphan";
    public const string FavoriteReorder = "favorite.reorder";
    public const string AliasSet = "alias.set";
    public const string FilterAliasSet = "filter-alias.set";
    public const string CustomizationsExport = "customizations.export";
    public const string CustomizationsReset = "customizations.reset";
    public const string HiddenSet = "hidden.set";
    public const string HiddenRestore = "hidden.restore";
    public const string HiddenList = "hidden.list";
    public const string CatalogInspect = "catalog.inspect";
    public const string DatabaseIntegrity = "database.integrity";
    public const string DatabaseOptimize = "database.optimize";
    public const string DatabaseRepair = "database.repair";
    public const string DatabasePurge = "database.purge";
    public const string PrivacyAudit = "privacy.audit";
    public const string QueryHistoryGet = "history.get";
    public const string QueryHistoryDelete = "history.delete";
    public const string QueryHistoryReset = "history.reset";
    public const string SavedSearchGet = "saved-search.get";
    public const string SavedSearchSave = "saved-search.save";
    public const string SavedSearchDelete = "saved-search.delete";
    public const string AgentBackendsGet = "agent.backends.get";
    public const string AgentStatusGet = "agent.status.get";
    public const string AgentModelsGet = "agent.models.get";
    public const string AgentPreferencesGet = "agent.preferences.get";
    public const string AgentPreferencesUpdate = "agent.preferences.update";
    public const string AgentLoginStart = "agent.login.start";
    public const string AgentLoginCancel = "agent.login.cancel";
    public const string AgentLoginComplete = "agent.login.complete";
    public const string AgentLogout = "agent.logout";
    public const string AgentConversationGet = "agent.conversation.get";
    public const string AgentTurnStart = "agent.turn.start";
    public const string AgentTurnInput = "agent.turn.input";
    public const string AgentTurnCancel = "agent.turn.cancel";
    public const string AgentClear = "agent.clear";
    public const string AgentPrewarm = "agent.prewarm";
    public const string AgentTrustGet = "agent.trust.get";
    public const string AgentTrustChallenge = "agent.trust.challenge";
    public const string AgentTrustUpdate = "agent.trust.update";
    public const string AgentGrantsGet = "agent.grants.get";
    public const string AgentGrantsAdd = "agent.grants.add";
    public const string AgentGrantsRemove = "agent.grants.remove";
    public const string AgentGrantsClear = "agent.grants.clear";
    public const string AgentContextResolve = "agent.context.resolve";
    public const string AgentOperationsGet = "agent.operations.get";
    public const string AgentOutput = "agent.output";
    public const string PackageSearch = "package.search";
    public const string PackagePreview = "package.preview";
    public const string PackageCommit = "package.commit";
    public const string PackageCancel = "package.cancel";
}

public static class ProtocolMessageTypes
{
    public const string ClipboardResult = "clipboard-result";
    public const string FileSelection = "file-selection";
    public const string Result = "result";
    public const string Done = "done";
    public const string Error = "error";
    public const string Status = "status";
    public const string Progress = "progress";
    public const string ActionResult = "action-result";
    public const string PreviewResult = "preview-result";
    public const string AiProvidersResult = "ai-providers-result";
    public const string AiModelsResult = "ai-models-result";
    public const string AiSelectionResult = "ai-selection-result";
    public const string AiTemplatesResult = "ai-templates-result";
    public const string AiPreflightResult = "ai-preflight-result";
    public const string AiStatus = "ai-status";
    public const string AiChunk = "ai-chunk";
    public const string AiDone = "ai-done";
    public const string AiSettingsResult = "ai-settings-result";
    public const string AiProviderSaveResult = "ai-provider-save-result";
    public const string AiProviderTestResult = "ai-provider-test-result";
    public const string Pong = "pong";
    public const string ExtensionsResult = "extensions-result";
    public const string ExtensionSession = "extension-session";
    public const string BootstrapResult = "bootstrap-result";
    public const string PreferencesResult = "preferences-result";
    public const string CustomizationResult = "customization-result";
    public const string ExportResult = "export-result";
    public const string HiddenItemsResult = "hidden-items-result";
    public const string CatalogInspection = "catalog-inspection";
    public const string CatalogMaintenanceResult = "catalog-maintenance-result";
    public const string PrivacyAuditResult = "privacy-audit-result";
    public const string QueryHistoryResult = "query-history-result";
    public const string FilterAliasResult = "filter-alias-result";
    public const string SuggestionsResult = "suggestions-result";
    public const string SavedSearchResult = "saved-search-result";
    public const string AgentBackendsResult = "agent-backends-result";
    public const string AgentStatusResult = "agent-status-result";
    public const string AgentModelsResult = "agent-models-result";
    public const string AgentPreferencesResult = "agent-preferences-result";
    public const string AgentLoginResult = "agent-login-result";
    public const string AgentConversationResult = "agent-conversation-result";
    public const string AgentEvent = "agent-event";
    public const string AgentTrustResult = "agent-trust-result";
    public const string AgentTrustChallengeResult = "agent-trust-challenge-result";
    public const string AgentGrantsResult = "agent-grants-result";
    public const string AgentOperationsResult = "agent-operations-result";
    public const string PackageSearchResult = "package-search-result";
    public const string PackagePreviewResult = "package-preview-result";
    public const string PackageCommitResult = "package-commit-result";
}

public sealed class ProtocolMessage
{
    public ClipboardResponse? Clipboard { get; init; }
    public List<FileSelectionItem>? FileSelection { get; init; }
    public int Version { get; init; } = ProtocolLimits.CurrentVersion;
    public long RequestId { get; init; }
    public required string Type { get; init; }
    public string? SessionId { get; init; }
    public SearchResult? Result { get; init; }
    public SearchCompletion? SearchCompletion { get; init; }
    public ActionExecutionResult? ActionResult { get; init; }
    public FilePreview? Preview { get; init; }
    public AiProviderInventory? AiProviders { get; init; }
    public AiModelInventory? AiModels { get; init; }
    public AiSelection? AiSelection { get; init; }
    public AiPreflight? AiPreflight { get; init; }
    public AiChunk? AiChunk { get; init; }
    public AiDone? AiDone { get; init; }
    public AiError? AiError { get; init; }
    public AiTemplateResult? AiTemplates { get; init; }
    public AiProviderSettings? AiSettings { get; init; }
    public AiProviderSaveResult? AiProviderSave { get; init; }
    public AiProviderTestResult? AiProviderTest { get; init; }
    public RuntimeStatusSnapshot? RuntimeStatus { get; init; }
    public IndexProgress? Progress { get; init; }
    public ReindexReport? ReindexReport { get; init; }
    public string? Error { get; init; }
    public ExtensionSessionState? ExtensionSession { get; init; }
    public ExtensionInventory? Extensions { get; init; }
    public BootstrapSnapshot? Bootstrap { get; init; }
    public PreferencesSnapshot? Preferences { get; init; }
    public CustomizationResult? Customization { get; init; }
    public CustomizationExport? Export { get; init; }
    public HiddenItemsResult? HiddenItems { get; init; }
    public CatalogInspection? CatalogInspection { get; init; }
    public CatalogMaintenanceResult? CatalogMaintenance { get; init; }
    public PrivacyAuditResult? PrivacyAudit { get; init; }
    public QueryHistorySnapshot? QueryHistory { get; init; }
    public FilterAliasResult? FilterAliases { get; init; }
    public SearchSuggestionSnapshot? Suggestions { get; init; }
    public SavedSearchResult? SavedSearches { get; init; }
    public AgentBackendInventory? AgentBackends { get; init; }
    public AgentStatus? AgentStatus { get; init; }
    public AgentModelInventory? AgentModels { get; init; }
    public AgentPreferences? AgentPreferences { get; init; }
    public AgentLoginState? AgentLogin { get; init; }
    public AgentConversationSnapshot? AgentConversation { get; init; }
    public AgentStreamEvent? AgentEvent { get; init; }
    public AgentTrustState? AgentTrust { get; init; }
    public AgentTrustChallenge? AgentTrustChallenge { get; init; }
    public AgentGrantResult? AgentGrants { get; init; }
    public AgentOperationsSnapshot? AgentOperations { get; init; }
    public PackageSearchResult? PackageSearch { get; init; }
    public PackageTransactionPreview? PackagePreview { get; init; }
    public PackageCommitResult? PackageCommit { get; init; }
}

public sealed record SearchCompletion( bool Complete, int IncompleteProviderCount );
