pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import "Protocol.js" as Protocol

Item {
  required property var host
  readonly property alias part_reasoningToggle: reasoningToggle
  readonly property alias part_outputToast: outputToast

  id: transcriptPane
  Layout.fillWidth: true
  Layout.fillHeight: true

  ScrollView {
    id: transcriptScroll
    anchors.fill: parent
    clip: true
    contentWidth: availableWidth

    ColumnLayout {
      width: transcriptScroll.availableWidth
      spacing: Math.round(14 * transcriptPane.host.theme.scale)

      Repeater {
        id: entryRepeater
        model: transcriptPane.host.controller.entries

        delegate: AgentTranscriptBubble {
          id: entryDelegate
          required property var modelData
          readonly property string roleLabel: modelData && modelData.role === Protocol.AgentTranscriptRoles.User
            ? "You" : transcriptPane.host.backendDisplayName
          Layout.fillWidth: true
          theme: transcriptPane.host.theme
          fromUser: modelData && modelData.role === Protocol.AgentTranscriptRoles.User
          label: entryDelegate.roleLabel
          body: entryDelegate.modelData ? String(entryDelegate.modelData.text || "") : ""
          truncated: entryDelegate.modelData && entryDelegate.modelData.truncated === true
          onCopyRequested: transcriptPane.host.copyText(body)
        }
      }

      AgentTranscriptBubble {
        id: activeTextView
        objectName: "agentActiveText"
        Layout.fillWidth: true
        visible: transcriptPane.host.controller.activeText.length > 0
        theme: transcriptPane.host.theme
        label: transcriptPane.host.backendDisplayName
        body: transcriptPane.host.controller.activeText
        Accessible.name: "Streaming assistant answer"
        onCopyRequested: transcriptPane.host.copyText(body)
      }

    RowLayout {
      Layout.fillWidth: true
      visible: transcriptPane.host.controller.activeReasoning.length > 0
      spacing: transcriptPane.host.theme.gap

      AgentButton {
        id: reasoningToggle
        theme: transcriptPane.host.theme
        compact: true
        objectName: "agentReasoningToggle"
        text: transcriptPane.host.reasoningExpanded ? "Hide Thinking" : "Thinking"
        Accessible.name: transcriptPane.host.reasoningExpanded
          ? "Hide the reasoning summary"
          : "Show the reasoning summary"
        onClicked: transcriptPane.host.reasoningExpanded = !transcriptPane.host.reasoningExpanded
        Keys.onTabPressed: transcriptPane.host.moveFocus(1)
        Keys.onBacktabPressed: transcriptPane.host.moveFocus(-1)
      }

      Text {
        Layout.fillWidth: true
        visible: !transcriptPane.host.reasoningExpanded
        text: "Reasoning summary hidden"
        color: transcriptPane.host.theme.textFaint
        font.family: transcriptPane.host.theme.fontFamily
        font.pixelSize: transcriptPane.host.theme.fontCaption
        elide: Text.ElideRight
      }
    }

    Text {
      id: reasoningBody
      objectName: "agentReasoningBody"
      Layout.fillWidth: true
      visible: transcriptPane.host.reasoningExpanded
        && transcriptPane.host.controller.activeReasoning.length > 0
      textFormat: Text.PlainText
      text: transcriptPane.host.controller.activeReasoning
      color: transcriptPane.host.theme.textMuted
      wrapMode: Text.Wrap
      font.family: transcriptPane.host.theme.fontFamily
      font.pixelSize: transcriptPane.host.theme.fontSmall
      Accessible.role: Accessible.StaticText
      Accessible.name: "Reasoning summary"
    }

    Repeater {
      id: toolRepeater
      model: transcriptPane.host.controller.tools

      delegate: RowLayout {
        id: toolDelegate
        required property var modelData
        Layout.fillWidth: true
        spacing: transcriptPane.host.theme.gap
        Accessible.role: Accessible.ListItem
        Accessible.name: "Tool "
          + String(toolDelegate.modelData
            ? (toolDelegate.modelData.label || toolDelegate.modelData.name || "")
            : "")
          + ": "
          + String(toolDelegate.modelData ? toolDelegate.modelData.state || "" : "")

        Text {
          Layout.fillWidth: true
          textFormat: Text.PlainText
          text: String(toolDelegate.modelData
            ? (toolDelegate.modelData.label
              || toolDelegate.modelData.name || "Tool") : "Tool")
          color: transcriptPane.host.theme.text
          elide: Text.ElideRight
          font.family: transcriptPane.host.theme.fontFamily
          font.pixelSize: transcriptPane.host.theme.fontSmall
        }

        Text {
          textFormat: Text.PlainText
          text: String(toolDelegate.modelData
            ? toolDelegate.modelData.state || "" : "")
          color: transcriptPane.host.theme.textFaint
          font.family: transcriptPane.host.theme.fontFamily
          font.pixelSize: transcriptPane.host.theme.fontCaption
        }
      }
    }
  }
  }

  DeckToast {
    id: outputToast
    objectName: "agentOutputToast"
    theme: transcriptPane.host.theme
    z: 2
    anchors.horizontalCenter: parent.horizontalCenter
    anchors.bottom: parent.bottom
    anchors.bottomMargin: Math.round(12 * transcriptPane.host.theme.scale)
  }
}
