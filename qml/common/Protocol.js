.pragma library
// Generated from Core and ExtensionProtocol. Run scripts/generate-protocol-contract.sh; do not edit.
var Version = 3;

var Operations = Object.freeze({
  AgentBackendsGet: "agent.backends.get",
  AgentClear: "agent.clear",
  AgentContextResolve: "agent.context.resolve",
  AgentConversationGet: "agent.conversation.get",
  AgentGrantsAdd: "agent.grants.add",
  AgentGrantsClear: "agent.grants.clear",
  AgentGrantsGet: "agent.grants.get",
  AgentGrantsRemove: "agent.grants.remove",
  AgentLoginCancel: "agent.login.cancel",
  AgentLoginComplete: "agent.login.complete",
  AgentLoginStart: "agent.login.start",
  AgentLogout: "agent.logout",
  AgentModelsGet: "agent.models.get",
  AgentOperationsGet: "agent.operations.get",
  AgentOutput: "agent.output",
  AgentPreferencesGet: "agent.preferences.get",
  AgentPreferencesUpdate: "agent.preferences.update",
  AgentPrewarm: "agent.prewarm",
  AgentStatusGet: "agent.status.get",
  AgentTrustChallenge: "agent.trust.challenge",
  AgentTrustGet: "agent.trust.get",
  AgentTrustUpdate: "agent.trust.update",
  AgentTurnCancel: "agent.turn.cancel",
  AgentTurnInput: "agent.turn.input",
  AgentTurnStart: "agent.turn.start",
  AiClear: "ai.clear",
  AiModelsGet: "ai.models.get",
  AiOutput: "ai.output",
  AiPreflight: "ai.preflight",
  AiProvidersDelete: "ai.providers.delete",
  AiProvidersGet: "ai.providers.get",
  AiProvidersSave: "ai.providers.save",
  AiProvidersTest: "ai.providers.test",
  AiSelectionGet: "ai.selection.get",
  AiSelectionUpdate: "ai.selection.update",
  AiSettingsGet: "ai.settings.get",
  AiSettingsUpdate: "ai.settings.update",
  AiStart: "ai.start",
  AiTemplatesDelete: "ai.templates.delete",
  AiTemplatesGet: "ai.templates.get",
  AiTemplatesSave: "ai.templates.save",
  AliasSet: "alias.set",
  Bootstrap: "bootstrap",
  Cancel: "cancel",
  CatalogInspect: "catalog.inspect",
  ClipboardAction: "clipboard.action",
  ClipboardActionsPrepare: "clipboard.actions.prepare",
  ClipboardClose: "clipboard.close",
  ClipboardMigrationAcknowledge: "clipboard.migration.acknowledge",
  ClipboardOpen: "clipboard.open",
  ClipboardPreview: "clipboard.preview",
  ClipboardQuery: "clipboard.query",
  ClipboardReleaseAbort: "clipboard.release.abort",
  ClipboardSettingsGet: "clipboard.settings.get",
  ClipboardSettingsSet: "clipboard.settings.set",
  CustomizationsExport: "customizations.export",
  CustomizationsReset: "customizations.reset",
  DatabaseIntegrity: "database.integrity",
  DatabaseOptimize: "database.optimize",
  DatabasePurge: "database.purge",
  DatabaseRepair: "database.repair",
  ExtensionAction: "extension.action",
  ExtensionClose: "extension.close",
  ExtensionOpen: "extension.open",
  ExtensionPresent: "extension.present",
  ExtensionsManage: "extensions.manage",
  FavoriteMove: "favorite.move",
  FavoriteRemoveOrphan: "favorite.remove-orphan",
  FavoriteReorder: "favorite.reorder",
  FavoriteSet: "favorite.set",
  FilesBatch: "files.batch",
  FilesClear: "files.clear",
  FilesSelect: "files.select",
  FilterAliasSet: "filter-alias.set",
  HiddenList: "hidden.list",
  HiddenRestore: "hidden.restore",
  HiddenSet: "hidden.set",
  IndexAdd: "index.add",
  IndexDisable: "index.disable",
  IndexEnable: "index.enable",
  IndexPause: "index.pause",
  IndexRemove: "index.remove",
  IndexResume: "index.resume",
  IndexRoots: "index.roots",
  IndexRootsImport: "index.roots.import",
  Invoke: "invoke",
  LauncherOpenCancel: "launcher.open.cancel",
  PackageCancel: "package.cancel",
  PackageCommit: "package.commit",
  PackagePreview: "package.preview",
  PackageSearch: "package.search",
  Ping: "ping",
  PreferencesGet: "preferences.get",
  PreferencesUpdate: "preferences.update",
  Preview: "preview",
  PrivacyAudit: "privacy.audit",
  QueryHistoryDelete: "history.delete",
  QueryHistoryGet: "history.get",
  QueryHistoryReset: "history.reset",
  QuicklinksDisable: "quicklinks.disable",
  QuicklinksEnable: "quicklinks.enable",
  QuicklinksGet: "quicklinks.get",
  QuicklinksRemove: "quicklinks.remove",
  QuicklinksSave: "quicklinks.save",
  Reindex: "reindex",
  SavedSearchDelete: "saved-search.delete",
  SavedSearchGet: "saved-search.get",
  SavedSearchSave: "saved-search.save",
  Search: "search",
  Status: "status",
  Suggest: "suggest"
});

