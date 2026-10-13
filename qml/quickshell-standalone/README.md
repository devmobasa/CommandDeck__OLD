# Standalone Quickshell host

This host runs the shared CommandDeck frontend on Hyprland without the Omarchy
plugin loader. Start it as a named Quickshell configuration, then invoke its
`commanddeck` IPC target to open, close, or toggle the launcher.

The host is an experimental source integration. It requires a compatible
Quickshell build with Wayland, wlroots layer-shell, Hyprland, and screencopy
support. The daemon and its systemd user service must already be installed.

```bash
qs -p ./qml
```

From another terminal:

```bash
qs ipc -p ./qml call commanddeck toggle '{}'
```

Window, workspace, preview, and shortcut features currently use the shared
Hyprland adapter. Other compositors need their own adapter and backend provider
before those capabilities can be advertised.
