import QtQuick
import "UiConstants.js" as Ui

// Shared, non-visual source of truth for the discoverability surfaces.
//
// The launcher's teaching states (empty-state chips, the "?" filter palette)
// must never drift from the daemon's SearchFilterRegistry, so the visible list
// is always derived from `filterDescriptors`. This object only adds a curated
// one-line runnable example per known token; a token with no mapped example
// still renders, falling back to the token itself.
QtObject {
  id: root

  // Keyed by the canonical filter token so it stays valid as labels change.
  // The calculator ("=") is not a daemon filter but is offered alongside them.
  readonly property var examples: ({
    [Ui.QueryPrefix.App]: "app: firefox",
    [Ui.QueryPrefix.Apps]: "apps: firefox",
    [Ui.QueryPrefix.File]: "file: report.md",
    [Ui.QueryPrefix.Files]: "files: report.md",
    [Ui.QueryPrefix.Text]: "text: meeting notes",
    [Ui.QueryPrefix.Service]: "svc: nginx",
    [Ui.QueryPrefix.ServiceLong]: "service: nginx",
    [Ui.QueryPrefix.Services]: "services: nginx",
    [Ui.QueryPrefix.Ai]: "ai: summarize this file",
    [Ui.QueryPrefix.Codex]: "codex: refactor this function",
    [Ui.QueryPrefix.Claude]: "claude: explain this stack trace",
    [Ui.QueryPrefix.Cursor]: "cursor: review this diff",
    [Ui.QueryPrefix.Grok]: "grok: summarize this article",
    [Ui.QueryPrefix.Ask]: "ask: how do I resize a window",
    [Ui.QueryPrefix.Configuration]: "cfg: monitors",
    [Ui.QueryPrefix.Config]: "config: monitors",
    [Ui.QueryPrefix.Win]: "win: slack",
    [Ui.QueryPrefix.Window]: "window: slack",
    [Ui.QueryPrefix.Windows]: "windows: slack",
    [Ui.QueryPrefix.Workspace]: "workspace: 3",
    [Ui.QueryPrefix.Ws]: "ws: 3",
    [Ui.QueryPrefix.Workspaces]: "workspaces: 3",
    [Ui.QueryPrefix.Projects]: "proj: dotfiles",
    [Ui.QueryPrefix.Snippets]: "snip: review",
    [Ui.QueryPrefix.Scripts]: "script: tests",
    [Ui.QueryPrefix.Clipboard]: "clip: invoice",
    [Ui.QueryPrefix.Browse]: "browse: ~/Downloads",
    [Ui.QueryPrefix.Omarchy]: "oma: theme",
    [Ui.QueryPrefix.HerdrTab]: "htab: tests",
    [Ui.QueryPrefix.Command]: "cmd: deploy",
    [Ui.QueryPrefix.Quicklink]: "link: github",
    [Ui.QueryPrefix.Unit]: "unit: nginx",
    [Ui.QueryPrefix.Timezone]: "tz: tokyo",
    [Ui.QueryPrefix.Time]: "time: 3pm london to tokyo",
    [Ui.QueryPrefix.Web]: "web: wayland layer shell",
    [Ui.QueryPrefix.Emoji]: "emoji: rocket",
    [Ui.QueryPrefix.EmojiShort]: "e: rocket",
    [Ui.QueryPrefix.Calculator]: "= 100 km to miles"
  })

  // The assistant is offered through a dedicated, prominent affordance, so its
  // filter tokens are kept out of the plain filter grid to avoid duplication.
  readonly property var assistantTokens: [Ui.QueryPrefix.Ai, Ui.QueryPrefix.Codex, Ui.QueryPrefix.Claude, Ui.QueryPrefix.Cursor, Ui.QueryPrefix.Grok, Ui.QueryPrefix.Ask]

  function normalize(token) {
    return String(token || "").toLowerCase()
  }

  function exampleFor(token) {
    const key = root.normalize(token)
    if (root.hasExample(key)) return root.examples[key]
    return key
  }

  // False when exampleFor only echoes the token, so a surface that already
  // shows the token can leave the example out.
  function hasExample(token) {
    return root.examples.hasOwnProperty(root.normalize(token))
  }

  function isAssistantToken(token) {
    return root.assistantTokens.indexOf(root.normalize(token)) >= 0
  }

  // Derive { id, token, label, example, hasExample } entries from the daemon descriptors so
  // the visible list can never fall out of sync with the registry.
  function entries(descriptors) {
    const list = []
    const source = descriptors || []
    for (let index = 0; index < source.length; index++) {
      const descriptor = source[index]
      const token = String(descriptor.token || "")
      if (token.length === 0) continue
      list.push({
        id: descriptor.id,
        token: token,
        label: String(descriptor.label || token),
        example: root.exampleFor(token),
        hasExample: root.hasExample(token)
      })
    }
    return list
  }

  readonly property var calculatorEntry: ({
    id: Ui.FilterTokenKind.Calculator,
    token: Ui.QueryPrefix.Calculator,
    label: "Calculator",
    example: root.exampleFor(Ui.QueryPrefix.Calculator),
    hasExample: true
  })

  // The teaching grid: the calculator first (not a daemon filter), then every
  // non-assistant filter (the assistant has its own prominent chip).
  // `includeCalculator` lets a caller opt out.
  function chipEntries(descriptors, includeCalculator) {
    const derived = root.entries(descriptors).filter(
      entry => !root.isAssistantToken(entry.token))
    return includeCalculator === false
      ? derived
      : [root.calculatorEntry].concat(derived)
  }

  // The "?" palette lists every filter prefix, including the assistant, with the
  // calculator offered alongside them.
  function paletteEntries(descriptors, includeCalculator) {
    const derived = root.entries(descriptors)
    return includeCalculator === false
      ? derived
      : [root.calculatorEntry].concat(derived)
  }

  // Returns a concrete filter token to suggest in guidance text, preferring the
  // files filter, then the first descriptor, then a literal fallback.
  function suggestionToken(descriptors) {
    const derived = root.entries(descriptors).filter(
      entry => !root.isAssistantToken(entry.token))
    for (let index = 0; index < derived.length; index++) {
      if (root.normalize(derived[index].token) === Ui.QueryPrefix.File) return derived[index].token
    }
    return derived.length > 0 ? derived[0].token : Ui.QueryPrefix.File
  }
}
