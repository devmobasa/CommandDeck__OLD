import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts

FocusScope {
  id: root
  required property var theme
  required property var controller
  property bool opened: false
  signal dismissed()
  signal cancelRequested()
  signal retryRequested()
  signal outputRequested(string action, string path, string confirmationToken)

  function focusTargets() {
    const candidates = [cancelButton, retryButton, copyButton, saveButton,
      overwriteButton, openButton, closeButton]
    return candidates.filter(item => item.visible && item.enabled)
  }
  function moveFocus(direction) {
    const targets = focusTargets()
    if (targets.length === 0) return
    let current = targets.findIndex(item => item.activeFocus)
    if (current < 0) current = direction > 0 ? -1 : 0
    targets[(current + direction + targets.length) % targets.length].forceActiveFocus()
  }

  // One-click save into the deck's answer folder (~/Documents by default).
  // A native file chooser cannot run inside the shell process: with the GTK
  // platform theme it took Quickshell down, so the daemon still owns the
  // write, the overwrite confirmation, and reopening the saved file.
  property string saveDirectory: ""
  property string lastSavePath: ""

  function defaultSavePath() {
    if (root.saveDirectory.length === 0) return ""
    const now = new Date()
    const two = value => (value < 10 ? "0" : "") + value
    const stamp = now.getFullYear() + "-" + two(now.getMonth() + 1) + "-" + two(now.getDate())
      + "-" + two(now.getHours()) + two(now.getMinutes()) + two(now.getSeconds())
    return root.saveDirectory + "/commanddeck-answer-" + stamp + ".md"
  }

  function saveTo(path) {
    const target = String(path || "").trim()
    if (target.length === 0) return
    root.lastSavePath = target
    root.outputRequested(Ui.OutputAction.Save, target, "")
  }

  visible: opened
  focus: opened
  Accessible.role: Accessible.Dialog
  Accessible.name: "AI response"
  Keys.onEscapePressed: root.dismissed()
  Keys.onPressed: event => {
    if (event.key === Qt.Key_Tab) {
      root.moveFocus((event.modifiers & Qt.ShiftModifier) ? -1 : 1)
      event.accepted = true
    } else if (event.key === Qt.Key_Backtab) {
      root.moveFocus(-1)
      event.accepted = true
    }
  }
  Rectangle {
    anchors.fill: parent; color: root.theme.scrim
    MouseArea { anchors.fill: parent; onClicked: root.dismissed() }
  }
  Rectangle {
    width: Math.min(800, parent.width - 80)
    height: Math.min(640, parent.height - 80)
    anchors.centerIn: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1; border.color: root.theme.border
    MouseArea { anchors.fill: parent; acceptedButtons: Qt.NoButton }
    ColumnLayout {
      anchors.fill: parent; anchors.margins: root.theme.panelPadding; spacing: root.theme.gap
      Label { text: "AI response"; font.bold: true; color: root.theme.text }
      Label {
        objectName: "aiResponseState"
        text: root.controller.state === Ui.AiState.Streaming ? "Generating…"
          : root.controller.error.length > 0 ? root.controller.error
          : root.controller.state
        textFormat: Text.PlainText
        color: root.controller.error.length > 0 ? root.theme.urgent : root.theme.text
      }
      Label {
        objectName: "aiResponseOutputStatus"
        visible: root.controller.outputStatus.length > 0
        text: root.controller.outputStatus
        textFormat: Text.PlainText
        color: root.controller.overwritePending ? root.theme.urgent : root.theme.textMuted
        wrapMode: Text.Wrap
        Accessible.name: "AI output action: " + text
      }
      ScrollView {
        Layout.fillWidth: true; Layout.fillHeight: true
        AiMarkdownView {
          width: parent.width
          theme: root.theme
          sourceText: root.controller.output
        }
      }
      GridLayout {
        visible: root.controller.state === Ui.AiState.Completed
          || root.controller.state === Ui.AiState.Cancelled
          || root.controller.state === Ui.AiState.Failed
        columns: 2
        Layout.fillWidth: true
        Label { text: "Input tokens"; color: root.theme.textMuted }
        Label {
          text: root.controller.usage && root.controller.usage.inputTokens !== undefined
            && root.controller.usage.inputTokens !== null
            ? String(root.controller.usage.inputTokens) : "Not reported"
          color: root.theme.text
        }
        Label { text: "Output tokens"; color: root.theme.textMuted }
        Label {
          text: root.controller.usage && root.controller.usage.outputTokens !== undefined
            && root.controller.usage.outputTokens !== null
            ? String(root.controller.usage.outputTokens) : "Not reported"
          color: root.theme.text
        }
        Label { text: "Context limit"; color: root.theme.textMuted }
        Label {
          text: root.controller.usage && root.controller.usage.contextLimit
            ? String(root.controller.usage.contextLimit) : "Not reported"
          color: root.theme.text
        }
        Label { text: "Rate remaining"; color: root.theme.textMuted }
        Label {
          text: root.controller.usage && root.controller.usage.rateRemaining !== undefined
            && root.controller.usage.rateRemaining !== null
            ? String(root.controller.usage.rateRemaining) : "Not reported"
          color: root.theme.text
        }
        Label { text: "Estimated cost"; color: root.theme.textMuted }
        Label {
          text: root.controller.usage && root.controller.usage.estimatedCost !== undefined
            && root.controller.usage.estimatedCost !== null
            ? String(root.controller.usage.estimatedCost) + " "
              + (root.controller.usage.costCurrency || "")
            : "Not configured"
          color: root.theme.text
        }
      }
      RowLayout {
        Layout.alignment: Qt.AlignRight
        DeckButton {
          id: cancelButton
          theme: root.theme
          objectName: "aiOutputCancel"
          text: "Cancel"; visible: root.controller.state === Ui.AiState.Streaming
          onClicked: root.cancelRequested()
          Keys.onTabPressed: root.moveFocus(1)
          Keys.onBacktabPressed: root.moveFocus(-1)
        }
        DeckButton {
          id: retryButton
          theme: root.theme
          objectName: "aiOutputRetry"
          text: "Retry"; visible: root.controller.state === Ui.AiState.Failed || root.controller.state === Ui.AiState.Cancelled
          onClicked: root.retryRequested()
          Keys.onTabPressed: root.moveFocus(1)
          Keys.onBacktabPressed: root.moveFocus(-1)
        }
        DeckButton {
          id: copyButton
          theme: root.theme
          objectName: "aiOutputCopy"
          text: "Copy"; visible: root.controller.state === Ui.AiState.Completed
          onClicked: root.outputRequested(Ui.OutputAction.Copy, "", "")
          Keys.onTabPressed: root.moveFocus(1)
          Keys.onBacktabPressed: root.moveFocus(-1)
        }
        DeckButton {
          id: saveButton
          theme: root.theme
          objectName: "aiOutputSave"
          text: "Save"; visible: root.controller.state === Ui.AiState.Completed
          enabled: root.saveDirectory.length > 0
          Accessible.description: "Saves a Markdown file into " + root.saveDirectory
          onClicked: root.saveTo(root.defaultSavePath())
          Keys.onTabPressed: root.moveFocus(1)
          Keys.onBacktabPressed: root.moveFocus(-1)
        }
        DeckButton {
          id: overwriteButton
          theme: root.theme
          kind: Ui.ButtonVariant.Danger
          objectName: "aiOutputOverwrite"
          text: "Confirm Overwrite"
          visible: root.controller.state === Ui.AiState.Completed
            && root.controller.overwritePending
          Accessible.name: "Confirm overwrite AI output file"
          Accessible.description: "Destructive action. Replaces the selected file"
          onClicked: root.outputRequested(
            Ui.OutputAction.Save,
            root.controller.outputPathPending,
            root.controller.overwriteConfirmationToken)
          Keys.onTabPressed: root.moveFocus(1)
          Keys.onBacktabPressed: root.moveFocus(-1)
        }
        DeckButton {
          id: openButton
          theme: root.theme
          objectName: "aiOutputOpen"
          text: "Open"; visible: root.controller.state === Ui.AiState.Completed
          enabled: root.controller.savedOutputAvailable
          Accessible.description: enabled
            ? "Open the exact file saved from this response"
            : "Save this response before opening it"
          onClicked: root.outputRequested(Ui.OutputAction.Open, "", "")
          Keys.onTabPressed: root.moveFocus(1)
          Keys.onBacktabPressed: root.moveFocus(-1)
        }
        DeckButton {
          id: closeButton
          theme: root.theme
          objectName: "aiOutputClose"
          text: "Close"
          onClicked: root.dismissed()
          Keys.onTabPressed: root.moveFocus(1)
          Keys.onBacktabPressed: root.moveFocus(-1)
        }
      }
    }
  }
  onOpenedChanged: if (opened) Qt.callLater(function() { closeButton.forceActiveFocus() })
}
