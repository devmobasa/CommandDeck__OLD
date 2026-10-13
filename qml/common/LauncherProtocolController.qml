pragma ComponentBehavior: Bound
import QtQuick

Item {
  id: supervisor
  required property var host
  readonly property alias protocolControl: protocol

  Timer {
    interval: supervisor.host.herdrRefreshInterval
    running: supervisor.host.herdrLiveUpdatesEnabled
    repeat: true
    onTriggered: supervisor.host.requestSearch()
  }

  ProtocolClient {
    id: protocol
    enabled: !supervisor.host.testMode
    onRequestSent: value => supervisor.host.protocolRequestSent(value)
    onMessage: value => {
      supervisor.host.daemonAvailable = true
      supervisor.host.handleMessage(value)
    }
    onDisconnected: supervisor.host.handleProtocolDisconnected()
    onReconnected: supervisor.host.handleProtocolReconnected()
  }
}
