.pragma library
.import "components/Protocol.js" as Protocol
.import "components/QuicklinkValidation.js" as QuicklinkValidation

// Demonstration results in the shapes the daemon's providers send. Paths sit
// under /home/demo; window addresses, Herdr IDs, and file contents are invented.
// Each collection takes the stage's icon(name) resolver; scripts/capture-gallery.cs
// finds the icon("…") calls in this file to resolve their theme files.

var K = Protocol.ResultKinds
var A = Protocol.ActionKinds
var hyprConfig = "/home/demo/.config/hypr/looknfeel.lua"

function indexRoots() {
  return [
    { id: "demo-documents", path: "/home/demo/Documents", source: "configuration",
      enabled: true, status: "Indexed", issue: "" },
    { id: "demo-projects", path: "/home/demo/Projects", source: "added",
      enabled: true, status: "Indexed", issue: "" },
    { id: "demo-archive", path: "/home/demo/Archive", source: "configuration",
      enabled: true, status: "Needs attention", issue: "Folder does not exist." },
    { id: "demo-downloads", path: "/home/demo/Downloads", source: "configuration",
      enabled: false, status: "Disabled", issue: null }
  ]
}

function quicklinks() {
  const entries = [
    { source: "configuration", enabled: true,
      quicklink: { id: "demo-search", title: "Web search", alias: "find", urlTemplate: "https://example.org/search?q={query}" } },
    { source: "user", enabled: true,
      quicklink: { id: "demo-docs", title: "Team docs", alias: "docs", icon: "text-markdown",
        urlTemplate: "https://docs.example.org/search?q={query}" } },
    { source: "user", enabled: true,
      quicklink: { id: "demo-project", title: "Project board", alias: "board", urlTemplate: "https://projects.example.org/board" } }
  ]

  for (const entry of entries) {
    const error = QuicklinkValidation.validate(entry.quicklink, entries)
    if (error) {
      throw new Error("Invalid gallery quicklink: " + error)
    }
  }

  return entries
}

// Prepared ranking responses illustrate a learned abbreviation, without a daemon.
function learnedFirefox(icon, query) {
  const placement = section("applications", "Applications", 6)
  const firefox = app(icon("firefox"), "firefox", "Firefox", "Browse the web", "firefox %u",
    query === "ff" ? section("top-match", "Top Matches", 0) : placement)
  const files = app(icon("org.gnome.Nautilus"), "org.gnome.Nautilus", "Files", "Browse folders and files", "nautilus %U", placement)
  return query === "ff" ? [firefox] : [files, firefox]
}

function section(id, title, order) {
  return { id: id, title: title, order: order }
}

function app(icon, id, title, subtitle, command, placement) {
  const desktopFile = "/usr/share/applications/" + id + ".desktop"
  return {
    id: "app:" + id + ".desktop", kind: K.Application, sourceId: "applications",
    providerId: "applications", title: title, subtitle: subtitle, icon: icon,
    canCustomize: true, section: placement,
    details: [
      { label: "Desktop file", value: desktopFile, monospace: true },
      { label: "Command", value: command, monospace: true },
      { label: "Terminal", value: "No" }
    ],
    actions: [{ id: "launch", title: "Launch", kind: A.LaunchDesktop, arguments: {} }]
  }
}

var fileTypes = { md: "Markdown document", lua: "Lua source", txt: "Plain text" }

function file(icon, id, title, folder, placement, markdown) {
  const extension = title.slice(title.lastIndexOf(".") + 1)
  return {
    id: "file:" + id, kind: K.File, sourceId: "files", title: title,
    subtitle: folder, icon: icon, canCustomize: true, section: placement,
    details: [{ label: "Folder", value: folder }, { label: "Type", value: fileTypes[extension] || "Document" }],
    detailMarkdown: markdown || "",
    actions: [
      { id: "open", title: "Open", kind: A.OpenPath, arguments: {} },
      { id: "reveal", title: "Reveal in File Manager", kind: A.RevealPath, arguments: {} }
    ]
  }
}

