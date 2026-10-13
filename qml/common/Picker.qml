pragma ComponentBehavior: Bound
import QtQuick
import Quickshell
import Quickshell.Hyprland
import Quickshell.Io
import Quickshell.Wayland

ShellRoot {
  id: root
  property var session: ({ prompt: "Loading choices…", choices: [] })
  property bool finished: false
  Theme { id: pickerTheme; loadShellTokens: false }
  FileView {
    id: input
    path: Quickshell.env("COMMANDDECK_PICKER_INPUT")
    blockLoading: true
    printErrors: false
    onLoaded: {
      try { root.session = JSON.parse(text()) }
      catch (error) { Qt.exit(2) }
    }
    onLoadFailed: Qt.exit(2)
  }
  FileView {
    id: output
    path: Quickshell.env("COMMANDDECK_PICKER_OUTPUT")
    printErrors: false
    atomicWrites: true
    onSaved: Qt.exit(0)
    onSaveFailed: Qt.exit(2)
  }
  Variants {
    model: Quickshell.screens.length ? [Quickshell.screens.find(screen =>
      Hyprland.focusedMonitor && screen.name === Hyprland.focusedMonitor.name) || Quickshell.screens[0]] : []
    delegate: PanelWindow {
    required property var modelData
    screen: modelData
    visible: true
    implicitWidth: 620
    implicitHeight: 460
    color: "transparent"
    exclusionMode: ExclusionMode.Ignore
    WlrLayershell.layer: WlrLayer.Overlay
    WlrLayershell.namespace: "commanddeck-picker"
    WlrLayershell.keyboardFocus: WlrKeyboardFocus.Exclusive
    PickerView {
      anchors.fill: parent
      theme: pickerTheme
      session: root.session
      onChosen: index => {
        if (root.finished) return
        root.finished = true
        output.setText(String(index))
      }
      onCanceled: Qt.exit(1)
    }
  }
}
}
