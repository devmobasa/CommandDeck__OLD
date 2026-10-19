import QtQuick
import "Protocol.js" as Protocol
import "UiConstants.js" as Ui

// A one-character symbol for an action menu row, chosen by what the action
// does so rows of the same meaning look alike on every result. Plain text
// keeps it legible in any theme and font without an icon theme.
Text {
  id: root

  required property var theme
  property var action: null
  property bool bookmarked: false

  function glyphFor(action, bookmarked) {
    if (!action) return ""
    if (action.isDestructive === true) return "✕"
    switch (action.kind) {
      case Ui.ResultAction.FavoriteToggle: return bookmarked ? "★" : "☆"
      case Ui.ResultAction.AliasEdit: return "#"
      case Ui.ResultAction.HideResult: return "⊘"
      case Ui.ResultAction.RankingExplanation: return "?"
      case Protocol.ActionKinds.OpenPath:
      case Protocol.ActionKinds.OpenUrl:
      case Protocol.ActionKinds.LaunchDesktop:
      case Protocol.ActionKinds.ExtensionOpen:
      case Protocol.ActionKinds.AgentOpen:
      case Protocol.ActionKinds.HeyOpen:
      case Protocol.ActionKinds.HeyOpenPage: return "↗"
      case Protocol.ActionKinds.RevealPath:
      case Protocol.ActionKinds.BrowseFolder: return "↳"
      case Protocol.ActionKinds.CopyText:
      case Protocol.ActionKinds.SnippetCopy: return "⧉"
      case Protocol.ActionKinds.EditAtLine: return "✎"
      case Protocol.ActionKinds.HyprlandDispatch:
      case Protocol.ActionKinds.HerdrFocus: return "▣"
      case Protocol.ActionKinds.AiPrompt:
      case Protocol.ActionKinds.AgentPrompt: return "✦"
      case Protocol.ActionKinds.ApplyQuery:
      case Protocol.ActionKinds.ApplySavedSearch:
      case Protocol.ActionKinds.BrowseHerdrTabs: return "⌕"
      case Protocol.ActionKinds.RunArgv:
      case Protocol.ActionKinds.RunTrustedShell:
      case Protocol.ActionKinds.RunScript:
      case Protocol.ActionKinds.SystemdUnit: return "›"
      default: return "•"
    }
  }

  text: root.glyphFor(root.action, root.bookmarked)
  textFormat: Text.PlainText
  horizontalAlignment: Text.AlignHCenter
  verticalAlignment: Text.AlignVCenter
  color: root.theme.textMuted
  font.family: root.theme.fontFamily
  font.pixelSize: root.theme.fontBody
  Accessible.ignored: true
}
