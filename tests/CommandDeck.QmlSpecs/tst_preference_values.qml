import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

Item {
  CommandDeck.PersonalizationController {
    id: controller
    property int sequence: 0
    property var lastRequest: null
    nextRequestId: () => ++sequence
    onRequestReady: request => lastRequest = request
  }

  CommandDeck.PreferenceUpdateQueue { id: queue }
  SignalSpy { id: dispatches; target: queue; signalName: "dispatchRequested" }

  TestCase {
    name: "Preference_values"
    when: windowShown

    function initialPreferences() {
      return { registryRevision: 3, preferenceRevision: 7, reduceMotion: false,
        editors: [{ id: "editor", arguments: ["--line", "{line}"], enabled: true }],
        sources: [{ sourceId: "files", enabledInSearch: true, enabledOnHome: true, position: 1 }] }
    }

    function reordered(enabled) {
      return { sources: [{ position: 1, enabledOnHome: true, enabledInSearch: enabled, sourceId: "files" }],
        editors: [{ enabled: true, arguments: ["--line", "{line}"], id: "editor" }],
        reduceMotion: false, preferenceRevision: 7, registryRevision: 3 }
    }

    function test_source_patch_compares_values_independent_of_object_order() {
      controller.resetPreferences(initialPreferences())
      controller.submitPreferences(reordered(false))
      const request = controller.lastRequest
      verify(request.sourcePatch !== undefined, JSON.stringify(request))
      verify(request.preferences === undefined)
      compare(request.sourcePatch.registryRevision, 3)
      compare(request.sourcePatch.preferenceRevision, 7)
      compare(request.sourcePatch.changes.length, 1)
      compare(request.sourcePatch.changes[0].sourceId, "files")
      compare(request.sourcePatch.changes[0].enabledInSearch, false)
      controller.settle(request.requestId)
    }

    function test_queue_does_not_resend_an_equivalent_reordered_acknowledgement() {
      queue.reset(initialPreferences())
      dispatches.clear()
      queue.submit(reordered(false))
      queue.submit(reordered(false))
      const acknowledged = initialPreferences()
      acknowledged.sources[0].enabledInSearch = false
      queue.accept(acknowledged)
      compare(dispatches.count, 1)
      verify(!queue.inFlight)
      compare(queue.draft.sources[0].enabledInSearch, false)
    }

    function test_changed_non_source_values_still_send_full_preferences() {
      controller.resetPreferences(initialPreferences())
      const draft = reordered(false)
      draft.editors[0].arguments.reverse()
      controller.submitPreferences(draft)
      const request = controller.lastRequest
      verify(request.preferences !== undefined)
      verify(request.sourcePatch === undefined)
      compare(request.preferences.editors[0].arguments[0], "{line}")
      controller.settle(request.requestId)
    }
  }
}
