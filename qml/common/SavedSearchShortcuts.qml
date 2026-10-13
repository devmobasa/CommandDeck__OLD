pragma ComponentBehavior: Bound
import QtQuick

Item {
  id: root
  required property var searches
  property bool active: false
  signal requested(var search)

  Repeater {
    model: root.searches || []
    Item {
      id: shortcutDelegate
      required property var modelData
      readonly property var savedSearch: modelData || null
      // Persistence uses Hyprland's SUPER spelling; Qt names that modifier META.
      readonly property string shortcutSequence: savedSearch && savedSearch.shortcut
        ? savedSearch.shortcut.replace(/(^|\+)SUPER(?=\+)/gi, "$1META")
        : ""
      width: 0
      height: 0

      Shortcut {
        sequence: shortcutDelegate.shortcutSequence
        enabled: root.active && shortcutDelegate.shortcutSequence.length > 0
          && !/^CTRL\+[1-9]$/i.test(shortcutDelegate.shortcutSequence)
        context: Qt.WindowShortcut
        onActivated: root.requested(shortcutDelegate.savedSearch)
      }
    }
  }
}
