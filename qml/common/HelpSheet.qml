pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

// Keyboard and filter reference.
//
// Every in-overlay chord is invisible without this: the footer only names the
// primary action, and the row badges only hint at Ctrl+N. Effective compositor
// launcher hotkeys remain in Settings. The filter half is built from daemon
// descriptors, so it cannot drift from SearchFilterRegistry.
Rectangle {
  id: root

  required property var theme
  property bool opened: false
  property var filterDescriptors: []
  property string clipboardShortcut: ""
  property Item returnFocusTarget: null
  property bool focusReturnAllowed: true

  signal dismissed()

  readonly property var shortcutSections: [
    {
      title: "Navigate",
      items: [
        { keys: ["↑", "↓"], text: "Move selection" },
        { keys: ["Ctrl+P", "Ctrl+N"], text: "Move selection" },
        { keys: ["PgUp", "PgDn"], text: "Move by eight" },
        { keys: ["Ctrl+Home", "Ctrl+End"], text: "First or last result" },
        { keys: ["Ctrl+↑", "Ctrl+↓"], text: "Previous or next section" }
      ]
    },
    {
      title: "Open",
      items: [
        { keys: ["↵"], text: "Run the primary action" },
        { keys: ["Ctrl+1", "Ctrl+9"], text: "Run the numbered result" },
        { keys: ["Tab"], text: "Complete a filter hint or the selected result" }
      ]
    },
    {
      title: "Actions",
      items: [
        { keys: ["→"], text: "Open actions, caret at end of query" },
        { keys: ["Ctrl+K"], text: "Open actions" },
        { keys: ["Ctrl+↵"], text: "Run the second action, or Reveal for files" },
        { keys: ["Ctrl+Shift+↵"], text: "Run the third action" },
        { keys: ["Ctrl+Shift+C"], text: "Copy the file path, or the result's first Copy action" },
        { keys: ["Ctrl+D"], text: "Toggle bookmark for a customizable result" },
        { keys: ["Alt+Shift+A"], text: "Set or edit the result's alias" },
        { keys: ["Alt+Shift+D"], text: "Don't show this result" },
        { keys: ["←", "Esc"], text: "Close the action panel" }
      ]
    },
    {
      title: "Filters",
      items: [
        { keys: ["⌫"], text: "Remove the filter on an empty query" },
        { keys: ["Ctrl+U"], text: "Delete the query before the caret" }
      ]
    },
    {
      title: "General",
      items: [
        { keys: ["Ctrl+,"], text: "Settings" },
        { keys: clipboardShortcut ? [root.chordLabel(clipboardShortcut)] : [], text: clipboardShortcut ? "Clipboard History" : "Clipboard History shortcut disabled" },
        { keys: ["F1", "Ctrl+/"], text: "This sheet" },
        { keys: ["Esc"], text: "Close CommandDeck" }
      ]
    }
  ]

  readonly property int renderedFilterCount: filterRepeater.count
  readonly property int renderedSectionCount: sectionRepeater.count

  function filterTokens(descriptor) {
    const aliases = descriptor && descriptor.aliases ? descriptor.aliases : []
    return [descriptor.token].concat(aliases).join("  ")
  }

  function renderedFilterTokensAt(index) {
    const item = root.filterItemAt(index)
    return item ? item["tokens"] : ""
  }

  function filterItemAt(index) {
    return index >= 0 && index < filterRepeater.count
      ? filterRepeater.itemAt(index)
      : null
  }

  // Preferences store chords in canonical upper case (CTRL+SHIFT+V); show them
  // the way every other key in the sheet reads (Ctrl+Shift+V).
  function chordLabel(chord) {
    return String(chord).split("+")
      .map(part => part.length > 1 ? part.charAt(0) + part.slice(1).toLowerCase() : part)
      .join("+")
  }

  function shortcutListed(chord) {
    const wanted = String(chord).toUpperCase()
    for (let sectionIndex = 0; sectionIndex < shortcutSections.length; sectionIndex++) {
      const items = shortcutSections[sectionIndex].items
      for (let itemIndex = 0; itemIndex < items.length; itemIndex++) {
        if (items[itemIndex].keys.some(key => String(key).toUpperCase() === wanted)) return true
      }
    }
    return false
  }

  visible: root.opened
  color: root.theme.scrim
  focus: visible
  activeFocusOnTab: true
  KeyNavigation.tab: root
  KeyNavigation.backtab: root
  Accessible.role: Accessible.Dialog
  Accessible.name: "Keyboard shortcuts and search filters"

  onVisibleChanged: {
    if (visible) Qt.callLater(function() { root.forceActiveFocus() })
    else if (root.returnFocusTarget) {
      Qt.callLater(function() {
        if (!root.focusReturnAllowed) return
        const target = root.returnFocusTarget
        if (target && target.visible && target.enabled) target.forceActiveFocus()
      })
    }
  }

  Keys.onPressed: event => {
    const control = (event.modifiers & Qt.ControlModifier) !== 0
    if (event.key === Qt.Key_Tab || event.key === Qt.Key_Backtab) {
      root.forceActiveFocus()
      event.accepted = true
    } else if (event.key === Qt.Key_Escape || event.key === Qt.Key_F1
               || (control && event.key === Qt.Key_Slash)) {
      root.dismissed()
      event.accepted = true
    }
  }

  // Swallow clicks so the scrim cannot reach the results behind the sheet.
  MouseArea {
    anchors.fill: parent
    acceptedButtons: Qt.AllButtons
    hoverEnabled: true
    onClicked: root.dismissed()
  }

  Rectangle {
    width: Math.min(Math.round(760 * root.theme.scale), parent.width - 48)
    height: Math.min(Math.round(560 * root.theme.scale), parent.height - 48)
    anchors.centerIn: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.theme.border

    MouseArea {
      anchors.fill: parent
      acceptedButtons: Qt.AllButtons
    }

    ColumnLayout {
      anchors.fill: parent
      anchors.margins: root.theme.panelPadding
      spacing: root.theme.gap

      RowLayout {
        Layout.fillWidth: true

        Text {
          Layout.fillWidth: true
          text: "Keyboard & filters"
          color: root.theme.text
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontHeading
          font.weight: Font.DemiBold
        }

        KeyCap {
          theme: root.theme
          label: "Esc"
        }
      }

      Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 1
        color: root.theme.separator
      }

      ScrollView {
        id: helpScroll

        Layout.fillWidth: true
        Layout.fillHeight: true
        clip: true
        // Not `parent.width` on the content: inside a ScrollView the parent is
        // the Flickable content item, which sizes to its content.
        contentWidth: availableWidth

        RowLayout {
          width: helpScroll.availableWidth
          spacing: Math.round(24 * root.theme.scale)

          ColumnLayout {
            Layout.fillWidth: true
            Layout.alignment: Qt.AlignTop
            spacing: Math.round(14 * root.theme.scale)

            Repeater {
              id: sectionRepeater
              model: root.shortcutSections

              delegate: ColumnLayout {
                id: section

                required property var modelData

                Layout.fillWidth: true
                spacing: Math.round(5 * root.theme.scale)

                Text {
                  text: section.modelData.title.toUpperCase()
                  color: root.theme.textFaint
                  font.family: root.theme.fontFamily
                  font.pixelSize: root.theme.fontCaption
                  font.letterSpacing: 0.8
                  font.weight: Font.DemiBold
                }

                Repeater {
                  model: section.modelData.items

                  delegate: RowLayout {
                    id: shortcutRow

                    required property var modelData

                    Layout.fillWidth: true
                    spacing: Math.round(6 * root.theme.scale)

                    Repeater {
                      model: shortcutRow.modelData.keys

                      delegate: KeyCap {
                        required property var modelData

                        theme: root.theme
                        label: modelData
                      }
                    }

                    Text {
                      Layout.fillWidth: true
                      Layout.leftMargin: Math.round(4 * root.theme.scale)
                      text: shortcutRow.modelData.text
                      color: root.theme.textMuted
                      font.family: root.theme.fontFamily
                      font.pixelSize: root.theme.fontSmall
                      elide: Text.ElideRight
                    }
                  }
                }
              }
            }
          }

          ColumnLayout {
            Layout.fillWidth: true
            Layout.alignment: Qt.AlignTop
            spacing: Math.round(5 * root.theme.scale)

            Text {
              text: "FILTERS"
              color: root.theme.textFaint
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontCaption
              font.letterSpacing: 0.8
              font.weight: Font.DemiBold
            }

            Repeater {
              id: filterRepeater
              model: root.filterDescriptors

              delegate: RowLayout {
                id: filterRow

                required property var modelData
                readonly property string tokens: root.filterTokens(filterRow.modelData)

                Layout.fillWidth: true
                spacing: Math.round(8 * root.theme.scale)

                Text {
                  Layout.preferredWidth: Math.round(150 * root.theme.scale)
                  textFormat: Text.PlainText
                  text: filterRow.modelData.label || ""
                  color: root.theme.textMuted
                  font.family: root.theme.fontFamily
                  font.pixelSize: root.theme.fontSmall
                  elide: Text.ElideRight
                }

                Text {
                  Layout.fillWidth: true
                  textFormat: Text.PlainText
                  text: filterRow.tokens
                  color: root.theme.accent
                  font.family: "monospace"
                  font.pixelSize: root.theme.fontSmall
                  elide: Text.ElideRight
                }
              }
            }

            Text {
              Layout.fillWidth: true
              Layout.topMargin: Math.round(10 * root.theme.scale)
              visible: root.renderedFilterCount === 0
              text: "Filters load when the daemon connects."
              color: root.theme.textFaint
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontSmall
              wrapMode: Text.Wrap
            }
          }
        }
      }
    }
  }
}