var Messages = Object.freeze({
  ActionResult: "action-result",
  AgentBackendsResult: "agent-backends-result",
  AgentConversationResult: "agent-conversation-result",
  AgentEvent: "agent-event",
  AgentGrantsResult: "agent-grants-result",
  AgentLoginResult: "agent-login-result",
  AgentModelsResult: "agent-models-result",
  AgentOperationsResult: "agent-operations-result",
  AgentPreferencesResult: "agent-preferences-result",
  AgentStatusResult: "agent-status-result",
  AgentTrustChallengeResult: "agent-trust-challenge-result",
  AgentTrustResult: "agent-trust-result",
  AiChunk: "ai-chunk",
  AiDone: "ai-done",
  AiModelsResult: "ai-models-result",
  AiPreflightResult: "ai-preflight-result",
  AiProviderSaveResult: "ai-provider-save-result",
  AiProviderTestResult: "ai-provider-test-result",
  AiProvidersResult: "ai-providers-result",
  AiSelectionResult: "ai-selection-result",
  AiSettingsResult: "ai-settings-result",
  AiStatus: "ai-status",
  AiTemplatesResult: "ai-templates-result",
  BootstrapResult: "bootstrap-result",
  CatalogInspection: "catalog-inspection",
  CatalogMaintenanceResult: "catalog-maintenance-result",
  ClipboardResult: "clipboard-result",
  CustomizationResult: "customization-result",
  Done: "done",
  Error: "error",
  ExportResult: "export-result",
  ExtensionSession: "extension-session",
  ExtensionsResult: "extensions-result",
  FileSelection: "file-selection",
  FilterAliasResult: "filter-alias-result",
  HiddenItemsResult: "hidden-items-result",
  IndexRootsResult: "index.roots.result",
  PackageCommitResult: "package-commit-result",
  PackagePreviewResult: "package-preview-result",
  PackageSearchResult: "package-search-result",
  Pong: "pong",
  PreferencesResult: "preferences-result",
  PreviewResult: "preview-result",
  PrivacyAuditResult: "privacy-audit-result",
  Progress: "progress",
  QueryHistoryResult: "query-history-result",
  QuicklinksResult: "quicklinks-result",
  Result: "result",
  SavedSearchResult: "saved-search-result",
  Status: "status",
  SuggestionsResult: "suggestions-result"
});

