pragma ComponentBehavior: Bound
import QtQuick
import "MatchHighlight.js" as MatchHighlight
import "Protocol.js" as Protocol
import QtQuick.Layouts

Column {
  id: root

  required property var theme
  property var matches: []
  readonly property int renderedCount: matchRepeater.count

  width: parent ? parent.width : implicitWidth
  spacing: Math.round(9 * root.theme.scale)

  function labelFor(field) {
    const labels = {
      path: "Matched path",
      metadata: "Matched metadata",
      content: "Matched content",
      config: "Matched configuration"
    }
    return labels[field] || "Matched " + String(field || "field")
  }

  function renderedMarkupAt(index) {
    const item = root.matchItemAt(index)
    return item ? item.renderedMarkup : ""
  }

  function matchItemAt(index) {
    return matchRepeater.itemAt(index)
  }

  Repeater {
    id: matchRepeater
    model: root.matches || []

    delegate: RowLayout {
      id: matchRow

      required property var modelData
      readonly property string renderedMarkup: valueText.text

      width: root.width
      spacing: Math.round(12 * root.theme.scale)

      Text {
        Layout.preferredWidth: Math.round(92 * root.theme.scale)
        Layout.alignment: Qt.AlignTop
        text: root.labelFor(matchRow.modelData.field)
        color: root.theme.textFaint
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
        wrapMode: Text.Wrap
      }

      Text {
        id: valueText
        Layout.fillWidth: true
        Layout.alignment: Qt.AlignTop
        text: MatchHighlight.highlighted(matchRow.modelData.value, matchRow.modelData.ranges, root.theme.accent)
        textFormat: Text.StyledText
        color: root.theme.textMuted
        font.family: matchRow.modelData.field === Protocol.RankingFieldNames.Path
          || matchRow.modelData.field === Protocol.RankingFieldNames.Config
          ? "monospace"
          : root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
        wrapMode: Text.WrapAnywhere
        Accessible.role: Accessible.StaticText
        Accessible.name: root.labelFor(matchRow.modelData.field)
          + ", " + String(matchRow.modelData.value || "")
      }
    }
  }
}
