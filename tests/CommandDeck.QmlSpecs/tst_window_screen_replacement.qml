import QtQuick
import "../../qml/common" as Deck

Item {
  Deck.Theme { id: source; loadShellTokens: false }
  QtObject {
    id: fixtureHost
    property var part_appTheme: source
    property var part_displayScaleStore: QtObject {
      property bool legacy: true
      property int legacyPercent: 100
    }
    property var compositor: QtObject {
      property var monitors: [{ name: "CDREUSE", height: 1800 }]
    }
    property int launcherWidthPercent: 100
    property int launcherHeightPercent: 100
    property int displaySizePercent: 100
    property bool settingsOpen: false
    property bool extensionManagementOpen: false
  }
  QtObject {
    id: original
    property string name: "CDREUSE"
    property int width: 1440
    property int height: 900
    property real devicePixelRatio: 2
    property int orientation: Qt.LandscapeOrientation
  }
  QtObject {
    id: replacement
    property string name: "CDREUSE"
    property int width: 1280
    property int height: 800
    property real devicePixelRatio: 2
    property int orientation: Qt.LandscapeOrientation
  }
  QtObject {
    id: fractional
    property string name: "HDMI-A-1"
    property int width: 1920
    property int height: 1200
    property real devicePixelRatio: 2
    property int orientation: Qt.LandscapeOrientation
  }
  QtObject {
    id: rotated
    property int width: 1080
    property int height: 1920
    property int orientation: Qt.PortraitOrientation
  }
  QtObject {
    id: fixtureWindow
    property var modelData: original
    property var screen: original
    property real width: screen.width
    property real height: screen.height
    property real devicePixelRatio: 2
  }
  Deck.WindowDisplay { id: display; host: fixtureHost; window: fixtureWindow }

  DeadlineTestCase {
    name: "WindowScreenReplacement"

    function init() {
      fixtureWindow.screen = original
      fixtureWindow.devicePixelRatio = 2
      replacement.name = "CDREUSE"
      replacement.devicePixelRatio = 2
      rotated.width = 1080
      rotated.height = 1920
      fixtureHost.part_displayScaleStore.legacy = true
      waitForValue(display, "configuredSize", Qt.size(1440, 900))
    }

    function test_replacement_updates_compatibility_metrics_data() {
      return [{ tag: "same-output-name", name: "CDREUSE" },
        { tag: "window-changes-screen", name: "HDMI-A-1" }]
    }

    function test_replacement_updates_compatibility_metrics(data) {
      compare(display.legacyHeight, 1800)
      compare(display.theme.rowHeight, 61)
      compare(display.theme.fontBody, 14)

      replacement.name = data.name
      fixtureWindow.screen = replacement
      waitForValue(display, "configuredSize", Qt.size(1280, 800))

      compare(display.input(false).width, 1280)
      compare(display.input(false).height, 800)
      compare(display.input(false).legacyHeight, 1600)
      compare(display.theme.rowHeight, 57)
      compare(display.theme.fontBody, 13)
      compare(display.launcher.panelWidth, 1117)
      compare(display.launcher.panelHeight, 664)

      fixtureWindow.devicePixelRatio = 1.5
      compare(display.legacyHeight, 1200)
      compare(display.theme.rowHeight, 49)
      compare(display.theme.fontBody, 12)
    }

    function test_fractional_window_dpr_preserves_legacy_metrics() {
      fixtureWindow.screen = fractional
      fixtureWindow.devicePixelRatio = 1.5
      waitForValue(display, "configuredSize", Qt.size(1920, 1200))

      compare(display.input(false).legacyHeight, 1800)
      compare(display.input(true).legacyHeight, 1800)
      compare(display.theme.rowHeight, 61)
      compare(display.theme.fontBody, 14)
    }

    function test_rotated_output_preserves_native_mode_height_data() {
      return [{ tag: "90-degrees", orientation: Qt.PortraitOrientation },
        { tag: "270-degrees", orientation: Qt.InvertedPortraitOrientation }]
    }

    function test_rotated_output_preserves_native_mode_height(data) {
      rotated.orientation = data.orientation
      fixtureWindow.screen = rotated
      fixtureWindow.devicePixelRatio = 1
      waitForValue(display, "configuredSize", Qt.size(1080, 1920))

      compare(display.legacyHeight, 1080)
      compare(display.input(true).legacyHeight, 1080)
      compare(display.theme.rowHeight, 46)
      compare(display.theme.fontBody, 12)

      rotated.width = 1920
      rotated.height = 1080
      rotated.orientation = Qt.InvertedLandscapeOrientation
      waitForValue(display, "configuredSize", Qt.size(1920, 1080))

      compare(display.legacyHeight, 1080)
      compare(display.theme.rowHeight, 46)
      compare(display.theme.fontBody, 12)
    }

    function test_recommended_keeps_logical_metrics_after_replacement() {
      fixtureHost.part_displayScaleStore.legacy = false
      fixtureWindow.screen = replacement
      waitForValue(display, "configuredSize", Qt.size(1280, 800))

      compare(display.legacyHeight, 1600)
      compare(display.theme.rowHeight, 46)
      compare(display.theme.fontBody, 12)
      compare(display.launcher.panelWidth, 900)
      compare(display.launcher.panelHeight, 600)
      fixtureWindow.devicePixelRatio = 1.5
      compare(display.legacyHeight, 1200)
      compare(display.theme.rowHeight, 46)
      compare(display.theme.fontBody, 12)
    }
  }
}