var ResultKinds = Object.freeze({
  AiCommand: "ai-command",
  Application: "application",
  Assistant: "assistant",
  Calculator: "calculator",
  Configuration: "configuration",
  CustomCommand: "custom-command",
  Emoji: "emoji",
  ExtensionCommand: "extension-command",
  File: "file",
  Mail: "mail",
  Omarchy: "omarchy",
  Process: "process",
  Quicklink: "quicklink",
  SavedSearch: "saved-search",
  Status: "status",
  Tab: "tab",
  Timezone: "timezone",
  WebSearch: "web-search",
  Window: "window",
  Workspace: "workspace"
});

var SourceIds = Object.freeze({
  Ai: "ai",
  Applications: "applications",
  Calculator: "calculator",
  Clipboard: "clipboard",
  CommandsQuicklinks: "commands-quicklinks",
  Configuration: "configuration",
  DesktopControls: "desktop-controls",
  Emoji: "emoji",
  Files: "files",
  Folders: "folders",
  Herdr: "herdr",
  Hey: "hey",
  OmarchyControl: "omarchy-control",
  OmarchySystem: "omarchy-system",
  Processes: "processes",
  Projects: "projects",
  Scripts: "scripts",
  Snippets: "snippets",
  Status: "status",
  SystemdUnits: "systemd-units",
  Timezone: "timezone",
  WebSearch: "web-search",
  WindowsWorkspaces: "windows-workspaces"
});

var ProviderIds = Object.freeze({
  Agent: "agent",
  Ai: "ai",
  Applications: "applications",
  ApplicationsFastPath: "applications-fast-path",
  Assistant: "assistant",
  Calculator: "calculator",
  Catalog: "catalog",
  Clipboard: "clipboard",
  Commands: "commands",
  Config: "config",
  DesktopControls: "desktop-controls",
  Emoji: "emoji",
  Extensions: "extensions",
  Fallback: "fallback",
  Files: "files",
  Folders: "folders",
  Herdr: "herdr",
  Hey: "hey",
  Hyprland: "hyprland",
  Omarchy: "omarchy",
  OmarchyMenu: "omarchy-menu",
  Processes: "processes",
  Projects: "projects",
  Quicklinks: "quicklinks",
  RunCommand: "run-command",
  SavedSearch: "saved-search",
  Scripts: "scripts",
  Snippets: "snippets",
  SystemdUnits: "systemd-units",
  Timezone: "timezone",
  WebSearch: "web-search"
});

var ActionKinds = Object.freeze({
  AgentOpen: "agent-open",
  AgentPrompt: "agent-prompt",
  AiPrompt: "ai-prompt",
  ApplyQuery: "apply-query",
  ApplySavedSearch: "apply-saved-search",
  BrowseFolder: "browse-folder",
  BrowseHerdrTabs: "browse-herdr-tabs",
  BrowseOmarchyMenu: "browse-omarchy-menu",
  ClipboardHistory: "clipboard-history",
  CollectFile: "collect-file",
  CopyText: "copy-text",
  EditAtLine: "edit-at-line",
  ExtensionOpen: "extension-open",
  HerdrCreateTab: "herdr-create-tab",
  HerdrFocus: "herdr-focus",
  HerdrOpenProject: "herdr-open-project",
  HerdrRenameTab: "herdr-rename-tab",
  HeyInstall: "hey-install",
  HeyOpen: "hey-open",
  HeyOpenPage: "hey-open-page",
  HeyReadThread: "hey-read-thread",
  HeySignIn: "hey-sign-in",
  HeyTriage: "hey-triage",
  HyprlandDispatch: "hyprland-dispatch",
  LaunchArgv: "launch-argv",
  LaunchDesktop: "launch-desktop",
  OpenClipboardHistory: "open-clipboard-history",
  OpenPath: "open-path",
  OpenUrl: "open-url",
  ProcessSignal: "process-signal",
  ResetRanking: "reset-ranking",
  RevealPath: "reveal-path",
  RunArgv: "run-argv",
  RunScript: "run-script",
  RunTrustedShell: "run-trusted-shell",
  SnippetCopy: "snippet-copy",
  SnippetPreview: "snippet-preview",
  SystemdUnit: "systemd-unit"
});

