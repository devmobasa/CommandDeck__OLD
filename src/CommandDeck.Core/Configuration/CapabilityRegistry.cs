namespace CommandDeck;

public sealed record CapabilityDescriptor(
    string Name,
    string DefaultState,
    string DataRead,
    string DataWrittenOrTransmitted,
    string UserVisibleTrigger,
    string Control );

public static class CapabilityRegistry
{
    public static IReadOnlyList<CapabilityDescriptor> All { get; } =
    [
        new("Application discovery", "On", "XDG `.desktop` entries", "Catalog metadata", "Search", "Disable provider"),
        new("Omarchy commands", "On", "Omarchy menu JSONC", "Catalog metadata", "Search/action", "Disable provider"),
        new("Filename indexing", "On for effective roots", "Names, paths, stat data", "SQLite catalog and indexed folder preferences", "Background reindex; Settings > Search > Indexed folders or index CLI", "Remove added roots; disable/enable configured roots; privacy exclusions always apply"),
        new("Text content indexing", "Configurable", "Eligible local text", "FTS5 body", "Background reindex", "Content mode/root/exclusions"),
        new("Index privacy audit", "On demand", "Stored catalog paths and path-only exclusion diagnostics", "Aggregate counts and generic messages only", "`commanddeck catalog audit` or `privacy.audit`", "No offending paths/content returned; fix policy then reindex"),
        new("PDF/document extraction", "Off unless tool/config enabled", "Selected documents", "Extracted text to FTS", "Background reindex", "Disable extractor/type"),
        new("Structured config indexing", "On for selected roots", "Static keys/values", "Config records/FTS", "Background reindex", "Root/exclusions"),
        new("Hyprland inspection", "On", "Windows/workspaces/monitors", "None except transient results", "Search", "Disable provider"),
        new("Hyprland actions", "On", "Current compositor state", "Hyprland IPC action", "Explicit Enter/action", "Disable provider/action"),
        new("Window commands", "On", "Hyprland's active window and monitor layout when a window command matches the search", "Hyprland IPC float, resize, move, fullscreen, monitor, or workspace dispatches to the window focused before the launcher opened, by its literal address", "Explicit Enter/action on a matching search row or `ctl:`", "Disable the Desktop controls source"),
        new("Process signals", "On", "Name, state, start time, owner, memory, and CPU time of the user's own processes from /proc, only for a `kill:` search", "SIGTERM or SIGKILL through a pidfd to the chosen process after its PID, start time, and owner are checked again", "Confirmed Terminate or Force kill naming the PID and process", "Cancel the confirmation; disable the Processes source"),
        new("Color picker", "On when hyprpicker is installed", "Screen pixels, inside hyprpicker after the launcher closes", "The picked color to the clipboard through hyprpicker --autocopy", "Explicit Enter on Pick Color; a 300 ms user timer starts hyprpicker", "Escape in hyprpicker; disable the Desktop controls source"),
        new("Desktop controls", "On", "Whether wpctl, playerctl, and rfkill are on PATH", "Literal wpctl, playerctl, or rfkill argv for the default audio sink/source, the active media player, or the Wi-Fi/Bluetooth soft block", "Explicit Enter/action on a matching search row or `ctl:`", "Disable the Desktop controls source"),
        new("Calculator", "On", "Query expression", "None for arithmetic and units; optional qalc exchange-rate fetch on currency expressions at most once per 15 minutes", "`=` prefix", "Disable provider; `search.calculator.useQalc=never`"),
        new("Quicklinks", "On", "User config/query", "URL to browser", "Explicit action", "Remove quicklink"),
        new("Custom commands", "Configurable", "User command config", "Executes configured argv", "Explicit action", "Remove/disable command"),
        new("Usage frequency", "On", "Selected result/action", "SQLite counters/timestamps", "Successful invocation", "Disable/reset"),
        new("Context ranking", "On", "Bounded active app/workspace and explicit directory/project", "Transient score contribution only", "Search", "Disable one context switch"),
        new("Query history", "Off", "Search query", "Bounded query_history rows", "Successful non-copy invocation", "Opt in/inspect/export/delete/reset"),
        new("Local suggestions API", "Explicit CLI/socket request only", "Current query, local catalog titles, opted-in query history", "Response of at most eight suggestions", "`suggest` request; not requested by launcher typing", "Disabling query history removes history-derived text"),
        new("Saved searches", "On", "Typed query expression and effective shortcut inventory", "Bounded SQLite saved_searches rows", "Explicit Settings, Home, shortcut, or CLI action", "Rename, remove, hide from Home, clear shortcut, or reset; conflicts are rejected"),
        new("File and configuration actions", "On", "Selected indexed path, desktop handler metadata, exact key/value location", "Literal argv to selected handler/editor or exact text to wl-copy", "Explicit action-panel command", "Configure editor adapters; action payloads are daemon-authored and never shell strings"),
        new("Personalization snapshot", "On", "Source preferences, bookmarks, aliases, effective shortcuts", "None", "Open Settings", "Close Settings; shortcut inspection is read-only"),
        new("Source/home preferences", "On", "Current complete preference snapshot", "SQLite preference rows", "Toggle, reorder, fallback, or motion edit", "Reverse edit or reset; rejected writes roll back/reload"),
        new("Bookmarks and aliases", "On", "Fresh daemon-cached result identity", "SQLite favorite/alias rows", "Explicit action-panel command", "Remove bookmark/alias; forged or stale result IDs rejected"),
        new("Filter aliases", "On", "Validated token and stable filter ID", "Bounded SQLite filter_aliases rows", "Explicit Settings or CLI command", "Remove alias or reset; built-in/reserved/conflicting tokens are rejected"),
        new("Favorite ordering/orphan cleanup", "On", "Existing customization IDs", "SQLite favorite positions or orphan deletion", "Explicit Settings action", "Reorder again; orphan deletion targets unavailable entries only"),
        new("Customization export", "On demand", "Preferences, bookmarks, result aliases, filter aliases, indexed folder additions and disabled configuration paths", "Local protocol response rendered in the export dialog", "Export button", "No network transmission; dismiss dialog"),
        new("Customization reset", "On demand", "Existing personalization rows", "Deletes personalization rows and restores defaults", "Confirmed reset action", "Confirmation required; exported data is not auto-restored"),
        new("AI model discovery", "Off until configured", "Provider model catalog; named credential at explicit use", "Configured local/remote endpoint", "Refresh Models", "Disable provider; no passive polling"),
        new("AI preflight and streaming", "Off until configured", "Exact explicit prompt and up to four reviewed text/result/file attachments", "Digest-bound payload to the displayed destination", "Confirm preflight", "Cancel/close/disconnect; disable provider"),
        new("AI selection and templates", "On when AI is configured", "Stable provider/model IDs and bounded template definitions", "SQLite selection/template rows", "Settings or CLI save/delete", "Clear selection, delete template, or customization reset"),
        new("AI output actions", "On after completed generation", "Ephemeral generated text", "Clipboard or atomic mode-0600 user-selected file; open only saved file", "Explicit Copy/Save/Open", "Output vanishes on restart; overwrite confirmation required"),
        new("Assistant conversation", "Off until enabled", "Typed prompt, streamed answer, reasoning summaries", "Codex App Server in a Bubblewrap sandbox; inference through the CommandDeck-owned OpenAI login", "`ai:` or `codex:` query, or a Home row", "Disable assistant, Clear, or Cancel; nothing persists"),
        new("Assistant login", "Off until enabled", "Device code or named API key credential", "Mode-0600 CommandDeck-owned credential file; the user's Codex CLI login is never read", "Inline login card", "Sign out deletes the owned credential"),
        new("Assistant context grants", "Level 2 default, approved folders", "Text files under approved folders or, at level 3, under home except protected paths", "Bounded file text to the selected model after a decision card", "Assistant tool request", "Deny, revoke a grant, lower the trust level, or disable"),
        new("Assistant launcher actions", "Level 1 and above", "Cached launcher results and their actions", "Existing typed actions; launch, focus, and copy run at once, others confirm", "Assistant tool request", "Deny on the card or lower the trust level"),
        new("Assistant confirmed commands", "Off (level 5)", "Exact argv, executable identity, working directory", "Literal argv run as the user outside Codex with bounded output returned to the model", "Confirmation card", "Deny, revoke a remembered command, or lower the trust level"),
        new("Assistant unrestricted mode", "Off (level 6)", "The full user session", "Codex full-access profile in a transient user unit", "Typed phrase and duration in Settings", "Downgrade, lease expiry, disable, or reset"),
        new("Official package changes", "Off (level 4) and requires the installed helper", "Local pacman databases", "Polkit-authorized helper runs literal pacman argv after an exact preview", "Confirmation card and Polkit prompt", "Deny, cancel before pacman locks, or uninstall the helper"),
        new("Network generally", "Off in core search", "None", "None", "Provider-specific only", "Provider config"),
        new("Telemetry", "Off/nonexistent", "None", "None", "None", "Not implemented"),
        new("Clipboard history", "Off until explicit opt-in", "Wayland clipboard UTF-8 text up to 32 KiB; sensitive hints are skipped", "Mode-0600 SQLite history beside the catalog", "Enable capture in clip: action menu", "Pause capture, pin/unpin, delete, clear, or configure retention/capacity"),
        new("Third-party extensions", "Not in v1", "—", "—", "—", "Deferred")
    ];
}