function config(icon, block, key, value, line, placement) {
  const valueColumn = 5 + key.length + 3
  return {
    id: "cfg:" + block + "." + key, kind: K.Configuration, sourceId: "configuration",
    providerId: "config", icon: icon, canCustomize: true, section: placement,
    title: block + "." + key,
    subtitle: hyprConfig + ":" + line + ":5 · " + value,
    details: [
      { label: "File", value: hyprConfig, monospace: true },
      { label: "Key location", value: line + ":5", monospace: true },
      { label: "Value location", value: line + ":" + valueColumn, monospace: true },
      { label: "Key", value: key, monospace: true },
      { label: "Section", value: block, monospace: true },
      { label: "Value", value: value }
    ],
    detailMarkdown: "```lua\n" + block + " = {\n  " + key + " = " + value + ",\n}\n```",
    actions: [
      { id: "edit", title: "Edit at Key", kind: A.EditAtLine, arguments: {} },
      { id: "copy-value", title: "Copy Value", kind: A.CopyText, arguments: { text: value } },
      { id: "copy-key", title: "Copy Key", kind: A.CopyText, arguments: { text: key } },
      { id: "copy-assignment", title: "Copy Assignment", kind: A.CopyText,
        arguments: { text: key + " = " + value } },
      { id: "reveal", title: "Reveal", kind: A.RevealPath, arguments: {} }
    ]
  }
}

function window(icon, address, title, windowClass, workspace, placement) {
  return {
    id: "window:" + address, kind: K.Window, sourceId: "windows-workspaces",
    providerId: "hyprland", title: title, subtitle: windowClass + " · workspace " + workspace,
    icon: icon, section: placement,
    details: [
      { label: "Class", value: windowClass, monospace: true },
      { label: "Workspace", value: String(workspace) },
      { label: "Address", value: address, monospace: true }
    ],
    actions: [
      { id: "focus", title: "Focus Window", kind: A.HyprlandDispatch, arguments: {} },
      { id: "close", title: "Close Window", kind: A.HyprlandDispatch, arguments: {},
        isDestructive: true, requiresConfirmation: true }
    ]
  }
}

function workspace(icon, number, windows, placement) {
  return {
    id: "workspace:" + number, kind: K.Workspace, sourceId: "windows-workspaces",
    providerId: "hyprland", title: "Workspace " + number,
    subtitle: windows + " windows · DP-1", icon: icon, section: placement,
    details: [{ label: "Monitor", value: "DP-1", monospace: true }, { label: "Windows", value: String(windows) }],
    actions: [{ id: "switch", title: "Switch Workspace", kind: A.HyprlandDispatch, arguments: {} }]
  }
}

function omarchy(icon, id, title, parents, command, placement) {
  return {
    id: "omarchy:" + id, kind: K.Omarchy, sourceId: "omarchy-control", providerId: "omarchy",
    icon: icon, canCustomize: true, title: title, subtitle: parents, section: placement,
    details: [
      { label: "Command", value: command, monospace: true },
      { label: "Source", value: "/home/demo/.local/share/omarchy/default/omarchy/omarchy-menu.jsonc",
        monospace: true }
    ],
    actions: [{ id: "run", title: "Run", kind: A.RunTrustedShell, arguments: {} }]
  }
}

// A submenu row: Enter lists its entries in the launcher under its path.
function omarchyMenu(icon, id, title, parents, items, placement) {
  const path = parents + " › " + title
  return {
    id: "omarchy:" + id, kind: K.Omarchy, sourceId: "omarchy-control", providerId: "omarchy",
    icon: icon, canCustomize: true, title: title, subtitle: parents, section: placement,
    details: [
      { label: "Menu", value: path },
      { label: "Entries", value: String(items) },
      { label: "Source", value: "/home/demo/.local/share/omarchy/default/omarchy/omarchy-menu.jsonc",
        monospace: true }
    ],
    metadata: { omarchyMenu: id, omarchyMenuPath: path, omarchyMenuItems: String(items) },
    actions: [{ id: "open-submenu", title: "Open Submenu", kind: A.BrowseOmarchyMenu,
      arguments: { omarchyMenu: id, omarchyMenuPath: path } }]
  }
}

