.pragma library
// Generated from QuicklinkValidator, AliasValidator and SearchFilterRegistry. Do not edit.
var titleMessage = "Quicklink title must be 1-64 characters without controls."
var urlLengthMessage = "Quicklink URL must be 1-2048 characters without controls."
var urlMessage = "Quicklink URL must be an absolute HTTP or HTTPS URL without credentials; only {query} placeholders are supported."
var iconMessage = "Quicklink icon must be 1-256 characters without controls."
var aliasPattern = "^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$"
var aliasMessage = "Aliases must be 1-32 ASCII letters, digits, dots, underscores, or hyphens."
var reservedAliasMessage = "Alias \u0027{0}\u0027 is reserved by launcher syntax."
var reservedAliases = ["ai", "app", "ask", "browse", "cfg", "claude", "clip", "cmd", "codex", "config", "control", "controls", "ctl", "cursor", "e", "emoji", "file", "files", "grok", "hattention", "herdr", "herdr-attention", "herdr-space", "herdr-tab", "hey", "home", "hspace", "hspaces", "htab", "htabs", "kill", "link", "mail", "oma", "proc", "process", "processes", "proj", "script", "service", "settings", "snip", "svc", "system", "text", "time", "tz", "unit", "web", "win", "window", "workspace", "workspaces", "ws"]
