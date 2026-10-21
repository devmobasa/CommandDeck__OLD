import QtQuick
import "DisplaySizing.js" as Sizing

QtObject {
  id: root
  required property var host
  property var window: null
  property real width: window ? window.width : 1920
  property real height: window ? window.height : 1080
  // Screen objects do not survive unplug/replug. Read through the window on
  // every replacement; a compositor monitor found by name can still be stale.
  readonly property var screen: window ? window.screen : null
  property real legacyHeight: {
    if (!screen) return height

    // Qt Wayland reports quarter-turns of landscape modes as portrait. Native
    // portrait modes need native-mode/transform data that ShellScreen lacks.
    const rotated = screen.orientation === Qt.PortraitOrientation
      || screen.orientation === Qt.InvertedPortraitOrientation
    return (rotated ? screen.width : screen.height) * window.devicePixelRatio
  }
  // A compositor configure can update both axes; publish one coherent size.
  property size configuredSize: Qt.size(width, height)
  readonly property Timer configure: Timer {
    interval: 0
    onTriggered: root.configuredSize = Qt.size(root.width, root.height)
  }
  onWidthChanged: configure.restart()
  onHeightChanged: configure.restart()

  function input(settings, percent) {
    const source = host.part_appTheme
    const store = host.part_displayScaleStore
    return { width: configuredSize.width, height: configuredSize.height,
      fontScale: source.fontScale, bodyPixels: source.tokens ? source.tokens.fontBody : 12,
      widthPercent: host.launcherWidthPercent, heightPercent: host.launcherHeightPercent,
      percent: percent === undefined ? host.displaySizePercent : percent, settings: settings,
      legacy: store.legacy, legacyPercent: store.legacyPercent, legacyHeight: legacyHeight }
  }

  readonly property var launcher: Sizing.calculate(input(false))
  readonly property var settings: Sizing.calculate(input(true))
  readonly property int maximumFittingPercent: Sizing.maximumFittingPercent(input(false, 100))
  readonly property var sizing: host.settingsOpen || host.extensionManagementOpen ? settings : launcher
  readonly property Theme theme: WindowTheme { source: root.host.part_appTheme; sizing: root.sizing }
  readonly property string description: host.displaySizePercent + "% requested"
    + (host.displaySizePercent === 100 && !host.part_displayScaleStore.legacy ? " (Recommended)" : "")
    + Sizing.fitDescription(launcher, maximumFittingPercent, "on this screen")
    + (host.part_displayScaleStore.legacy ? " · Previous sizing" : "")
}