function herdrTab(icon, id, title, space, number, panes, status, focused) {
  return {
    id: "herdr:tab:" + id, kind: K.Tab, sourceId: "herdr", providerId: "herdr", icon: icon,
    title: title,
    subtitle: "In " + space + " · Tab " + number + " · " + panes + " panes" + (focused ? " · focused" : ""),
    details: [
      { label: "Space", value: space }, { label: "Number", value: String(number) },
      { label: "Panes", value: String(panes) }, { label: "Agent status", value: status },
      { label: "Herdr ID", value: id, monospace: true }
    ],
    metadata: {
      herdrTargetKind: "tab", herdrTargetId: id, herdrWorkspaceId: "w1",
      herdrWorkspaceLabel: space, herdrFocused: focused ? "true" : "false", herdrAgentStatus: status
    },
    actions: [
      { id: "switch-tab", title: "Switch Herdr Tab", kind: A.HerdrFocus, arguments: {} },
      { id: "rename-tab", title: "Rename tab", kind: A.HerdrRenameTab, arguments: {} }
    ]
  }
}

var themeCommand = "theme=$(omarchy-theme-switcher); [[ -n $theme ]] && omarchy-theme-set \"$theme\""

var planningNotes = "## Planning notes\n\nA small example document for the next project.\n\n"
  + "- Sketch the first screen\n- Write down the open questions\n- Share a short draft"

// Bare Home: bookmarks, recent and frequent items, open windows, and Omarchy.
function home(icon) {
  const bookmarks = section("bookmarks", "Bookmarks", 1)
  const recent = section("recent-frequent", "Recent & Frequent", 2)
  const windows = section("windows-workspaces", "Windows & Workspaces", 3)
  const menu = section("omarchy", "Omarchy", 4)
  return [
    app(icon("chromium"), "chromium", "Chromium", "Access the Internet", "chromium %U", bookmarks),
    file(icon("text-markdown"), "planning", "Planning notes.md", "~/Documents/Notes", recent, planningNotes),
    config(icon("preferences-system"), "general", "gaps_out", "10", 14, recent),
    app(icon("obsidian"), "obsidian", "Obsidian", "Knowledge base", "obsidian %u", recent),
    window(icon("com.mitchellh.ghostty"), "0x5a1c2e0", "README.md — nvim", "com.mitchellh.ghostty", 2, windows),
    window(icon("spotify"), "0x5a1d410", "Spotify Premium", "spotify", 9, windows),
    omarchy(icon("omarchy"), "style.theme", "Theme", "Style", themeCommand, menu)
  ]
}

// Typed-search candidates grouped by source, in default source order.
function corpus(icon) {
  const control = section("omarchy-control", "Omarchy Control", 5)
  const applications = section("applications", "Applications", 6)
  const files = section("files", "Files", 7)
  const configuration = section("configuration", "Configuration", 11)
  const omarchyIcon = icon("omarchy")
  const configIcon = icon("preferences-system")
  return [
    omarchy(omarchyIcon, "trigger.toggle.window-gaps", "Window Gaps", "Trigger › Toggle",
      "omarchy-hyprland-window-gaps-toggle", control),
    omarchy(omarchyIcon, "style.background", "Background", "Style",
      "background=$(omarchy-theme-bg-switcher); [[ -n $background ]] && omarchy-theme-bg-set \"$background\"", control),
    omarchy(omarchyIcon, "trigger.toggle.nightlight", "Nightlight", "Trigger › Toggle",
      "omarchy-toggle-nightlight", control),
    omarchyMenu(omarchyIcon, "setup.network.dns", "DNS", "Setup › Network", 4, control),
    app(icon("signal-desktop"), "signal-desktop", "Signal", "Private messenger", "signal-desktop %U", applications),
    app(icon("chromium"), "chromium", "Chromium", "Access the Internet", "chromium %U", applications),
    file(icon("text-x-generic"), "window-no-gaps", "window-no-gaps.lua",
      "~/.local/share/omarchy/default/hypr/toggles", files,
      "```lua\ngeneral = {\n  gaps_in = 0,\n  gaps_out = 0,\n  border_size = 0,\n}\n```"),
    file(icon("text-markdown"), "planning", "Planning notes.md", "~/Documents/Notes", files, planningNotes),
    config(configIcon, "general", "gaps_out", "10", 14, configuration),
    config(configIcon, "general", "gaps_in", "5", 13, configuration),
    config(configIcon, "general", "border_size", "2", 15, configuration)
  ]
}

