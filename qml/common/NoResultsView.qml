import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Layouts

// C9 - guiding no-results state, with the D12 assistant route.
//
// Replaces the bare "No results" with actionable guidance that names the query
// and offers a concrete next step: a real filter token (from the descriptors)
// and an activatable "ask the assistant" route that carries the current text to
// the Codex assistant via the existing `ai:` path.
Item {
  id: root

  required property var theme
  // The AI master switch hides the assistant route entirely.
  property bool assistantAvailable: true
  property string query: ""
  property var filterDescriptors: []

  signal assistantRequested()
  signal filtersRequested()

  readonly property string suggestionToken: catalog.suggestionToken(root.filterDescriptors)
  readonly property string messageText: "No matches for \"" + root.query
    + "\" — try a filter like " + root.suggestionToken
    + " or ask the assistant with ai:"

  // Test and wiring hooks.
  readonly property alias assistantControl: assistantButton
  readonly property alias filtersControl: filtersButton
  function activateAssistant() { root.assistantRequested() }

  FilterCatalog { id: catalog }

  Accessible.role: Accessible.StaticText
  Accessible.name: root.messageText

  ColumnLayout {
    anchors.left: parent.left
    anchors.right: parent.right
    anchors.verticalCenter: parent.verticalCenter
    anchors.leftMargin: root.theme.panelPadding
    anchors.rightMargin: root.theme.panelPadding
    spacing: root.theme.gap

    Text {
      text: "No matches"
      color: root.theme.text
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSubtitle
      font.weight: Font.DemiBold
    }

    Text {
      id: messageLabel
      Layout.fillWidth: true
      textFormat: Text.PlainText
      text: root.messageText
      color: root.theme.textMuted
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
      wrapMode: Text.Wrap
    }

    RowLayout {
      Layout.fillWidth: true
      Layout.topMargin: Math.round(4 * root.theme.scale)
      spacing: Math.max(6, Math.round(8 * root.theme.scale))

      // Activatable assistant route (D12): Enter/click carries the current text
      // to the assistant.
      Rectangle {
        id: assistantButton
        visible: root.assistantAvailable
        Layout.preferredHeight: Math.round(root.theme.inputHeight * 0.82)
        // Size from the whole label row so the `ai:` token keeps its padding.
        implicitWidth: assistantContent.implicitWidth + Math.round(24 * root.theme.scale)
        radius: root.theme.radiusSmall
        color: assistantButton.activeFocus
          ? root.theme.selectedBackground
          : root.theme.withAlpha(root.theme.accent, 0.12)
        border.width: 1
        border.color: assistantButton.activeFocus
          ? root.theme.selectedBorder
          : root.theme.withAlpha(root.theme.accent, 0.5)
        activeFocusOnTab: true

        Accessible.role: Accessible.Button
        Accessible.name: "Ask the assistant"
        Accessible.description: "Send \"" + root.query + "\" to the Codex assistant"
        Accessible.onPressAction: assistantButton.activate()

        function activate() { root.assistantRequested() }

        Keys.onPressed: event => {
          if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter
              || event.key === Qt.Key_Space) {
            assistantButton.activate()
            event.accepted = true
          }
        }

        RowLayout {
          id: assistantContent
          anchors.centerIn: parent
          spacing: Math.round(8 * root.theme.scale)

          Text {
            id: assistantLabel
            text: "Ask the assistant"
            color: root.theme.text
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontSmall
            font.weight: Font.DemiBold
          }

          Text {
            text: Ui.QueryPrefix.Ai
            color: root.theme.accent
            font.family: "monospace"
            font.pixelSize: root.theme.fontSmall
          }
        }

        MouseArea {
          anchors.fill: parent
          onClicked: assistantButton.activate()
        }
      }

      Rectangle {
        id: filtersButton
        Layout.preferredHeight: Math.round(root.theme.inputHeight * 0.82)
        implicitWidth: filtersLabel.implicitWidth + Math.round(24 * root.theme.scale)
        radius: root.theme.radiusSmall
        color: filtersButton.activeFocus ? root.theme.selectedBackground : "transparent"
        border.width: 1
        border.color: filtersButton.activeFocus
          ? root.theme.selectedBorder : root.theme.separator
        activeFocusOnTab: true

        Accessible.role: Accessible.Button
        Accessible.name: "Browse all filters"
        Accessible.onPressAction: filtersButton.activate()

        function activate() { root.filtersRequested() }

        Keys.onPressed: event => {
          if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter
              || event.key === Qt.Key_Space) {
            filtersButton.activate()
            event.accepted = true
          }
        }

        Text {
          id: filtersLabel
          anchors.centerIn: parent
          text: "Browse filters  ?"
          color: root.theme.textMuted
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontSmall
        }

        MouseArea {
          anchors.fill: parent
          onClicked: filtersButton.activate()
        }
      }
    }
  }
}
