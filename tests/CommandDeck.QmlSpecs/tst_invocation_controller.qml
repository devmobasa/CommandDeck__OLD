import QtQuick
import QtTest
import "../../qml/common" as Deck

TestCase {
  name: "InvocationController"
  Deck.InvocationController { id: invocation }

  function init() { invocation.clear() }

  function test_confirmation_input_and_one_active_request() {
    const result = { id: "document" }
    const action = { id: "open" }
    verify(invocation.requestConfirmation(result, action))
    verify(!invocation.requestInput(result, action, false))
    const confirmed = invocation.takeConfirmation()
    compare(confirmed.result, result)
    compare(confirmed.action, action)
    compare(invocation.pendingConfirmationResult, null)

    verify(invocation.requestInput(result, action, true))
    verify(!invocation.requestConfirmation(result, action))
    verify(invocation.actionInputOpen)
    const input = invocation.takeInput()
    compare(input.result, result)
    compare(input.action, action)
    verify(input.confirmed)
    verify(!invocation.actionInputOpen)

    verify(invocation.begin(41, true, true))
    verify(invocation.busy)
    verify(!invocation.begin(42, false, false))
    verify(!invocation.requestInput(result, action, false))
    verify(!invocation.requestConfirmation(result, action))
    verify(!invocation.settle(42))
    compare(invocation.requestId, 41)
    const completion = invocation.complete(41, { succeeded: true, message: "Saved" }, true)
    compare(completion.status, "Saved")
    verify(completion.refresh)
    verify(!completion.dismiss)
    verify(!invocation.busy)
    verify(!invocation.herdrEditPending)
    verify(!invocation.refreshAfterAction)
  }

  function test_clear_discards_every_pending_transition() {
    invocation.requestConfirmation({ id: "old" }, { id: "run" })
    invocation.requestInput({ id: "old" }, { id: "run" }, true)
    invocation.begin(51, true, true)
    invocation.clear()
    compare(invocation.requestId, 0)
    compare(invocation.pendingConfirmationResult, null)
    verify(!invocation.actionInputOpen)
    verify(!invocation.refreshAfterAction)
    verify(!invocation.herdrEditPending)
    verify(!invocation.settle(51))
  }

  function test_outcome_policy_keeps_failures_and_output_visible() {
    verify(invocation.begin(61, false, false))
    const failed = invocation.complete(61, { succeeded: false, message: "Denied" }, true)
    compare(failed.status, "Denied")
    verify(!failed.refresh)
    verify(!failed.dismiss)
    compare(invocation.complete(61, { succeeded: true }, true), null)

    verify(invocation.begin(62, false, false))
    const output = invocation.complete(62, { succeeded: true, detailMarkdown: "details" }, true)
    verify(output.output)
    verify(!output.dismiss)

    verify(invocation.begin(63, false, false))
    verify(invocation.complete(63, { succeeded: true }, true).dismiss)
  }
}
