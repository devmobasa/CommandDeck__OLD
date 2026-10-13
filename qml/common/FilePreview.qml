pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

ColumnLayout {
  id: root

  required property var theme
  property var preview: null
  property bool loading: false
  property string error: ""
  property Item typingTarget: null
  property Item previousTabStop: null
  property Item nextTabStop: null
  readonly property bool isImage: !!(root.preview && root.preview.imageSource)
  signal matchRequested(int index)
  signal openRequested()
  readonly property int matchCount: !root.isImage && root.preview ? root.preview.matchCount || 0 : 0
  readonly property int matchIndex: root.preview ? root.preview.matchIndex || 0 : 0
  readonly property Item firstControl: matchCount > 0 ? previousButton : null
  readonly property Item lastControl: matchCount > 0 ? openButton : null
  Keys.forwardTo: typingTarget ? [typingTarget] : []

  component PreviewButton: Button {
    id: button
    focusPolicy: Qt.TabFocus
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontSmall
    Accessible.name: text
    Keys.onReturnPressed: clicked()
    Keys.onEnterPressed: clicked()
    background: Rectangle {
      radius: root.theme.radiusSmall
      color: root.theme.surfaceStrong
      border.width: button.activeFocus ? 2 : 1
      border.color: button.activeFocus ? root.theme.text : root.theme.border
    }
    contentItem: Text {
      text: button.text
      color: root.theme.text
      font: button.font
      horizontalAlignment: Text.AlignHCenter
      verticalAlignment: Text.AlignVCenter
    }
  }

  readonly property string contentText: root.preview && root.preview.text
    ? root.preview.text
    : ""
  readonly property string language: root.preview && root.preview.language
    ? root.preview.language
    : "text"
  readonly property bool truncated: root.preview
    && root.preview.truncated === true
  readonly property bool hasContent: root.loading || root.error.length > 0
    || root.preview !== null

  spacing: Math.round(8 * root.theme.scale)
  visible: root.hasContent

  RowLayout {
    Layout.fillWidth: true
    visible: root.matchCount > 0

    PreviewButton {
      id: previousButton
      objectName: "previousPreviewMatch"
      text: "Previous"
      KeyNavigation.backtab: root.previousTabStop
      KeyNavigation.tab: nextButton
      KeyNavigation.left: openButton
      KeyNavigation.right: nextButton
      onClicked: if (!root.loading) root.matchRequested((root.matchIndex + root.matchCount - 1) % root.matchCount)
    }
    PreviewButton {
      id: nextButton
      objectName: "nextPreviewMatch"
      text: "Next"
      KeyNavigation.backtab: previousButton
      KeyNavigation.tab: openButton
      KeyNavigation.left: previousButton
      KeyNavigation.right: openButton
      onClicked: if (!root.loading) root.matchRequested((root.matchIndex + 1) % root.matchCount)
    }
    PreviewButton {
      id: openButton
      objectName: "openPreviewMatch"
      text: "Open line"
      KeyNavigation.backtab: nextButton
      KeyNavigation.tab: root.nextTabStop
      KeyNavigation.left: nextButton
      KeyNavigation.right: previousButton
      onClicked: if (!root.loading) root.openRequested()
    }
  }

  Text {
    Layout.fillWidth: true
    visible: root.preview !== null
    text: (root.matchCount > 0
      ? "Matching line " + (root.matchIndex + 1) + " of " + root.matchCount
      : root.isImage ? "Image preview" : "File preview") + (root.preview && root.preview.scanTruncated ? " · Scan limited" : "")
    color: root.theme.textFaint
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontSmall
    wrapMode: Text.Wrap
  }

  RowLayout {
    Layout.fillWidth: true
    visible: root.preview !== null

    Text {
      Layout.fillWidth: true
      textFormat: Text.PlainText
      text: root.language + (root.isImage ? " · " + root.preview.imageWidth + " × " + root.preview.imageHeight : "")
      color: root.theme.textFaint
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
      font.capitalization: Font.AllUppercase
    }

    Text {
      visible: root.truncated
      text: "Preview truncated"
      color: root.theme.textFaint
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
    }
  }

  Text {
    Layout.fillWidth: true
    visible: root.loading
    text: "Loading preview…"
    color: root.theme.textFaint
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
  }

  Text {
    Layout.fillWidth: true
    visible: !root.loading && root.error.length > 0
    text: root.error
    color: root.theme.textFaint
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
    wrapMode: Text.Wrap
    textFormat: Text.PlainText
  }

  ImageFilePreview {
    Layout.fillWidth: true
    visible: root.isImage && !root.loading && root.error.length === 0
    theme: root.theme
    imageSource: visible ? root.preview.imageSource : ""
    imageWidth: root.isImage ? root.preview.imageWidth : 0
    imageHeight: root.isImage ? root.preview.imageHeight : 0
  }

  Rectangle {
    Layout.fillWidth: true
    implicitHeight: previewLines.implicitHeight + Math.round(20 * root.theme.scale)
    visible: root.preview !== null && !root.isImage
    color: root.theme.surfaceStrong
    radius: root.theme.radiusSmall

    ColumnLayout {
      id: previewLines
      anchors.left: parent.left
      anchors.right: parent.right
      anchors.top: parent.top
      anchors.margins: Math.round(10 * root.theme.scale)
      spacing: 0
      Repeater {
        model: root.contentText.split("\n")
        Rectangle {
          id: lineRow
          required property int index
          required property string modelData
          readonly property int lineNumber: (root.preview ? root.preview.startLine || 1 : 1) + index
          Layout.fillWidth: true
          implicitHeight: lineText.implicitHeight
          color: root.preview && root.preview.matchLine === lineNumber ? root.theme.selectedBackground : "transparent"

          RowLayout {
            anchors.left: parent.left
            anchors.right: parent.right
            spacing: 8
            Text {
              text: lineRow.lineNumber
              color: root.theme.textFaint
              font.family: "monospace"
              font.pixelSize: root.theme.fontSmall
              Layout.alignment: Qt.AlignTop
              Layout.preferredWidth: 36 * root.theme.scale
              horizontalAlignment: Text.AlignRight
            }
            Text {
              id: lineText
              Layout.fillWidth: true
              text: lineRow.modelData || " "
              color: root.theme.textMuted
              font.family: "monospace"
              font.pixelSize: root.theme.fontBody
              wrapMode: Text.WrapAnywhere
              textFormat: Text.PlainText
            }
          }
        }
      }
    }
  }
}
