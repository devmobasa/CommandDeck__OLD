pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Layouts

// C10 - one-time first-run coach.
//
// A dismissible overlay shown once, pointing (in words + light callouts) at the
// search field, the filters ("?" / prefixes), and F1 help. Any key, any click,
// or "Got it" dismisses it; the owner then persists the "seen" flag durably.
// It never shows while a query is present or another surface (assistant,
// settings, palette, …) is open.
Rectangle {
  id: root

  required property var theme
  property bool opened: false
  property bool seen: false
  property bool suppressed: false

  signal dismissed()

  readonly property bool shouldShow: root.opened && !root.seen && !root.suppressed

  function dismiss() {
    root.dismissed()
  }

  visible: root.shouldShow
  color: root.theme.withAlpha(root.theme.background, 0.86)
  focus: visible
  activeFocusOnTab: true
  KeyNavigation.tab: root
  KeyNavigation.backtab: root
  Accessible.role: Accessible.Dialog
  Accessible.name: "Welcome to CommandDeck"

  onVisibleChanged: {
    if (visible) Qt.callLater(function() { root.forceActiveFocus() })
  }

  // Dismiss on any key.
  Keys.onPressed: event => {
    root.dismiss()
    event.accepted = true
  }

  // Dismiss on any click.
  MouseArea {
    anchors.fill: parent
    acceptedButtons: Qt.AllButtons
    onClicked: root.dismiss()
  }

  Rectangle {
    width: Math.min(Math.round(460 * root.theme.scale), parent.width - 40)
    implicitHeight: coachColumn.implicitHeight + Math.round(2 * root.theme.panelPadding)
    height: implicitHeight
    anchors.centerIn: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.theme.border

    // Keep clicks on the card from double-firing the scrim dismiss handler; the
    // "Got it" button drives the dismiss explicitly.
    MouseArea {
      anchors.fill: parent
      acceptedButtons: Qt.AllButtons
    }

    ColumnLayout {
      id: coachColumn
      anchors.left: parent.left
      anchors.right: parent.right
      anchors.top: parent.top
      anchors.margins: root.theme.panelPadding
      spacing: root.theme.gap

      Text {
        text: "Welcome to CommandDeck"
        color: root.theme.text
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontHeading
        font.weight: Font.DemiBold
      }

      Text {
        Layout.fillWidth: true
        text: "Three things to know:"
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
        wrapMode: Text.Wrap
      }

      Repeater {
        model: [
          { badge: "⌕", title: "Search", body: "Type to find apps, files, windows, and commands." },
          { badge: "?", title: "Filters", body: "Type ? to browse filters, or a prefix like file:." },
          { badge: "F1", title: "Help", body: "Press F1 for every shortcut and filter." }
        ]

        delegate: RowLayout {
          id: coachRow
          required property var modelData
          Layout.fillWidth: true
          spacing: Math.round(10 * root.theme.scale)

          Rectangle {
            Layout.alignment: Qt.AlignTop
            implicitWidth: Math.round(28 * root.theme.scale)
            implicitHeight: Math.round(28 * root.theme.scale)
            radius: root.theme.radiusSmall
            color: root.theme.withAlpha(root.theme.accent, 0.14)
            border.width: 1
            border.color: root.theme.withAlpha(root.theme.accent, 0.5)

            Text {
              anchors.centerIn: parent
              text: String(coachRow.modelData.badge)
              color: root.theme.accent
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontSmall
              font.weight: Font.DemiBold
            }
          }

          ColumnLayout {
            Layout.fillWidth: true
            spacing: 1

            Text {
              text: String(coachRow.modelData.title)
              color: root.theme.text
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontSmall
              font.weight: Font.DemiBold
            }

            Text {
              Layout.fillWidth: true
              text: String(coachRow.modelData.body)
              color: root.theme.textMuted
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontSmall
              wrapMode: Text.Wrap
            }
          }
        }
      }

      DeckButton {
        id: gotItButton
        theme: root.theme
        kind: Ui.ButtonVariant.Primary
        compact: true
        Layout.alignment: Qt.AlignRight
        Layout.topMargin: Math.round(4 * root.theme.scale)
        text: "Got it"
        Accessible.name: "Dismiss the welcome coach"
        onClicked: root.dismiss()


      }
    }
  }
}
