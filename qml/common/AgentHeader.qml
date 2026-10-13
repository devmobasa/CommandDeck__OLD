pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts

RowLayout {
  required property var host

  id: assistantHeader
  Layout.fillWidth: true
  spacing: assistantHeader.host.theme.gap

  Text {
    id: headerLabel
    Layout.fillWidth: true
    textFormat: Text.PlainText
    text: assistantHeader.host.headerText
    color: assistantHeader.host.theme.textMuted
    font.family: assistantHeader.host.theme.fontFamily
    font.pixelSize: assistantHeader.host.theme.fontSmall
    elide: Text.ElideRight
    Accessible.role: Accessible.StaticText
    Accessible.name: "Assistant backend: " + text
  }

  // Dedicated, never-truncated chip for the reasoning effort. It carries a
  // small "Thinking" caption so the level reads as a labeled value, kept
  // visually distinct from the model/locality line and the trust badge.
  Rectangle {
    id: reasoningEffortChip
    objectName: "agentReasoningEffortChip"
    visible: assistantHeader.host.reasoningEffortLabel.length > 0
    Layout.preferredWidth: reasoningEffortRow.implicitWidth
      + Math.round(16 * assistantHeader.host.theme.scale)
    Layout.preferredHeight: reasoningEffortRow.implicitHeight
      + Math.round(8 * assistantHeader.host.theme.scale)
    radius: assistantHeader.host.theme.radiusSmall
    color: assistantHeader.host.theme.withAlpha(assistantHeader.host.theme.accent, 0.16)
    border.width: 1
    border.color: assistantHeader.host.theme.withAlpha(assistantHeader.host.theme.accent, 0.42)

    RowLayout {
      id: reasoningEffortRow
      anchors.centerIn: parent
      spacing: Math.round(6 * assistantHeader.host.theme.scale)

      Text {
        text: "Thinking"
        color: assistantHeader.host.theme.textMuted
        font.family: assistantHeader.host.theme.fontFamily
        font.pixelSize: assistantHeader.host.theme.fontCaption
        font.weight: Font.DemiBold
      }

      Text {
        id: reasoningEffortValue
        objectName: "agentReasoningEffortValue"
        textFormat: Text.PlainText
        text: assistantHeader.host.reasoningEffortLabel
        color: assistantHeader.host.theme.accent
        font.family: assistantHeader.host.theme.fontFamily
        font.pixelSize: assistantHeader.host.theme.fontSmall
        font.weight: Font.Bold
      }
    }

    Accessible.role: Accessible.StaticText
    Accessible.name: "Reasoning effort: " + assistantHeader.host.reasoningEffortLabel
    Accessible.description: "The assistant's thinking level"
  }

  Rectangle {
    id: trustBadge
    Layout.preferredWidth: trustBadgeLabel.implicitWidth
      + Math.round(12 * assistantHeader.host.theme.scale)
    Layout.preferredHeight: trustBadgeLabel.implicitHeight
      + Math.round(6 * assistantHeader.host.theme.scale)
    radius: assistantHeader.host.theme.radiusSmall
    color: assistantHeader.host.trustAlarming
      ? assistantHeader.host.theme.urgent
      : assistantHeader.host.theme.surfaceStrong

    Text {
      id: trustBadgeLabel
      anchors.centerIn: parent
      textFormat: Text.PlainText
      text: assistantHeader.host.trustBadgeText
      color: assistantHeader.host.trustAlarming ? assistantHeader.host.theme.background : assistantHeader.host.theme.text
      font.family: assistantHeader.host.theme.fontFamily
      font.pixelSize: assistantHeader.host.theme.fontCaption
      font.weight: Font.DemiBold
    }

    Accessible.role: Accessible.StaticText
    Accessible.name: "Assistant authority: " + trustBadgeLabel.text
    Accessible.description: assistantHeader.host.trustAlarming
      ? "Unrestricted or time-boxed authority is active"
      : "Authority changes require a Settings challenge"
  }
}
