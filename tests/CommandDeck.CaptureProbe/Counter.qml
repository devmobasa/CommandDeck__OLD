import QtQuick
import QtQuick.Window

Window {
  id: root
  required property int number
  width: 640
  height: 360
  visible: true
  title: "CommandDeck synthetic counter " + number
  property int counter: 0
  color: Qt.rgba(counter / 255, number * 32 / 255, 0, 1)
  onFrameSwapped: console.log("RENDERED " + number + " " + counter + " " + Date.now())
  Text {
    anchors.centerIn: parent
    text: "Synthetic " + root.number + " frame " + root.counter
    color: "white"
    font.pixelSize: 20
  }
  Timer {
    interval: 500
    running: true
    repeat: true
    onTriggered: {
      root.counter = (root.counter + 1) % 256
      console.log("COUNTER " + root.number + " " + root.counter + " " + Date.now())
    }
  }
}
