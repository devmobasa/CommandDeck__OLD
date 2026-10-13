import QtQuick
import QtQuick.Layouts

// A transient confirmation pill ("Copied."). Call show(text); it fades in,
// holds for a moment, and fades out. It takes no focus and no clicks, so it
// can sit over any surface without changing the keyboard flow.
Item {
  id: root

  required property var theme
  property string message: ""
  property int holdMs: 2200
  readonly property bool shown: hold.running

  function show(text) {
    root.message = String(text || "")
    if (root.message.length === 0) return
    hold.restart()
  }

  implicitWidth: pill.implicitWidth
  implicitHeight: pill.implicitHeight
  opacity: root.shown ? 1 : 0
  visible: opacity > 0
  // Spec themes are plain objects without motion tokens; fall back to the
  // launcher's normal duration so the binding never reads undefined.
  Behavior on opacity {
    NumberAnimation { duration: root.theme.durationNormal !== undefined ? root.theme.durationNormal : 140 }
  }
  Accessible.role: Accessible.StaticText
  Accessible.name: root.message

  Timer {
    id: hold
    interval: root.holdMs
  }

  Rectangle {
    id: pill
    implicitWidth: row.implicitWidth + Math.round(24 * root.theme.scale)
    implicitHeight: row.implicitHeight + Math.round(14 * root.theme.scale)
    radius: height / 2
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.theme.withAlpha(root.theme.accent, 0.6)

    RowLayout {
      id: row
      anchors.centerIn: parent
      spacing: Math.round(8 * root.theme.scale)

      Text {
        text: "✓"
        color: root.theme.accent
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
        font.weight: Font.DemiBold
      }

      Text {
        textFormat: Text.PlainText
        text: root.message
        color: root.theme.text
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
      }
    }
  }
}
