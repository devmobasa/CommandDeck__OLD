import QtQuick
import QtTest
import "../../qml/common" as Deck

Item {
  width: 1280
  height: 720
  Deck.Theme { id: source; loadShellTokens: false }
  QtObject {
    id: host
    property var part_appTheme: source
    property var part_displayScaleStore: QtObject {
      property bool legacy: false
      property int legacyPercent: 100
    }
    property int launcherWidthPercent: 100
    property int launcherHeightPercent: 100
    property int displaySizePercent: 100
    property bool settingsOpen: false
    property bool extensionManagementOpen: false
  }
  Deck.WindowDisplay { id: display; host: host; width: 1280; height: 720 }
  Deck.SizingViewport {
    id: panel
    width: display.sizing.panelWidth
    height: display.sizing.panelHeight
    requestedWidth: display.sizing.contentWidth
    requestedHeight: display.sizing.contentHeight
    TextInput {
      id: search
      width: parent.width
      height: 46
      focus: true
      activeFocusOnTab: true
      KeyNavigation.backtab: footer.helpControl
      KeyNavigation.tab: footer.firstControl
    }
    Deck.FooterHintBar {
      id: footer
      anchors.bottom: parent.bottom
      theme: display.theme
      nextTabStop: search
      previousTabStop: search
    }
  }
  DeadlineTestCase {
    name: "SizingViewport_events"
    when: windowShown

    function visibleInViewport(item) {
      const point = item.mapToItem(panel.viewport, 0, 0)
      return point.x >= -0.1 && point.y >= -0.1
        && point.x + item.width <= panel.width + 0.1
        && point.y + item.height <= panel.height + 0.1
    }

    function test_resize_reveals_existing_footer_focus() {
      waitForValue(search, "activeFocus", true)
      keyClick(Qt.Key_Backtab)
      waitForValue(footer.helpControl, "activeFocus", true)
      verify(visibleInViewport(footer.helpControl))
      verify(!panel.viewport.interactive)

      display.height = 240
      waitForValue(panel.viewport, "interactive", true)
      waitForCondition(() => visibleInViewport(footer.helpControl))
      verify(footer.helpControl.activeFocus, "resizing must retain Help focus")

      display.height = 200
      waitForValue(display, "configuredSize", Qt.size(1280, 200))
      waitForCondition(() => visibleInViewport(footer.helpControl))
      verify(footer.helpControl.activeFocus, "further resizing reveals the same focused control")
      keyClick(Qt.Key_Tab)
      waitForValue(search, "activeFocus", true)
      waitForCondition(() => visibleInViewport(search))

      display.height = 720
      waitForValue(panel.viewport, "interactive", false)
      compare(panel.viewport.contentY, 0)
      verify(search.activeFocus)
    }
  }
}
