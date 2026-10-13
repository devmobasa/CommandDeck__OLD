pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "ClipboardFormat.js" as Format

// Stands in for the list and preview when a page has no entries, and says what
// to do next: turn capture on, resume it, change the search, or copy something.
Item {
  id: root

  required property var theme
  required property var controller
  readonly property alias actionControl: actionButton
  readonly property alias titleControl: title
  readonly property alias bodyControl: body
  signal enableRequested(Item invoker)
  signal resumeRequested(Item invoker)

  readonly property var policy: root.controller.settings && root.controller.settings.policy ? root.controller.settings.policy : null
  readonly property string query: root.controller.requestedQuery.trim()
  readonly property bool filtered: root.query.length > 0 || root.controller.pinnedOnly || root.controller.kindFilter.length > 0
  readonly property string mode: !root.controller.pageLoaded ? "loading"
    : root.filtered ? "filtered"
    : root.policy && root.policy.consent === "None" ? "disabled"
    : root.policy && root.policy.paused ? "paused"
    : "empty"
  readonly property string titleText: {
    if (root.mode === "loading") return "Loading clipboard history…"
    if (root.mode === "filtered") return root.query.length > 0 ? "No entries match “" + root.query + "”" : root.filterTitle()
    if (root.mode === "disabled") return "Clipboard history is off"
    if (root.mode === "paused") return "Capture is paused"
    return "Nothing copied yet"
  }
  readonly property string bodyText: {
    if (root.mode === "filtered")
      return root.query.length > 0 ? "Try other words, or press Esc to clear the search." : "Press Esc to show all entries."
    if (root.mode === "disabled")
      return "Turn on capture to keep a searchable history of the text you copy. History is stored locally, and you can pause or clear it at any time."
    if (root.mode === "paused") return "New copies are not saved while capture is paused."
    if (root.mode === "empty") return "Copy some text and it appears here."
    return ""
  }

  function filterTitle() {
    const kind = root.controller.kindFilter.length > 0 ? root.controller.kindLabel(root.controller.kindFilter).toLowerCase() : ""
    if (root.controller.pinnedOnly) return kind.length > 0 ? "No pinned " + kind : "No pinned entries"
    return "No " + kind + " in history"
  }

  Accessible.role: Accessible.StaticText
  Accessible.name: root.titleText + (root.bodyText.length > 0 ? " " + root.bodyText : "")

  ColumnLayout {
    anchors.centerIn: parent
    width: Math.max(0, Math.min(parent.width - root.theme.panelPadding * 2, Math.round(440 * root.theme.scale)))
    spacing: Math.round(10 * root.theme.scale)

    Rectangle {
      Layout.alignment: Qt.AlignHCenter
      Layout.bottomMargin: Math.round(4 * root.theme.scale)
      visible: root.mode !== "loading"
      implicitWidth: Math.round(46 * root.theme.scale)
      implicitHeight: implicitWidth
      radius: root.theme.radiusSmall + 2
      color: root.theme.withAlpha(root.theme.accent, actionButton.visible ? 0.2 : 0.1)

      Text {
        anchors.centerIn: parent
        text: Format.kindGlyph(root.controller.kindFilter)
        color: root.theme.accent
        font.family: root.theme.fontFamily
        font.pixelSize: Math.round(root.theme.fontHeading * 1.3)
        font.weight: Font.DemiBold
      }
    }

    Text {
      id: title
      Layout.fillWidth: true
      text: root.titleText
      textFormat: Text.PlainText
      horizontalAlignment: Text.AlignHCenter
      wrapMode: Text.Wrap
      color: root.theme.text
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontTitle
      font.weight: Font.DemiBold
    }

    Text {
      id: body
      Layout.fillWidth: true
      visible: text.length > 0
      text: root.bodyText
      textFormat: Text.PlainText
      horizontalAlignment: Text.AlignHCenter
      wrapMode: Text.Wrap
      lineHeight: 1.15
      color: root.theme.textMuted
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
    }

    ClipboardActionButton {
      id: actionButton
      Layout.alignment: Qt.AlignHCenter
      Layout.topMargin: Math.round(6 * root.theme.scale)
      visible: root.mode === "disabled" || root.mode === "paused"
      enabled: root.policy !== null
      theme: root.theme
      tone: "primary"
      text: root.mode === "disabled" ? "Enable capture…" : "Resume capture"
      onClicked: {
        const invoker = actionButton.activeFocus ? actionButton : null
        if (root.mode === "disabled") root.enableRequested(invoker)
        else root.resumeRequested(invoker)
      }
    }
  }
}
