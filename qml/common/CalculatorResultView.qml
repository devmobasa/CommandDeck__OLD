import QtQuick
import QtQuick.Layouts

ColumnLayout {
  id: root

  required property var theme
  property string expression: ""
  property string value: ""
  property string exactValue: ""
  // The result's primary action title, so the button and the footer agree.
  property string actionTitle: ""
  signal copyRequested()

  readonly property bool stacked: width < Math.round(400 * theme.scale)
  spacing: Math.round(24 * theme.scale)

  Text {
    text: "Calculator"
    color: root.theme.textMuted
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
    font.weight: Font.DemiBold
  }

  GridLayout {
    Layout.fillWidth: true
    Layout.minimumHeight: Math.round(180 * root.theme.scale)
    columns: root.stacked ? 1 : 3
    columnSpacing: Math.round(24 * root.theme.scale)
    rowSpacing: Math.round(16 * root.theme.scale)

    ColumnLayout {
      Layout.fillWidth: true
      Layout.preferredWidth: 1
      spacing: Math.round(12 * root.theme.scale)

      Text {
        objectName: "calculatorExpression"
        Layout.fillWidth: true
        text: root.expression
        textFormat: Text.PlainText
        wrapMode: Text.Wrap
        horizontalAlignment: Text.AlignHCenter
        color: root.theme.text
        font.family: "monospace"
        font.pixelSize: Math.round(root.theme.fontHeading * 1.5)
        Accessible.name: "Expression: " + text
      }

      KeyCap {
        Layout.alignment: Qt.AlignHCenter
        theme: root.theme
        label: "Expression"
      }
    }

    Text {
      Layout.alignment: Qt.AlignCenter
      text: root.stacked ? "↓" : "→"
      color: root.theme.textFaint
      font.pixelSize: Math.round(root.theme.fontHeading * 1.5)
      Accessible.ignored: true
    }

    ColumnLayout {
      Layout.fillWidth: true
      Layout.preferredWidth: 1
      spacing: Math.round(12 * root.theme.scale)

      Text {
        objectName: "calculatorValue"
        Layout.fillWidth: true
        text: root.value
        textFormat: Text.PlainText
        wrapMode: Text.Wrap
        horizontalAlignment: Text.AlignHCenter
        color: root.theme.accent
        font.family: "monospace"
        font.pixelSize: Math.round(root.theme.fontHeading * 1.5)
        font.weight: Font.DemiBold
        Accessible.name: "Result: " + text
      }

      KeyCap {
        Layout.alignment: Qt.AlignHCenter
        theme: root.theme
        label: "Result"
      }
    }
  }

  Text {
    objectName: "calculatorExactValue"
    Layout.fillWidth: true
    visible: root.exactValue.length > 0
    text: "Exact value: " + root.exactValue
    textFormat: Text.PlainText
    wrapMode: Text.Wrap
    color: root.theme.textMuted
    font.family: "monospace"
    font.pixelSize: root.theme.fontBody
  }

  DeckButton {
    id: copyButton
    theme: root.theme
    compact: true
    objectName: "calculatorCopyButton"
    Layout.alignment: Qt.AlignHCenter
    text: root.actionTitle
    visible: root.actionTitle.length > 0
    enabled: root.value.length > 0
    focusPolicy: Qt.NoFocus
    Accessible.description: "Press Enter in the search field to copy the result."
    onClicked: root.copyRequested()
  }
}
