import QtQuick
import "DisplaySizing.js" as Sizing

QtObject {
  id: root
  required property var host
  property real width: 1920
  property real height: 1080
  property real legacyHeight: height
  // A compositor configure can update both axes; publish one coherent size.
  property size configuredSize: Qt.size(width, height)
  readonly property Timer configure: Timer {
    interval: 0
    onTriggered: root.configuredSize = Qt.size(root.width, root.height)
  }
  onWidthChanged: configure.restart()
  onHeightChanged: configure.restart()

  function input(settings) {
    const source = host.part_appTheme
    const store = host.part_displayScaleStore
    return { width: configuredSize.width, height: configuredSize.height,
      fontScale: source.fontScale, bodyPixels: source.tokens ? source.tokens.fontBody : 12,
      percent: host.displaySizePercent, settings: settings,
      legacy: store.legacy, legacyPercent: store.legacyPercent, legacyHeight: legacyHeight }
  }

  readonly property var launcher: Sizing.calculate(input(false))
  readonly property var settings: Sizing.calculate(input(true))
  readonly property int maximumFittingPercent: Sizing.maximumFittingPercent(input(false))
  readonly property var sizing: host.settingsOpen || host.extensionManagementOpen ? settings : launcher
  readonly property Theme theme: WindowTheme { source: root.host.part_appTheme; sizing: root.sizing }
  readonly property string description: host.displaySizePercent + "% requested"
    + (host.displaySizePercent === 100 && !host.part_displayScaleStore.legacy ? " (Recommended)" : "")
    + Sizing.fitDescription(launcher, maximumFittingPercent, "on this screen")
    + (host.part_displayScaleStore.legacy ? " · Previous sizing" : "")
}
