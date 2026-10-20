import Quickshell.Hyprland

// `commanddeck hotkey install` binds the chord to Hyprland's global dispatcher
// for commanddeck:toggle. It reaches this object inside the running shell, so
// the hotkey starts no process. Hyprland delivers one press per key press.
// The unresolved base is explained in .qmllint.ini beside this file.
// qmllint disable unresolved-type
GlobalShortcut {
  required property var launcher

  appid: "commanddeck"
  name: "toggle"
  description: "Toggle CommandDeck"
  onPressed: launcher.toggle("{}")
}
