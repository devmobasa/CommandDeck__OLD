import QtQuick
import "../common" as Common

Common.CommandDeck {
  id: root

  desktopIntegration: Common.HyprlandDesktopIntegration {
    shell: root.shell
  }
  themeTokenSource: Qt.resolvedUrl("ThemeTokens.qml")
}
