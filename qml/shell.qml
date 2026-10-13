import QtQuick
import Quickshell

ShellRoot {
  Loader {
    source: Qt.resolvedUrl("quickshell-standalone/StandaloneHost.qml")
  }
}