// Candidates whose title contains every typed word, as a stand-in for ranking.
function search(icon, query) {
  const words = query.toLowerCase().split(/\s+/).filter(function(word) { return word.length > 0 })
  return corpus(icon).filter(function(row) {
    const title = row.title.toLowerCase()
    return words.every(function(word) { return title.indexOf(word) >= 0 })
  })
}

// `gaps`: an Omarchy toggle, the file behind it, and the Hyprland keys.
function gaps(icon) {
  return search(icon, "gaps")
}

function windows(icon) {
  const placement = section("windows-workspaces", "Windows & Workspaces", 8)
  return [
    window(icon("com.mitchellh.ghostty"), "0x5a1c2e0", "README.md — nvim", "com.mitchellh.ghostty", 2, placement),
    window(icon("com.mitchellh.ghostty"), "0x5a1c9f0", "dotnet watch — ~/projects/taskboard",
      "com.mitchellh.ghostty", 2, placement),
    window(icon("chromium"), "0x5a1b770", "Pull request #42 — Chromium", "chromium", 1, placement),
    window(icon("signal-desktop"), "0x5a1e120", "Signal", "signal", 4, placement),
    window(icon("spotify"), "0x5a1d410", "Spotify Premium", "spotify", 9, placement),
    workspace(icon("view-grid"), 2, 3, placement)
  ]
}

function omarchyControls(icon) {
  const placement = section("omarchy-control", "Omarchy Control", 5)
  return [
    omarchy(icon("omarchy"), "style.theme", "Theme", "Style", themeCommand, placement),
    omarchy(icon("omarchy"), "style.background", "Background", "Style",
      "background=$(omarchy-theme-bg-switcher); [[ -n $background ]] && omarchy-theme-bg-set \"$background\"", placement),
    omarchy(icon("omarchy"), "trigger.toggle.window-gaps", "Window Gaps", "Trigger › Toggle",
      "omarchy-hyprland-window-gaps-toggle", placement),
    omarchy(icon("omarchy"), "trigger.toggle.nightlight", "Nightlight", "Trigger › Toggle",
      "omarchy-toggle-nightlight", placement),
    omarchy(icon("omarchy"), "learn.keybindings", "Keybindings", "Learn", "omarchy-menu-keybindings", placement),
    omarchy(icon("omarchy"), "style.menu-bar.transparency", "Transparency", "Style › Menu Bar",
      "omarchy-bar transparent toggle", placement)
  ]
}

// The DNS submenu's entries; the provider in use carries Omarchy's checked state.
function dnsMenu(icon) {
  const placement = section("omarchy-control", "Omarchy Control", 5)
  const omarchyIcon = icon("omarchy")
  return ["DHCP", "Cloudflare", "Google", "Custom"].map(function(name) {
    const row = omarchy(omarchyIcon, "setup.network.dns." + name.toLowerCase(), name, "Setup › Network › DNS",
      "omarchy-dns " + name, placement)
    row.metadata = { omarchyChecked: name === "Cloudflare" ? "true" : "false" }
    return row
  })
}

// Snippets whose title contains `query`. Inputs are the template's form fields,
// which the launcher shows as pills in the search bar.
function snippets(icon, query) {
  const placement = section("snippets", "Snippets", 6)
  const snippet = function(id, title, text, inputs) {
    const action = function(kind, actionTitle) {
      return { id: kind, title: actionTitle, kind: kind, arguments: {}, inputs: inputs }
    }
    return {
      id: "snippet:" + id, kind: K.CustomCommand, sourceId: "snippets", providerId: "snippets",
      title: title, subtitle: "Snippet · copy or preview with inputs", icon: icon("edit-paste"),
      section: placement, detailMarkdown: text,
      actions: [action(A.SnippetCopy, "Copy snippet"), action(A.SnippetPreview, "Preview expanded snippet")]
    }
  }
  return [
    snippet("follow-up", "Follow-up email",
      "Hi {name},\n\nThanks for the call today. I'll send the {topic default=draft} by Friday.",
      [{ name: "name" }, { name: "topic", defaultValue: "draft" }]),
    snippet("release", "Release announcement", "Release {version}\n\nWhat's new:\n{highlights}",
      [{ name: "version" }, { name: "highlights" }]),
    snippet("late", "Running late", "Running {minutes default=\"10\"} minutes late, start without me.",
      [{ name: "minutes", defaultValue: "10" }])
  ].filter(function(row) {
    return row.title.toLowerCase().indexOf(query.toLowerCase()) >= 0
  })
}

