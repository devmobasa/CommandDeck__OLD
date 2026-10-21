pragma ComponentBehavior: Bound
import QtQuick
import Quickshell
import "./components" as Deck
import "./components/Protocol.js" as Protocol

// Projects, clipboard history, snippets, file selection, and the direct
// provider request preview.
ShellRoot {
  id: root
  property string overlay: ""
  property var projectResult: null
  readonly property string snippetTemplate:
    "Release {version}\n\nWhat's new:\n{highlights}\n\nThanks for trying the preview!"
  readonly property string expandedSnippet:
    "Release 0.4.0\n\nWhat's new:\nFaster search and clearer errors.\n\nThanks for trying the preview!"

  DemoStage {
    id: stage
    shots: [
      { name: "project-details.png", show: root.showProject },
      { name: "project.png", show: root.showProjectActions },
      { name: "clipboard.png", show: root.showClipboard },
      { name: "snippet-form.png", show: root.showSnippet },
      { name: "snippet-preview.png", show: root.showSnippetPreview },
      { name: "file-selection.png", show: root.showSelection },
      { name: "ai-preflight.png", show: root.showPreflight }
    ]
    panelOverlays: [
      Deck.ActionPanel {
        id: projectActions
        theme: stage.theme
        width: Math.min(Math.round(360 * stage.theme.scale), parent.width * 0.45)
        height: Math.min(parent.height - 28, implicitHeight)
        anchors.right: parent.right
        anchors.top: parent.top
        anchors.margins: 14
        visible: root.overlay === "project-actions"
      },
      Deck.ClipboardHistorySurface {
        anchors.fill: parent
        theme: stage.theme
        controller: stage.deck.part_clipboardController
      }
    ]

    Loader {
      id: snippetInputs
      anchors.fill: parent
      active: root.overlay === "snippet-form"
      sourceComponent: Component {
        Deck.ActionInputDialog {
          theme: stage.theme
          opened: true
          action: ({
            id: "snippet-preview", kind: Protocol.ActionKinds.SnippetPreview,
            title: "Preview expanded snippet",
            inputs: [{ name: "version" }, { name: "highlights" }]
          })
        }
      }
    }

    Deck.DetailDialog {
      anchors.fill: parent
      theme: stage.theme
      opened: root.overlay === "snippet-preview"
      title: "Release announcement"
      text: root.expandedSnippet
      plainText: true
    }

    Deck.AiPreflightSurface {
      anchors.fill: parent
      theme: stage.theme
      opened: root.overlay === "preflight"
      preflight: ({
        providerId: "demo-local", modelId: "demo-model", locality: "local",
        destination: "http://127.0.0.1:11434/api/chat", credentialSource: "not-required",
        payloadDigest: Quickshell.env("COMMANDDECK_PREVIEW_DIGEST"),
        payloadBytes: Quickshell.env("COMMANDDECK_PREVIEW_JSON").length,
        systemMessage: "Answer briefly.", userMessage: "Explain why the Moon has phases.",
        attachments: [], providerRequestPreview: Quickshell.env("COMMANDDECK_PREVIEW_JSON")
      })
    }
  }

  function showProject() {
    const placement = { id: "projects", title: "Projects", order: 6 }
    root.overlay = ""
    root.projectResult = {
      id: "project:demo-taskboard", kind: Protocol.ResultKinds.CustomCommand,
      sourceId: "projects", title: "Demo Taskboard", subtitle: "/demo/projects/taskboard",
      icon: stage.icon("folder-development"), section: placement,
      detailMarkdown: "/demo/projects/taskboard\n\n## main\n M src/Search.cs\n M README.md",
      actions: [
        { id: "editor", title: "Open in code", kind: Protocol.ActionKinds.RunArgv, arguments: {} },
        { id: "terminal", title: "Open terminal here", kind: Protocol.ActionKinds.RunArgv, arguments: {} },
        { id: "copy-path", title: "Copy project path", kind: Protocol.ActionKinds.CopyText,
          arguments: { text: "/demo/projects/taskboard" } }
      ]
    }
    stage.setResults("proj: demo", [root.projectResult,
      { id: "project:demo-site", kind: Protocol.ResultKinds.CustomCommand, sourceId: "projects",
        title: "Demo Website", subtitle: "/demo/projects/website", icon: stage.icon("folder-development"),
        section: placement,
        actions: [{ id: "editor", title: "Open in code", kind: Protocol.ActionKinds.RunArgv, arguments: {} }] }
    ], "2 projects")
    stage.content.part_searchBar.inputControl.cursorPosition = 0
  }

  function showProjectActions() {
    root.showProject()
    projectActions.openFor(root.projectResult)
    root.overlay = "project-actions"
  }

  function showClipboard() {
    const controller = stage.deck.part_clipboardController
    const now = Math.floor(Date.now() / 1000)
    root.overlay = ""
    controller.sessionId = "demo-session"
    controller.settings = {
      status: "Paused", policy: { revision: 1, consent: "Rich", paused: true,
        maximumEntries: 200, retentionHours: 168, quotaMiB: 256, clearOnStartup: false }
    }
    controller.capabilities = { copy: true, inspectOpenHandlers: true }
    controller.entries = [
      { id: "demo-checklist", incarnation: "demo-one", revision: 1, pinned: true,
        copiedUnixSeconds: now - 180, summary: "Release checklist", keywords: ["demo", "release"],
        presentation: { kind: "text", hasOpenDestination: false } },
      { id: "demo-link", incarnation: "demo-two", revision: 1, pinned: false,
        copiedUnixSeconds: now - 240, summary: "https://doc.qt.io/qt-6/qtquick-index.html",
        keywords: ["docs"], presentation: { kind: "links", hasOpenDestination: true } },
      { id: "demo-image", incarnation: "demo-three", revision: 1, pinned: false,
        copiedUnixSeconds: now - 300, summary: "Calculator screenshot", keywords: ["demo", "screenshot"],
        presentation: { kind: "images", imageWidth: 2940, imageHeight: 2040, hasOpenDestination: false } },
      { id: "demo-note", incarnation: "demo-four", revision: 1, pinned: false,
        copiedUnixSeconds: now - 540, summary: "Share the draft, then collect feedback.", keywords: [],
        presentation: { kind: "text", hasOpenDestination: false } }
    ]
    controller.selectedIndex = 2
    controller.preview = {
      presentation: controller.entries[2].presentation,
      imageSource: Quickshell.env("COMMANDDECK_DEMO_IMAGE")
    }
    controller.pageLoaded = true
    controller.opened = true
  }

  function showSnippet() {
    stage.deck.part_clipboardController.opened = false
    root.overlay = "snippet-form"
    stage.setResults("snip: release", [{ id: "snippet:release", kind: Protocol.ResultKinds.CustomCommand,
      sourceId: "snippets", title: "Release announcement", subtitle: "Snippet · copy or preview with inputs",
      icon: stage.icon("edit-paste"), detailMarkdown: root.snippetTemplate,
      actions: [{ id: "snippet-preview", title: "Preview expanded snippet",
        kind: Protocol.ActionKinds.SnippetPreview, arguments: {},
        inputs: [{ name: "version" }, { name: "highlights" }] }]
    }], "1 snippet")
    Qt.callLater(function() {
      const dialog = snippetInputs.item as Deck.ActionInputDialog
      if (!dialog) {
        throw new Error("The snippet input fixture did not load")
      }

      dialog.rowAt(0).input.text = "0.4.0"
      dialog.rowAt(1).input.text = "Faster search and clearer errors."
    })
  }

  function showSnippetPreview() {
    root.overlay = "snippet-preview"
  }

  function showSelection() {
    const titles = ["Planning notes.md", "Release notes.md", "Meeting notes.txt"]
    const icon = stage.icon("text-x-generic")
    const rows = titles.map(function(title, index) {
      return { id: "file:demo-" + index, kind: Protocol.ResultKinds.File, sourceId: "files",
        title: title, subtitle: "/demo/notes", icon: icon,
        actions: [
          { id: "open", title: "Open", kind: Protocol.ActionKinds.OpenPath,
            arguments: { path: "/demo/notes/" + title } },
          { id: "select", title: "Toggle file selection", kind: Protocol.ActionKinds.CollectFile,
            arguments: { path: "/demo/notes/" + title } }
        ] }
    })
    root.overlay = ""
    stage.deck.fileSelection = titles.slice(0, 2).map(function(title, index) {
      return { resultId: "file:demo-" + index, title: title, path: "/demo/notes/" + title }
    })
    stage.setResults("browse: /demo/notes", rows, "3 files · 2 selected")
    stage.content.part_searchBar.inputControl.cursorPosition = 0
  }

  function showPreflight() {
    stage.deck.fileSelection = []
    stage.setResults("ai: Explain why the Moon has phases.", [{ id: "ai:demo-local",
      kind: Protocol.ResultKinds.AiCommand, sourceId: "ai", title: "Ask local AI (demo-model)",
      subtitle: "Explicit local prompt; no cloud transfer", icon: stage.icon("computer"),
      actions: [{ id: "ask", title: "Ask", kind: Protocol.ActionKinds.AiPrompt, arguments: {} }]
    }], "Review the request before sending")
    stage.content.part_searchBar.inputControl.cursorPosition = 0
    root.overlay = "preflight"
  }
}
