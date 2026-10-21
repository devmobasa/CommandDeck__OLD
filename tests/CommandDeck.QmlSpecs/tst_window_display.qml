import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/DeckDisplayActions.js" as DisplayActions

Item {
  Deck.Theme { id: source; loadShellTokens: false }
  QtObject { id: store; property bool legacy: false; property int legacyPercent: 100 }
  QtObject {
    id: host
    property var part_appTheme: source
    property var part_displayScaleStore: store
    property int displaySizePercent: 100
    property bool settingsOpen: false
    property bool extensionManagementOpen: false
    property string statusText: ""
    property var presentingWindow: QtObject {
      readonly property alias launcherSizing: laptop.launcher
      readonly property alias displaySizing: laptop.sizing
      readonly property alias maximumFittingPercent: laptop.maximumFittingPercent
    }
    function applyDisplaySizePercent(percent) { displaySizePercent = percent }
  }
  Deck.WindowDisplay { id: primary; host: host; width: 2304; height: 1296; legacyHeight: 2160 }
  Deck.WindowDisplay { id: laptop; host: host; width: 1440; height: 900; legacyHeight: 1800 }

  TestCase {
    name: "WindowDisplay"

    function init() {
      host.settingsOpen = false
      host.extensionManagementOpen = false
      store.legacy = false
      host.displaySizePercent = 100
      primary.width = 2304
      primary.height = 1296
      laptop.width = 1440
      laptop.height = 900
      tryCompare(primary, "configuredSize", Qt.size(2304, 1296))
      tryCompare(laptop, "configuredSize", Qt.size(1440, 900))
    }

    function test_metrics_use_each_windows_logical_geometry() {
      compare(laptop.theme.fontBody, 12)
      verify(primary.theme.fontBody >= 12 && primary.theme.fontBody <= 15)
      const original = primary.sizing
      laptop.width = 1280
      laptop.height = 400
      compare(primary.sizing, original)
      tryVerify(function() { return laptop.sizing.limited })
      laptop.width = 1440
      laptop.height = 900
    }

    function test_zoom_is_full_strength_and_settings_has_no_text_discount() {
      const normalText = primary.theme.textScale
      const normalLayout = primary.theme.displayScale
      host.displaySizePercent = 150
      compare(primary.theme.textScale / normalText, 1.5)
      compare(primary.theme.displayScale / normalLayout, 1.5)
      host.settingsOpen = true
      compare(primary.theme.densityScale, 1)
      compare(primary.theme.textScale, primary.theme.displayScale)
    }

    function test_limits_are_visible_and_requested_zoom_survives_resizing() {
      host.displaySizePercent = 150
      verify(laptop.description.indexOf("150% requested; limited to fit") === 0)
      laptop.width = 2560
      laptop.height = 1440
      compare(host.displaySizePercent, 150)
      tryVerify(function() { return !laptop.sizing.limited })
      laptop.width = 1440
      laptop.height = 900
    }

    function test_legacy_default_is_not_labelled_recommended() {
      store.legacy = true
      verify(primary.description.indexOf("Previous sizing") >= 0)
      verify(primary.description.indexOf("Recommended") < 0)
    }

    function test_description_and_maximum_follow_launcher_while_settings_is_open() {
      primary.width = 1920
      primary.height = 1080
      tryCompare(primary, "configuredSize", Qt.size(1920, 1080))
      host.displaySizePercent = 150
      verify(!primary.launcher.limited)
      const description = primary.description
      compare(primary.maximumFittingPercent, 150)
      for (const surface of ["settingsOpen", "extensionManagementOpen"]) {
        host[surface] = true
        verify(primary.sizing.limited, "the larger Settings surface needs a different fit")
        compare(primary.description, description)
        compare(primary.maximumFittingPercent, 150)
        host[surface] = false
      }

      host.settingsOpen = true
      host.displaySizePercent = 150
      compare(laptop.maximumFittingPercent, 135)
      verify(laptop.description.indexOf("limited to fit (max 135% on this screen)") >= 0)
    }

    function test_narrow_and_scrolling_layouts_are_explained() {
      laptop.width = 720
      laptop.height = 900
      tryVerify(function() { return laptop.sizing.narrow })
      verify(laptop.description.indexOf("narrow layout") >= 0)
      verify(!laptop.sizing.scroll)
      laptop.width = 320
      laptop.height = 240
      tryVerify(function() { return laptop.sizing.scroll })
      verify(laptop.description.indexOf("scroll to view") >= 0)
      laptop.width = 1440
      laptop.height = 900
    }

    function test_zoom_status_when_no_percentage_fits() {
      laptop.width = 320
      laptop.height = 240
      tryCompare(laptop, "maximumFittingPercent", 0)
      DisplayActions.stepDisplaySize(host, 1)
      verify(host.statusText.indexOf("no percentage fits here") >= 0)
      verify(host.statusText.indexOf("max 75%") < 0)
      verify(host.statusText.indexOf("scroll to view") >= 0)
    }

    function test_zoom_status_reports_launcher_limit_while_settings_is_open() {
      host.settingsOpen = true
      host.displaySizePercent = 120
      DisplayActions.stepDisplaySize(host, 1)
      compare(host.statusText, "Display size: 130%")
      verify(laptop.settings.limited && !laptop.launcher.limited)
      DisplayActions.stepDisplaySize(host, 1)
      compare(host.statusText, "Display size: 140%; limited to fit (max 135% here)")
    }

  }
}