function herdrTabs(icon) {
  const terminal = icon("utilities-terminal")
  return [
    herdrTab(terminal, "t7", "api refactor", "taskboard", 2, 3, "blocked", false),
    herdrTab(terminal, "t3", "search latency", "taskboard", 1, 2, "working", true),
    herdrTab(terminal, "t9", "docs pass", "taskboard", 3, 1, "working", false),
    herdrTab(terminal, "t4", "release notes", "website", 1, 2, "idle", false)
  ]
}

function timezone(icon) {
  return [{
    id: "timezone:Europe/London->Asia/Tokyo", kind: K.Timezone, sourceId: "timezone",
    icon: icon("preferences-system-time"), section: section("timezone", "Timezone", 15),
    title: "London 3:00 PM → Tokyo 11:00 PM", subtitle: "London UTC+01:00 DST · Tokyo UTC+09:00",
    detailMarkdown: "**London → Tokyo**\n\n- London: 3:00 PM · UTC+01:00 · DST\n- Tokyo: 11:00 PM · UTC+09:00\n",
    actions: [{ id: "copy-time", title: "Copy time", kind: A.CopyText, arguments: { text: "11:00 PM" } }]
  }]
}

function conversion(icon) {
  return [{
    id: "calculator:100 km to miles", kind: K.Calculator, sourceId: "calculator", providerId: "calculator",
    icon: icon("accessories-calculator"), title: "62.137 mi", subtitle: "100 km to miles",
    details: [{ label: "Expression", value: "100 km to miles", monospace: true }],
    actions: [{ id: "copy", title: "Copy Result", kind: A.CopyText, arguments: { text: "62.137 mi" } }]
  }]
}

function emoji(icon) {
  return [{
    id: "emoji:rocket", kind: K.Emoji, sourceId: "emoji", icon: "🚀",
    section: section("emoji", "Emoji", 17), title: "🚀  rocket", subtitle: "launch space ship",
    details: [{ label: "Keywords", value: "launch space ship" }],
    actions: [{ id: "copy", title: "Copy Emoji", kind: A.CopyText, arguments: { text: "🚀" } }]
  }]
}


// Every built-in filter the help sheet and prefix palette list.
var filters = [
  { id: "applications", label: "Applications", token: "app:", aliases: ["apps:"] },
  { id: "files", label: "Files", token: "file:", aliases: ["files:", "text:"] },
  { id: "configuration", label: "Configuration", token: "cfg:", aliases: ["config:"] },
  { id: "windows-workspaces", label: "Windows", token: "win:", aliases: ["window:", "windows:"] },
  { id: "workspaces", label: "Workspaces", token: "ws:", aliases: ["workspace:", "workspaces:"] },
  { id: "omarchy-control", label: "Omarchy Control", token: "oma:", aliases: [] },
  { id: "projects", label: "Projects", token: "proj:", aliases: [] },
  { id: "snippets", label: "Snippets", token: "snip:", aliases: [] },
  { id: "clipboard", label: "Clipboard history", token: "clip:", aliases: [] },
  { id: "systemd-units", label: "Services", token: "svc:", aliases: ["service:", "services:"] },
  { id: "herdr", label: "Herdr", token: "herdr:", aliases: ["htab:", "hspace:"] },
  { id: "timezone", label: "Timezone", token: "tz:", aliases: ["time:"] },
  { id: "web-search", label: "Web search", token: "web:", aliases: [] },
  { id: "emoji", label: "Emoji", token: "emoji:", aliases: ["e:"] }
]
