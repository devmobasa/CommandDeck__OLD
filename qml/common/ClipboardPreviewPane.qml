pragma ComponentBehavior: Bound
import QtQuick
import "Protocol.js" as Protocol
import QtQuick.Controls
import QtQuick.Layouts
import "ClipboardFormat.js" as Format

// Preview card for the selected entry. The header names the kind and copy time
// and, while a search is active, steps through text matches; the body is an
// inert plain-text or image view; the footer lists representation facts.
Rectangle {
  id: root

  required property var theme
  required property var controller
  property double nowSeconds: 0
  readonly property alias textControl: previewText
  readonly property alias imageControl: previewImage
  readonly property alias highlightControl: highlights
  readonly property alias previousMatchControl: previousMatch
  readonly property alias nextMatchControl: nextMatch
  readonly property alias matchLabelControl: matchLabel
  readonly property alias copiedControl: copiedLabel
  readonly property alias factsControl: facts

  readonly property var entry: root.controller.selected
  readonly property var preview: root.controller.preview
  readonly property var slice: root.preview && root.preview.slice ? root.preview.slice : null
  readonly property var presentation: root.preview && root.preview.presentation ? root.preview.presentation
    : root.entry && root.entry.presentation ? root.entry.presentation : null
  readonly property string kind: root.presentation && root.presentation.kind ? String(root.presentation.kind) : Protocol.ClipboardPresentationKinds.Text
  readonly property bool imagePreview: root.kind === Protocol.ClipboardPresentationKinds.Images
  readonly property int matchCount: root.slice && root.slice.matchCount ? root.slice.matchCount : 0
  readonly property int matchIndex: root.slice && typeof root.slice.matchIndex === "number" ? root.slice.matchIndex : -1
  readonly property bool searching: root.entry !== null && !root.imagePreview && root.controller.requestedQuery.trim().length > 0
  readonly property string matchText: root.matchCount > 0
    ? "Match " + (root.matchIndex + 1) + " of " + root.matchCount + (root.slice.matchCountLimited ? "+" : "")
    : root.preview !== null ? "No text matches" : ""
  readonly property string chipText: root.kind === Protocol.ClipboardPresentationKinds.Files && root.presentation && root.presentation.destinationCount > 1
    ? "FILES" : Format.kindName(root.kind).toUpperCase()
  readonly property string factsText: {
    const parts = []
    const presentation = root.presentation
    const preview = root.preview
    if (presentation && presentation.kind === Protocol.ClipboardPresentationKinds.Images && presentation.imageWidth)
      parts.push("PNG " + presentation.imageWidth + " × " + presentation.imageHeight)
    if (preview && preview.openDestination && (!root.slice || String(root.slice.text).trim() !== preview.openDestination))
      parts.push("Destination: " + preview.openDestination)
    else if (presentation && presentation.destinationCount > 1)
      parts.push(presentation.destinationCount + " references")
    if (presentation && presentation.reducedFidelity) parts.push("Some copied formats were not kept")
    if (preview && preview.extractedText) parts.push(preview.truncated ? "Text extracted from HTML, truncated" : "Text extracted from HTML")
    if (root.slice && (root.slice.hasTextBefore || root.slice.hasTextAfter)) parts.push("Showing part of a longer entry")

    const keywords = root.entry && root.entry.keywords ? root.entry.keywords : []
    if (keywords.length > 0) parts.push("Keywords: " + keywords.join(", "))
    return parts.join("  ·  ")
  }

  // Steps to the neighbouring match, wrapping at either end.
  function stepMatch(delta) {
    if (root.matchCount < 2 || root.matchIndex < 0) return false
    root.controller.requestPreview((root.matchIndex + delta + root.matchCount) % root.matchCount)
    return true
  }

  function revealSelectedRange() {
    const flick = textView.contentItem as Flickable
    if (!flick) return

    const slice = root.slice
    const range = slice && slice.ranges && typeof slice.selectedRange === "number" ? slice.ranges[slice.selectedRange] : null
    if (!range) {
      flick.contentY = 0
      return
    }

    const rectangle = previewText.positionToRectangle(range.startUtf16)
    const margin = rectangle.height * 2
    const maximum = Math.max(0, flick.contentHeight - flick.height)
    if (rectangle.y - margin < flick.contentY) flick.contentY = Math.max(0, rectangle.y - margin)
    else if (rectangle.y + rectangle.height + margin > flick.contentY + flick.height)
      flick.contentY = Math.min(maximum, rectangle.y + rectangle.height + margin - flick.height)
  }

  color: root.theme.surface
  radius: root.theme.radiusSmall
  border.width: previewText.activeFocus ? 2 : 1
  border.color: previewText.activeFocus ? root.theme.focusRing : root.theme.separator

  Connections {
    target: root.controller
    function onPreviewChanged() {
      const preview = root.controller.preview
      const slice = preview ? preview.slice : null
      if (slice && slice.ranges && slice.selectedRange !== null && slice.selectedRange !== undefined && slice.ranges[slice.selectedRange])
        previewText.cursorPosition = slice.ranges[slice.selectedRange].startUtf16
      Qt.callLater(root.revealSelectedRange)
    }
  }

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: 2
    spacing: 0

    RowLayout {
      Layout.fillWidth: true
      Layout.preferredHeight: Math.round(42 * root.theme.scale)
      Layout.leftMargin: Math.round(12 * root.theme.scale)
      Layout.rightMargin: Math.round(6 * root.theme.scale)
      spacing: Math.round(10 * root.theme.scale)

      Rectangle {
        visible: root.entry !== null
        implicitWidth: kindLabel.implicitWidth + Math.round(14 * root.theme.scale)
        implicitHeight: Math.round(root.theme.fontCaption * 2.1)
        radius: Math.max(3, root.theme.radiusSmall - 2)
        color: root.theme.withAlpha(root.theme.accent, 0.18)

        Text {
          id: kindLabel
          anchors.centerIn: parent
          text: root.chipText
          color: root.theme.accent
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontCaption
          font.weight: Font.DemiBold
          font.letterSpacing: 1
        }
      }

      Text {
        id: copiedLabel
        Layout.fillWidth: true
        Layout.minimumWidth: 0
        text: root.entry ? "Copied " + Format.copiedLabel(root.entry.copiedUnixSeconds, root.nowSeconds) : ""
        textFormat: Text.PlainText
        elide: Text.ElideRight
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
      }

      Text {
        id: matchLabel
        visible: root.searching && text.length > 0
        text: root.matchText
        color: root.matchCount > 0 ? root.theme.text : root.theme.textFaint
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
      }

      ClipboardActionButton {
        id: previousMatch
        visible: root.searching
        enabled: root.matchCount > 1
        theme: root.theme
        text: "‹"
        toolTipText: "Previous match · Ctrl+Shift+G"
        Accessible.name: "Previous match"
        onClicked: root.stepMatch(-1)
      }

      ClipboardActionButton {
        id: nextMatch
        visible: root.searching
        enabled: root.matchCount > 1
        theme: root.theme
        text: "›"
        toolTipText: "Next match · Ctrl+G"
        Accessible.name: "Next match"
        onClicked: root.stepMatch(1)
      }
    }

    Rectangle {
      Layout.fillWidth: true
      Layout.preferredHeight: 1
      color: root.theme.separator
    }

    Item {
      Layout.fillWidth: true
      Layout.fillHeight: true

      ScrollView {
        id: textView
        anchors.fill: parent
        visible: !root.imagePreview
        clip: true

        TextArea {
          id: previewText
          text: root.slice ? root.slice.text : ""
          textFormat: TextEdit.PlainText
          readOnly: true
          selectByMouse: true
          wrapMode: TextEdit.Wrap
          padding: Math.round(14 * root.theme.scale)
          color: root.theme.text
          selectionColor: root.theme.withAlpha(root.theme.accent, 0.4)
          selectedTextColor: root.theme.text
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontBody
          Accessible.name: "Clipboard text preview"
          background: null

          Repeater {
            id: highlights
            model: root.slice && root.slice.ranges ? root.slice.ranges : []
            delegate: ClipboardMatchOverlay {
              required property var modelData
              required property int index
              target: previewText
              start: modelData.startUtf16
              length: modelData.lengthUtf16
              selected: root.slice !== null && root.slice.selectedRange === index
              color: root.theme.withAlpha(root.theme.accent, 0.22)
              selectedColor: root.theme.withAlpha(root.theme.accent, 0.5)
            }
          }
        }
      }

      Item {
        anchors.fill: parent
        anchors.margins: Math.round(14 * root.theme.scale)
        visible: root.imagePreview

        Image {
          id: previewImage
          anchors.fill: parent
          fillMode: Image.PreserveAspectFit
          smooth: true
          cache: false
          source: root.preview && root.preview.imageSource ? root.preview.imageSource : ""
          Accessible.name: "Clipboard image preview"
        }

        Rectangle {
          visible: previewImage.status === Image.Ready
          anchors.centerIn: previewImage
          width: previewImage.paintedWidth + 2
          height: previewImage.paintedHeight + 2
          color: "transparent"
          border.width: 1
          border.color: root.theme.separator
        }

        Text {
          anchors.centerIn: parent
          visible: root.preview !== null && String(previewImage.source).length === 0
          text: "Image preview unavailable"
          color: root.theme.textFaint
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontSmall
        }
      }
    }

    Rectangle {
      Layout.fillWidth: true
      Layout.preferredHeight: 1
      visible: facts.visible
      color: root.theme.separator
    }

    Text {
      id: facts
      Layout.fillWidth: true
      Layout.leftMargin: Math.round(12 * root.theme.scale)
      Layout.rightMargin: Math.round(12 * root.theme.scale)
      Layout.topMargin: Math.round(8 * root.theme.scale)
      Layout.bottomMargin: Math.round(8 * root.theme.scale)
      visible: text.length > 0
      text: root.factsText
      textFormat: Text.PlainText
      elide: Text.ElideMiddle
      color: root.theme.textMuted
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
    }
  }
}