var SystemdUnitActionIds = Object.freeze({
  CopyUnit: "copy-unit",
  Edit: "edit",
  Logs: "logs",
  Restart: "restart",
  Start: "start",
  Status: "status",
  Stop: "stop"
});

var FileSelectionActionIds = Object.freeze({
  CopyPaths: "copy-paths",
  Open: "open"
});

var AgentLoginActionIds = Object.freeze({
  CopyCode: "copy-code",
  CopyUrl: "copy-url",
  OpenUrl: "open-url"
});

var ExtensionActionKinds = Object.freeze({
  Callback: "callback",
  CopyText: "copy-text",
  OpenUrl: "open-url"
});

var ClipboardPresentationKinds = Object.freeze({
  Files: "files",
  Images: "images",
  Links: "links",
  Text: "text"
});

var ClipboardActionIds = Object.freeze({
  Clear: "clear",
  Copy: "copy",
  Delete: "delete",
  Keywords: "keywords",
  Open: "open",
  Pin: "pin",
  SequenceStart: "sequence-start",
  Unpin: "unpin"
});

var ClipboardHandlerStatuses = Object.freeze({
  ActionsBusy: "ActionsBusy",
  CheckTimedOut: "CheckTimedOut",
  HandlerDiscoveryUnavailable: "HandlerDiscoveryUnavailable",
  InvalidAssociations: "InvalidAssociations",
  NoAssociations: "NoAssociations",
  Resolved: "Resolved"
});

var ClipboardHandlerReasons = Object.freeze({
  ExecutableUnavailable: "ExecutableUnavailable",
  UnsupportedActivation: "UnsupportedActivation",
  UnsupportedTerminalLaunch: "UnsupportedTerminalLaunch"
});

var ClipboardHandlerRanks = Object.freeze({
  Declared: "declared",
  Default: "default",
  Preferred: "preferred"
});

var ExtensionSessionStatuses = Object.freeze({
  Closed: "closed",
  Completed: "completed",
  Confirmation: "confirmation",
  Failed: "failed",
  Ready: "ready",
  Running: "running",
  Starting: "starting",
  View: "view",
  Working: "working"
});

var ExtensionFeatures = Object.freeze({
  Brokers: "brokers@1",
  Commands: "commands@1",
  CopyText: "effects.copy-text@1",
  CopyTextAction: "actions.copy-text@1",
  DetailView: "views.detail@1",
  DevelopmentDebug: "development.debug@1",
  ErrorCodes: "errors.codes@1",
  FormView: "views.form@1",
  ListView: "views.list@1",
  NoViewCommands: "commands.no-view@1",
  OpenUrlAction: "actions.open-url@1",
  Pagination: "views.pagination@1",
  PngAssets: "assets.png@1",
  RemoteSearch: "views.remote-search@1",
  Search: "search@1",
  Settings: "settings@1",
  Storage: "storage@1",
  ViewStack: "views.stack@1"
});

var ExtensionViewKinds = Object.freeze({
  Detail: "detail",
  Form: "form",
  List: "list"
});

var ExtensionFormFieldKinds = Object.freeze({
  Checkbox: "checkbox",
  Choice: "choice",
  Multiline: "multiline",
  Text: "text"
});

var ExtensionManagementActions = Object.freeze({
  Attach: "attach",
  Configure: "configure",
  ConnectionLogin: "connection-login",
  ConnectionRemove: "connection-remove",
  ConnectionSet: "connection-set",
  Connections: "connections",
  Debug: "debug",
  DebugStop: "debug-stop",
  Disable: "disable",
  EffectsReset: "effects-reset",
  Enable: "enable",
  GlobalSearch: "global-search",
  Grants: "grants",
  Inspect: "inspect",
  Install: "install",
  List: "list",
  Logs: "logs",
  Remove: "remove",
  Rollback: "rollback"
});

var ExtensionConnectionKinds = Object.freeze({
  Files: "files",
  Http: "http"
});

