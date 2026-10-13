import QtQuick
import Quickshell
import "./components" as Deck
import "./components/Protocol.js" as Protocol
import "./DemoData.js" as Data

// Applications, file search, the calculator, and the prefix palette.
ShellRoot {
  id: root
  property bool prefixesOpen: false

  DemoStage {
    id: stage
    filters: [
      { id: "applications", label: "Applications", token: "app:", aliases: [] },
      { id: "files", label: "Files", token: "file:", aliases: ["text:"] },
      { id: "projects", label: "Projects", token: "proj:", aliases: [] },
      { id: "snippets", label: "Snippets", token: "snip:", aliases: [] },
      { id: "services", label: "Services", token: "svc:", aliases: [] },
      { id: "windows", label: "Windows", token: "win:", aliases: [] },
      { id: "web", label: "Web search", token: "web:", aliases: [] },
      { id: "emoji", label: "Emoji", token: "emoji:", aliases: [] }
    ]
    shots: [
      { name: "applications.png", show: root.showApps,
        caption: "CommandDeck · demonstration app entries" },
      { name: "search.png", show: root.showSearch },
      { name: "calculator.png", show: root.showCalculator },
      { name: "prefixes.png", show: root.showPrefixes }
    ]
    panelOverlays: [
      Deck.FilterPalette {
        anchors.fill: parent
        anchors.margins: stage.theme.panelPadding
        theme: stage.theme
        opened: root.prefixesOpen
        visible: opened
        filterDescriptors: stage.filters
      }
    ]
  }

  function app(id, title, subtitle, icon, command) {
    const desktopFile = "/demo/apps/" + id + ".desktop"
    return {
      id: "app:" + id + ".desktop", kind: Protocol.ResultKinds.Application,
      sourceId: "applications", providerId: "applications", title: title,
      subtitle: subtitle, icon: icon, canCustomize: true,
      details: [
        { label: "Desktop file", value: desktopFile, monospace: true },
        { label: "Desktop ID", value: id + ".desktop", monospace: true },
        { label: "Command", value: command, monospace: true },
        { label: "Terminal", value: "No" }
      ],
      actions: [{ id: "launch", title: "Launch", kind: Protocol.ActionKinds.LaunchDesktop,
        arguments: { desktopFile: desktopFile, desktopId: id + ".desktop", applicationName: title } }]
    }
  }

  function file(id, title, folder, icon, extra) {
    return Object.assign({
      id: "file:" + id, kind: Protocol.ResultKinds.File, sourceId: "files",
      title: title, subtitle: folder, icon: icon,
      actions: [{ id: "open", title: "Open", kind: Protocol.ActionKinds.OpenPath, arguments: {} }]
    }, extra || {})
  }

  function showApps() {
    stage.setResults("app:", [
      root.app("firefox", "Firefox", "Browse the web", stage.icon("firefox"), "firefox %u"),
      root.app("org.gnome.Nautilus", "Files", "Browse folders and files",
        stage.icon("org.gnome.Nautilus"), "nautilus %U"),
      root.app("demo-terminal", "Terminal", "Work in a command line",
        stage.icon("utilities-terminal"), "xdg-terminal-exec"),
      root.app("demo-editor", "Text Editor", "Edit text documents",
        stage.icon("accessories-text-editor"), "demo-editor %U")
    ], "4 applications")
    stage.content.part_searchBar.inputControl.cursorPosition = 0
  }

  function showSearch() {
    stage.setResults("notes", [
      root.file("planning-notes", "Planning notes.md", "Demo / Notes", stage.icon("text-markdown"), {
        invocationReference: "demo-planning-notes", canCustomize: true,
        actions: [
          { id: "open", title: "Open", kind: Protocol.ActionKinds.OpenPath, arguments: {} },
          { id: "reveal", title: "Reveal in File Manager", kind: Protocol.ActionKinds.RevealPath, arguments: {} }
        ],
        details: [{ label: "Folder", value: "Demo / Notes" }, { label: "Type", value: "Markdown document" }],
        detailMarkdown: "## Planning notes\n\nA small example document for the next project.\n\n"
          + "- Sketch the first screen\n- Write down the open questions\n- Share a short draft"
      }),
      root.file("release-notes", "Release notes.md", "Demo / Project", stage.icon("text-markdown")),
      root.file("meeting-notes", "Meeting notes.txt", "Demo / Notes", stage.icon("text-x-generic"))
    ], "3 results")
  }

  function showCalculator() {
    stage.setResults("= 100 km to miles", Data.conversion(stage.icon), "1 result")
  }

  function showPrefixes() {
    root.showSearch()
    root.prefixesOpen = true
  }
}
