import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/DeckSearchActions.js" as SearchActions

Item {
  width: 600
  height: 500
  Deck.Theme { id: theme; loadShellTokens: false }
  TextInput { id: search; width: 400 }
  Deck.ActionPanel {
    id: panel
    y: 40
    width: 400
    height: 400
    theme: theme
    visible: scene.opened
    onDismissed: { scene.opened = false; search.forceActiveFocus() }
    onActionChosen: action => scene.chosen = action
  }
  Deck.ActionInputDialog {
    id: inputs
    anchors.fill: parent
    theme: theme
    opened: invocation.actionInputOpen
    action: invocation.actionInputAction
    onSubmitted: values => { scene.submitted = values; scene.selection = invocation.takeInput() }
  }
  id: scene
  property bool opened: false
  property var chosen: null
  property var submitted: null
  property var selection: null
  Deck.InvocationController { id: invocation }

  TestCase {
    name: "ActionRefresh"
    when: windowShown
    function action() {
      return {id: "run", title: "Run", kind: "run-command",
        arguments: {argv: "first"}, inputs: [], requiresConfirmation: true, isDestructive: false}
    }
    function row(value, reference) {
      return {id: "item", title: "Item", invocationReference: reference, actions: [value]}
    }
    function refreshHost(fresh) {
      return {
        get actionPanelOpen() { return scene.opened }, activeActionPanel: panel,
        part_invocationController: invocation,
        part_navigation: {indexOfResultId: id => fresh && fresh.id === id ? 0 : -1, resultAt: index => fresh},
        reconciliationPending: false, actionRefreshChanged: false, statusText: "",
        confirmationOpen: false, focusRevision: 0,
        resultStatus: results => "1 result", previewSearchIdentity: "query",
        part_searchSession: {activeIdentity: "query", pendingResults: [fresh]}
      }
    }
    function init() { search.text = ""; scene.opened = false; scene.chosen = null; scene.submitted = null; scene.selection = null; invocation.clear() }
    function test_menu_refresh_preserves_meaning_and_keyboard_focus() {
      scene.opened = true
      panel.openFor(row(action(), "old"))
      tryVerify(() => panel.filterControl.activeFocus)
      const next = row(action(), "fresh")
      next.actions[0].title = "New display title"
      verify(panel.retarget(next))
      verify(panel.filterControl.activeFocus)
      keyClick(Qt.Key_Return)
      compare(scene.chosen.arguments.argv, "first")
      compare(panel.result.invocationReference, "fresh")
    }
    function test_changed_menu_action_closes_before_activation() {
      scene.opened = true
      panel.openFor(row(action(), "old"))
      tryVerify(() => panel.filterControl.activeFocus)
      const changed = action()
      changed.arguments.argv = "second"
      const host = refreshHost(row(changed, "fresh"))
      SearchActions.searchCommitted(host, [host.part_navigation.resultAt(0)])
      verify(!scene.opened)
      compare(host.statusText, "This item changed; review it again")
      verify(search.activeFocus)
      for (const character of "query") keyClick(character)
      compare(search.text, "query")
      compare(scene.chosen, null)
    }
    function test_confirmation_and_inputs_reject_changed_meaning_data() {
      return [
        {tag: "kind", field: "kind", value: "other"},
        {tag: "arguments", field: "arguments", value: {argv: "second"}},
        {tag: "inputs", field: "inputs", value: [{id: "secret", required: true}]},
        {tag: "confirmation", field: "requiresConfirmation", value: false},
        {tag: "destructive", field: "isDestructive", value: true},
        {tag: "id", field: "id", value: "other"}
      ]
    }
    function test_confirmation_and_inputs_reject_changed_meaning(data) {
      for (const input of [false, true]) {
        invocation.clear()
        const before = action()
        if (input) verify(invocation.requestInput(row(before, "old"), before, true))
        else verify(invocation.requestConfirmation(row(before, "old"), before))
        const changed = action()
        changed[data.field] = data.value
        verify(!invocation.retarget(row(changed, "fresh")))
        verify(!invocation.collecting)
        compare(invocation.pendingConfirmationResult, null)
        compare(invocation.actionInputResult, null)
      }
    }
    function test_input_refresh_keeps_typed_values_and_focus() {
      const before = action()
      before.inputs = [{name: "destination", label: "Destination"}]
      verify(invocation.requestInput(row(before, "old"), before, true))
      tryVerify(() => inputs.rowAt(0) && inputs.rowAt(0).input.activeFocus)
      for (const character of "keep my draft") keyClick(character)
      const fresh = JSON.parse(JSON.stringify(before))
      verify(invocation.retarget(row(fresh, "fresh")))
      verify(inputs.rowAt(0).input.activeFocus)
      compare(inputs.rowAt(0).input.text, "keep my draft")
      mouseClick(inputs.submitControl)
      compare(scene.submitted.destination, "keep my draft")
      compare(scene.selection.result.invocationReference, "fresh")
    }
    function test_partial_commit_notice_survives_done() {
      scene.opened = true
      panel.openFor(row(action(), "old"))
      tryVerify(() => panel.filterControl.activeFocus)
      const changed = action()
      changed.arguments.argv = "second"
      const fresh = row(changed, "fresh")
      const host = refreshHost(fresh)
      SearchActions.searchCommitted(host, [fresh])
      compare(host.statusText, "This item changed; review it again")
      SearchActions.searchFinished(host, {complete: true})
      compare(host.statusText, "This item changed; review it again")
      verify(search.activeFocus)
    }
    function test_pending_dialog_follows_equivalent_fresh_reference() {
      const before = action()
      verify(invocation.requestConfirmation(row(before, "old"), before))
      const next = row(action(), "fresh")
      verify(invocation.retarget(next))
      compare(invocation.takeConfirmation().result.invocationReference, "fresh")
    }

    function test_removed_menu_waits_for_completed_results() {
      scene.opened = true
      panel.openFor(row(action(), "old"))
      tryVerify(() => panel.filterControl.activeFocus)
      const host = refreshHost(null)

      SearchActions.searchCommitted(host, [])
      verify(scene.opened, "a partial batch does not prove the target was removed")
      verify(panel.filterControl.activeFocus)

      SearchActions.searchFinished(host, {complete: true})
      verify(!scene.opened, "completion removes a menu whose target left the results")
      compare(host.statusText, "This item changed; review it again")
      verify(search.activeFocus)
      keyClick(Qt.Key_Q)
      compare(search.text, "q")
      compare(scene.chosen, null)
    }

    function test_removed_confirmation_and_input_wait_for_completed_results() {
      for (const input of [false, true]) {
        invocation.clear()
        const before = action()
        if (input) verify(invocation.requestInput(row(before, "old"), before, true))
        else verify(invocation.requestConfirmation(row(before, "old"), before))
        const host = refreshHost(null)
        host.confirmationOpen = !input

        SearchActions.searchCommitted(host, [])
        verify(invocation.collecting, "partial batches retain the pending action")

        SearchActions.searchFinished(host, {complete: true})
        verify(!invocation.collecting, "completion cancels a removed target")
        verify(!host.confirmationOpen)
        compare(invocation.pendingConfirmationResult, null)
        compare(invocation.actionInputResult, null)
        compare(host.focusRevision, 1, "dismissal requests focus restoration")
        compare(host.statusText, "This item changed; review it again")
      }
    }
  }
}