var ExtensionCredentialKinds = Object.freeze({
  None: "none",
  OAuth: "oauth",
  SessionBearer: "session-bearer",
  SystemdBearer: "systemd-bearer"
});

var ExtensionPermissionIds = Object.freeze({
  ClipboardWrite: "clipboard.write",
  DesktopOpenUrl: "desktop.open-url",
  FilesRead: "files.read",
  HttpRequest: "http.request",
  SecretsUse: "secrets.use",
  StorageRead: "storage.read",
  StorageWrite: "storage.write"
});

var RuntimeHealthStates = Object.freeze({
  Available: "available",
  Degraded: "degraded",
  Disabled: "disabled",
  Inactive: "inactive",
  Unavailable: "unavailable"
});

var IndexWorkStates = Object.freeze({
  Failed: "failed",
  Pending: "pending",
  Stale: "stale"
});

var QueryFieldIds = Object.freeze({
  Config: "config",
  Content: "content",
  Metadata: "metadata",
  Path: "path",
  Title: "title"
});

var RankingFieldNames = Object.freeze({
  Config: "config",
  Content: "content",
  Metadata: "metadata",
  Path: "path",
  Subtitle: "subtitle",
  Title: "title"
});

var EditorKinds = Object.freeze({
  Graphical: "graphical",
  Terminal: "terminal"
});

var HeyOpenTargets = Object.freeze({
  Terminal: "terminal",
  Web: "web"
});

var UndoActions = Object.freeze({
  Prefix: "undo:"
});

var ResultMetadataKeys = Object.freeze({
  CatalogResultId: "catalogResultId",
  CompletionText: "completionText",
  ExactAlias: "exactAlias",
  OmarchyChecked: "omarchyChecked",
  OmarchyMenu: "omarchyMenu",
  OmarchyMenuItems: "omarchyMenuItems",
  OmarchyMenuPath: "omarchyMenuPath",
  OmarchyParent: "omarchyParent",
  OmarchyUnavailable: "omarchyUnavailable"
});

var ShortcutStatuses = Object.freeze({
  Configured: "configured",
  NotConfigured: "not-configured",
  Unavailable: "unavailable"
});

var AiAdapterKinds = Object.freeze({
  Ollama: "ollama",
  OpenAiChatCompletions: "openai-chat-completions"
});

var AiCredentialKinds = Object.freeze({
  Environment: "environment",
  None: "none",
  Systemd: "systemd"
});

var AiLocality = Object.freeze({
  Local: "local",
  Remote: "remote"
});

var AiHealthStates = Object.freeze({
  Checking: "checking",
  Configured: "configured",
  Disabled: "disabled",
  Ready: "ready",
  Unavailable: "unavailable"
});

var AiSelectionStates = Object.freeze({
  Disabled: "disabled",
  Ready: "ready",
  Unavailable: "unavailable",
  Unknown: "unknown"
});

var AiAttachmentKinds = Object.freeze({
  File: "file",
  Result: "result",
  Text: "text"
});

var AiGenerationOutcomes = Object.freeze({
  Cancelled: "cancelled",
  Completed: "completed",
  Failed: "failed"
});

var AiErrorCategories = Object.freeze({
  Authentication: "authentication",
  Busy: "busy",
  Cancelled: "cancelled",
  Conflict: "conflict",
  ContextLimit: "context-limit",
  CredentialMissing: "credential-missing",
  DigestMismatch: "digest-mismatch",
  Disabled: "disabled",
  Internal: "internal",
  InvalidConfiguration: "invalid-config",
  MalformedResponse: "malformed-response",
  Network: "network",
  NotFound: "not-found",
  Permission: "permission",
  PersistenceFailed: "persistence-failed",
  PolicyDenied: "policy-denied",
  RateLimited: "rate-limited",
  RedirectRejected: "redirect-rejected",
  ResponseTooLarge: "response-too-large",
  SaveConflict: "save-conflict",
  StalePreflight: "stale-preflight",
  Timeout: "timeout",
  Unavailable: "unavailable",
  Unsupported: "unsupported",
  UnsupportedProviderOutput: "unsupported-provider-output"
});

