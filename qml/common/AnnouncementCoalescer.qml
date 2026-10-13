import QtQml

QtObject {
  id: root

  property int interval: 120
  property string pending: ""
  property string announcement: ""

  function publish(value) {
    pending = value || ""
    settle.restart()
  }

  function flush() {
    settle.stop()
    announcement = pending
  }

  property Timer settle: Timer {
    interval: root.interval
    repeat: false
    onTriggered: root.flush()
  }
}
