import QtQuick
import "../common" as Common

Common.CommandDeck {
  id: root

  desktopIntegration: Common.HyprlandDesktopIntegration {
    shell: root.shell
  }
  themeTokenSource: Qt.resolvedUrl("ThemeTokens.qml")

  // The Hyprland hotkey toggles the launcher in-process. Like the theme tokens,
  // the shortcut is created at run time: a Quickshell without its global
  // shortcut module still loads the launcher, and `omarchy-shell shell toggle`
  // keeps working. The standalone host keeps its own IPC handler instead.
  Component.onCompleted: {
    const component = Qt.createComponent(
      Qt.resolvedUrl("hotkey/LauncherShortcut.qml"), Component.PreferSynchronous)

    if (component.status === Component.Ready) {
      component.createObject(root, { launcher: root })
      return
    }

    console.warn("CommandDeck: the Hyprland global shortcut is unavailable; "
      + "bind the hotkey to `omarchy-shell shell toggle local.commanddeck '{}'`. "
      + component.errorString())
  }
}
