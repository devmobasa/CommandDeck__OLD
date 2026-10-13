import QtQuick
import Quickshell
import Quickshell.Io

QtObject {
  id: root
  property bool enabled: false
  // Proposed narrow host capability; absent on current stock Omarchy.
  property var capability: null
  property int connectionGeneration: 0
  readonly property string instance: Quickshell.env("HYPRLAND_INSTANCE_SIGNATURE")
  readonly property bool available: !!capability && capability.version === 1
    && Number.isInteger(capability.generation) && capability.generation >= 0
  readonly property bool permitted: enabled && available && stream.connected
    && capability.known === true && capability.locked === false
  readonly property string epoch: permitted
    ? instance + ":" + connectionGeneration + ":" + capability.generation : ""

  property Socket stream: Socket {
    path: Quickshell.env("XDG_RUNTIME_DIR") + "/hypr/" + root.instance + "/.socket2.sock"
    connected: root.enabled && root.available && root.instance.length > 0
    onConnectedChanged: root.connectionGeneration++
    // Consume events without retaining titles/addresses or logging content.
    parser: SplitParser { onRead: data => {} }
  }
}
