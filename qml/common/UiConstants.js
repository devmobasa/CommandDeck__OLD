.pragma library

// UI-owned identifiers only. Wire/domain values belong in generated Protocol.js.
// Keep groups semantic: equal spellings do not imply the same contract.
var AiState = Object.freeze({
  Idle: "idle", Preflighting: "preflighting", Preflight: "preflight",
  Streaming: "streaming", Completed: "completed", Cancelled: "cancelled",
  Failed: "failed", ConnectionLost: "connection-lost"
});
var AgentState = Object.freeze({ Starting: "starting", ConnectionLost: "connection-lost" });
var ProviderSettingsState = Object.freeze({
  Idle: "idle", Loading: "loading", Saving: "saving", Saved: "saved",
  SavedSelectionFailed: "saved-selection-failed", Conflict: "conflict",
  Failed: "failed", Unsupported: "unsupported", ConnectionLost: "connection-lost"
});
var ButtonVariant = Object.freeze({ Primary: "primary", Secondary: "secondary", Danger: "danger" });
var DisplaySize = Object.freeze({ Auto: "auto", Small: "small", Default: "default", Large: "large", ExtraLarge: "xlarge" });
var SettingsPage = Object.freeze({
  Search: "search", Assistant: "assistant", Editors: "editors", Filters: "filters",
  Ai: "ai", Shortcuts: "shortcuts", Customization: "customization"
});
var OutputAction = Object.freeze({ Copy: "copy", Save: "save", Open: "open" });
var ClipboardDialogPurpose = Object.freeze({
  Keywords: "keywords", Clear: "clear", Open: "open", Enable: "enable", Migration: "migration"
});
var ClipboardPendingKey = Object.freeze({
  Open: "open", Close: "close", Query: "query", More: "more", Preview: "preview",
  Action: "action", Prepare: "prepare", Abort: "abort", Copy: "copy", Clear: "clear",
  Settings: "settings", SettingsSet: "settingsSet", Migration: "migration", Sequence: "sequence"
});
var ExtensionManagementPage = Object.freeze({
  List: "list", Detail: "detail", Confirm: "confirm", Logs: "logs",
  Connections: "connections", Settings: "settings", Grants: "grants",
  Archive: "archive", ArchiveReview: "archive-review"
});
var ExtensionSessionPurpose = Object.freeze({ DisableList: "disable-list", DisableSave: "disable-save" });
var WorkspaceIdentityKind = Object.freeze({ Ordinary: "ordinary", Special: "special" });
var ResultAction = Object.freeze({
  WhyResult: "why-result", RankingExplanation: "ranking-explanation", Favorite: "favorite", Alias: "alias",
  FavoriteToggle: "favorite-toggle", AliasEdit: "alias-edit", Hide: "hide", HideResult: "hide-result"
});
var SearchShortcut = Object.freeze({ Reveal: "reveal", CopyPath: "copy-path", Favorite: "favorite" });
var ShortcutSlot = Object.freeze({
  Secondary: "secondary", Tertiary: "tertiary", Copy: "copy", Favorite: "favorite", Alias: "alias", Hide: "hide",
  ResetRanking: "reset-ranking"
});
var MutationKind = Object.freeze({
  Preferences: "preferences", Favorite: "favorite", Alias: "alias", FilterAlias: "filter-alias",
  SavedSearch: "saved-search", Reorder: "reorder", Orphan: "orphan", Reset: "reset", Export: "export",
  Hidden: "hidden"
});
var FilterTokenKind = Object.freeze({ Prefix: "prefix", Calculator: "calculator" });
var ReservedShortcut = Object.freeze({ Home: "home", Settings: "settings" });
var FocusReturnControl = Object.freeze({ FooterHelp: "footer-help", FooterPrefixes: "footer-prefixes" });
var ReasoningEffort = Object.freeze({ Low: "low", Medium: "medium", High: "high", ExtraHigh: "xhigh", Max: "max", Ultra: "ultra" });
var QueryPrefix = Object.freeze({
  App: "app:", Apps: "apps:", File: "file:", Files: "files:", Text: "text:",
  Service: "svc:", ServiceLong: "service:", Services: "services:",
  Ai: "ai:", Ask: "ask:", Codex: "codex:", Claude: "claude:", Cursor: "cursor:", Grok: "grok:",
  Configuration: "cfg:", Config: "config:", Win: "win:", Window: "window:", Windows: "windows:",
  Workspace: "workspace:", Workspaces: "workspaces:", Ws: "ws:",
  Projects: "proj:", Snippets: "snip:", Scripts: "script:", Clipboard: "clip:", Browse: "browse:",
  Omarchy: "oma:", HerdrTab: "htab:", Command: "cmd:", Quicklink: "link:", Unit: "unit:",
  Timezone: "tz:", Time: "time:", Web: "web:", Emoji: "emoji:", EmojiShort: "e:",
  Calculator: "="
});
// Launcher colours: the active Omarchy theme, or CommandDeck's own palette.
var ColorThemeSource = Object.freeze({ Omarchy: "omarchy", CommandDeck: "commanddeck" });
var DeckColorRole = Object.freeze({ Accent: "accent", Background: "background", Text: "text" });
// Swatches offered for each palette role; the first of each is the default.
var DeckColorPresets = Object.freeze({
  Accent: Object.freeze([
    Object.freeze({ name: "Blue", value: "#8ab4f8" }), Object.freeze({ name: "Violet", value: "#b4a1f5" }),
    Object.freeze({ name: "Teal", value: "#6fd3c4" }), Object.freeze({ name: "Green", value: "#a3d977" }),
    Object.freeze({ name: "Amber", value: "#f5c26b" }), Object.freeze({ name: "Rose", value: "#f4899b" })
  ]),
  Background: Object.freeze([
    Object.freeze({ name: "Carbon", value: "#0e0e11" }), Object.freeze({ name: "Graphite", value: "#1c1d22" }),
    Object.freeze({ name: "Navy", value: "#0f1626" }), Object.freeze({ name: "Pine", value: "#0f1a17" })
  ]),
  Text: Object.freeze([
    Object.freeze({ name: "Snow", value: "#f1f1f1" }), Object.freeze({ name: "Warm", value: "#ede6da" }),
    Object.freeze({ name: "Cool", value: "#e2e8f2" })
  ])
});
var DeckColors = Object.freeze({
  Accent: DeckColorPresets.Accent[0].value,
  Background: DeckColorPresets.Background[0].value,
  Text: DeckColorPresets.Text[0].value
});
// Settings → Search sources: the keyboard grid's columns, left to right.
var SourceGridColumn = Object.freeze({ Search: 0, Home: 1, MoveUp: 2, MoveDown: 3 });