var AiProviderSettingsSources = Object.freeze({
  LegacyConfiguration: "legacy-configuration",
  ManagedDocument: "managed-document"
});

var AiProviderTestOutcomes = Object.freeze({
  Empty: "empty",
  Failed: "failed",
  Models: "models",
  Unsupported: "unsupported"
});

var AgentBackendIds = Object.freeze({
  ClaudeCode: "claude.local",
  Codex: "codex.local",
  Cursor: "cursor.local",
  Grok: "grok.local"
});

var AgentBackendKinds = Object.freeze({
  AnswerOnly: "answer-only",
  ClaudeCodeCli: "claude-code-cli",
  CodexAppServer: "codex-app-server",
  CursorCli: "cursor-cli",
  GrokCli: "grok-cli"
});

var AgentTrustDurations = Object.freeze({
  OneHour: "one-hour",
  UntilDowngrade: "until-downgrade"
});

var AgentContextPolicies = Object.freeze({
  ApprovedFolders: "approved-folders",
  AskEveryAccess: "ask-every-access"
});

var AgentAuthenticationStates = Object.freeze({
  Authenticated: "authenticated",
  Failed: "failed",
  NotApplicable: "not-applicable",
  Pending: "pending",
  Unauthenticated: "unauthenticated"
});

var AgentHealthStates = Object.freeze({
  Configured: "configured",
  Disabled: "disabled",
  Incompatible: "incompatible",
  Ready: "ready",
  Starting: "starting",
  Unavailable: "unavailable"
});

var AgentLoginMethods = Object.freeze({
  ApiKey: "api-key",
  Browser: "browser",
  DeviceCode: "device-code"
});

var AgentConversationStates = Object.freeze({
  Cancelled: "cancelled",
  Completed: "completed",
  Failed: "failed",
  Idle: "idle",
  Running: "running",
  Waiting: "waiting"
});

var AgentTurnOutcomes = Object.freeze({
  Cancelled: "cancelled",
  Completed: "completed",
  Failed: "failed"
});

var AgentEventKinds = Object.freeze({
  Done: "done",
  Reasoning: "reasoning",
  Request: "request",
  Status: "status",
  Text: "text",
  Tool: "tool",
  Usage: "usage"
});

var AgentPendingRequestKinds = Object.freeze({
  Action: "action",
  Command: "command",
  Context: "context",
  Question: "question"
});

var AgentRequestDecisions = Object.freeze({
  Allow: "allow",
  AllowAlways: "allow-always",
  AllowFolder: "allow-folder",
  CancelTurn: "cancel-turn",
  Deny: "deny"
});

var AgentGrantStates = Object.freeze({
  Active: "active",
  Revoked: "revoked",
  Stale: "stale"
});

var AgentTranscriptRoles = Object.freeze({
  Assistant: "assistant",
  User: "user"
});

var AgentPreferenceFields = Object.freeze({
  AurEnabled: "aurEnabled",
  BackendId: "backendId",
  ContextPolicy: "contextPolicy",
  ConversationTtlMinutes: "conversationTtlMinutes",
  Enabled: "enabled",
  LoopbackPreflightProviderId: "loopbackPreflightProviderId",
  ModelId: "modelId",
  ReasoningEffort: "reasoningEffort",
  ShowHiddenModels: "showHiddenModels",
  TrustDuration: "trustDuration",
  TrustLeaseExpiresUnixMilliseconds: "trustLeaseExpiresUnixMilliseconds",
  TrustLevel: "trustLevel",
  TurnTimeoutMinutes: "turnTimeoutMinutes",
  UseCodexConfiguration: "useCodexConfiguration",
  WebSearchEnabled: "webSearchEnabled"
});

var SourcePreferenceFields = Object.freeze({
  EnabledInSearch: "enabledInSearch",
  EnabledOnHome: "enabledOnHome",
  Position: "position",
  SourceId: "sourceId"
});
