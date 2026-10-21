import QtQuick

Item {
  id: scene
  QtObject {
    property bool pending: false
    property int activeRequestId: 17
    property string query: "synthetic pending search"
  }

  DeadlineTestCase {
    name: "DeadlineDiagnostics"
    when: windowShown

    function test_timeout_snapshot_includes_nonvisual_controller_values() {
      const state = observedState(scene)
      verify(state.indexOf('"pending":false') >= 0, "report the controller's pending state")
      verify(state.indexOf('"activeRequestId":17') >= 0, "report the request being waited for")
      verify(state.indexOf('"query":"synthetic pending search"') >= 0, "report the pending query")
    }
  }
}
