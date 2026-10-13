import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

Item {
  width: 800
  height: 600
  CommandDeck.Theme { id: theme; loadShellTokens: false }
  CommandDeck.DeckButton {
    id: openButton
    theme: theme
    text: "Open extension"
    onClicked: view.opened = true
  }
  CommandDeck.ExtensionListView {
    id: view
    anchors.fill: parent
    theme: theme
    returnFocus: openButton
    onBackRequested: opened = false
  }
  SignalSpy { id: actions; target: view; signalName: "actionRequested" }
  SignalSpy { id: presentations; target: view; signalName: "presented" }
  CommandDeck.ExtensionDetailView {
    id: detail
    anchors.fill: parent
    theme: theme
    onBackRequested: opened = false
  }
  SignalSpy { id: detailActions; target: detail; signalName: "actionRequested" }
  LiteralTextProbe { id: literal }
  TestCase {
    name: "ExtensionNativeViews"
    when: windowShown
    function snapshot(revision, suffix) {
      return { revision: revision, content: { id: "probe", title: "Native list probe", search: { mode: "local" }, items: [
        { id: "alpha", title: "Alpha " + suffix, actions: [{ title: "Copy Alpha", handle: "alpha-" + revision }] },
        { id: "beta", title: "Beta " + suffix, actions: [{ title: "Copy Beta", handle: "beta-" + revision }] }
      ] } }
    }
    function init() {
      view.opened = false
      view.busy = false
      view.recoveryVisible = false
      view.disableAvailable = false
      view.snapshot = snapshot(1, "")
      actions.clear()
      presentations.clear()
      detail.opened = false
      detailActions.clear()
    }
    function test_keyboard_open_type_select_refresh_activate_back_reopen() {
      mouseClick(openButton)
      tryCompare(view.inputControl, "activeFocus", true)
      tryCompare(presentations, "count", 1)
      keyClick(Qt.Key_Down)
      compare(view.selectedId, "beta")
      for (let i = 2; i <= 6; i++) {
        view.snapshot = snapshot(i, "refresh " + i)
        waitForRendering(view)
        compare(view.selectedId, "beta")
        keyClick(Qt.Key_Return)
        compare(actions.signalArguments[actions.count - 1][2], "beta-" + i)
      }
      keyClick(Qt.Key_A)
      keyClick(Qt.Key_L)
      compare(view.inputControl.text, "al")
      compare(view.selectedId, "alpha")
      keyClick(Qt.Key_Escape)
      compare(view.opened, true)
      compare(view.inputControl.text, "")
      keyClick(Qt.Key_Escape)
      compare(view.opened, false)
      tryCompare(openButton, "activeFocus", true)
      keyClick(Qt.Key_Return)
      tryCompare(view.inputControl, "activeFocus", true)
      keyClick(Qt.Key_B)
      compare(view.inputControl.text, "b")
    }
    function test_mouse_selection_and_copy_keep_search_ready() {
      mouseClick(openButton)
      tryCompare(view.inputControl, "activeFocus", true)
      verify(waitForRendering(view))
      mouseClick(view.itemsControl.itemAtIndex(1))
      keyClick(Qt.Key_B)
      compare(view.inputControl.text, "b")
      compare(view.selectedId, "beta")
      mouseClick(view.primaryControl)
      keyClick(Qt.Key_E)
      compare(view.inputControl.text, "be")
      compare(actions.count, 1)
    }
    // An extension names its own actions. Every action button shows its title
    // as written: markup in it cannot restyle the button or hide words.
    function test_extension_action_titles_are_shown_literally() {
      const title = "<font color='red'>Pay</font> <span style='font-size:40px'>now</span> &amp; later"
      const itemAction = "<b>Save</b> <i>draft</i>"
      const viewAction = "<u>Generate</u> &lt;again&gt;"
      const next = snapshot(1, "")
      next.content.items[0].actions = [{ title: title, handle: "pay-1" }, { title: itemAction, handle: "save-1" }]
      next.content.actions = [{ id: "generate", title: viewAction, handle: "generate-1" }]
      view.snapshot = next
      mouseClick(openButton)
      tryCompare(view.inputControl, "activeFocus", true)
      verify(waitForRendering(view))

      compare(literal.problem(view.primaryControl, title), "")
      compare(literal.problem(view.viewActionsControl.itemAt(0), itemAction), "")
      compare(literal.problem(view.viewActionsControl.itemAt(1), viewAction), "")

      keyClick(Qt.Key_Return)
      compare(actions.signalArguments[0][2], "pay-1")
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      verify(view.viewActionsControl.itemAt(0).activeFocus)
      keyClick(Qt.Key_Space)
      compare(actions.signalArguments[1][2], "save-1")
    }

    function test_extension_detail_action_titles_are_shown_literally() {
      const title = "<h1>Open</h1> <a href='https://example.invalid'>site</a>"
      detail.snapshot = { revision: 1, content: { title: "Detail", body: "Body", actions: [{ id: "open", title: title, handle: "open-1" }] } }
      detail.opened = true
      tryCompare(detail.bodyControl, "activeFocus", true)
      verify(waitForRendering(detail))

      compare(literal.problem(detail.actionsControl.itemAt(0), title), "")
      keyClick(Qt.Key_Tab)
      verify(detail.actionsControl.itemAt(0).activeFocus)
      keyClick(Qt.Key_Space)
      compare(detailActions.signalArguments[0][2], "open-1")
    }
    function test_typing_from_keyboard_list_focus_edits_search() {
      mouseClick(openButton)
      tryCompare(view.inputControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_B)
      compare(view.inputControl.text, "b")
      compare(view.inputControl.activeFocus, true)
    }
    function test_mouse_primary_back_and_tab_order() {
      mouseClick(openButton)
      tryCompare(view.inputControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Down)
      compare(view.selectedId, "beta")
      keyClick(Qt.Key_Tab)
      compare(view.primaryControl.activeFocus, true)
      keyClick(Qt.Key_Space)
      compare(actions.count, 1)
      compare(actions.signalArguments[0][2], "beta-1")
      mouseClick(view.primaryControl)
      compare(actions.count, 2)
      mouseClick(view.backControl)
      compare(view.opened, false)
      tryCompare(openButton, "activeFocus", true)
    }
    function test_callback_busy_escape_and_recovery_keyboard() {
      const next = snapshot(1, "")
      next.content.actions = [{ id: "generate", title: "Generate again", handle: "generate-1" }]
      view.snapshot = next
      mouseClick(openButton)
      tryCompare(view.inputControl, "activeFocus", true)
      keyClick(Qt.Key_A)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      const callback = view.viewActionsControl.itemAt(0)
      compare(callback.activeFocus, true)
      keyClick(Qt.Key_Space)
      compare(actions.count, 1)
      compare(actions.signalArguments[0][1], "")
      compare(actions.signalArguments[0][2], "generate-1")
      keyClick(Qt.Key_Backtab)
      compare(view.primaryControl.activeFocus, true)
      keyClick(Qt.Key_Escape)
      compare(view.opened, true)
      compare(view.inputControl.text, "")
      compare(view.inputControl.activeFocus, true)
      view.busy = true
      keyClick(Qt.Key_Return)
      mouseClick(view.primaryControl)
      compare(actions.count, 1)
      view.recoveryVisible = true
      view.disableAvailable = true
      tryCompare(view.restartControl, "activeFocus", true)
      keyClick(Qt.Key_Right)
      compare(view.disableControl.activeFocus, true)
      keyClick(Qt.Key_Left)
      compare(view.restartControl.activeFocus, true)
      keyClick(Qt.Key_Escape)
      tryCompare(openButton, "activeFocus", true)
    }
    function test_regeneration_restores_action_identity_or_search() {
      function generated(revision, includeGenerate) {
        const next = snapshot(revision, "regenerated")
        next.content.actions = [{ id: "other", title: "Other action", handle: "other-" + revision }]
        if (includeGenerate) next.content.actions.unshift({ id: "generate", title: "Generate again", handle: "generate-" + revision })
        if (revision === 2) next.content.actions.reverse()
        return next
      }
      view.snapshot = generated(1, true)
      mouseClick(openButton)
      tryCompare(view.inputControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      verify(view.viewActionsControl.itemAt(0).activeFocus)
      keyClick(Qt.Key_Space)
      compare(actions.signalArguments[0][2], "generate-1")
      view.busy = true
      view.snapshot = generated(2, true)
      waitForRendering(view)
      view.busy = false
      tryVerify(function() { return view.viewActionsControl.itemAt(1).activeFocus })
      keyClick(Qt.Key_Space)
      compare(actions.signalArguments[1][2], "generate-2")
      view.busy = true
      view.snapshot = generated(3, false)
      view.busy = false
      tryCompare(view.inputControl, "activeFocus", true)
      keyClick(Qt.Key_B)
      compare(view.selectedId, "beta")
      view.snapshot = generated(4, true)
      tryCompare(view.inputControl, "activeFocus", true)
      verify(waitForRendering(view))
      mouseClick(view.viewActionsControl.itemAt(0))
      compare(actions.signalArguments[2][2], "generate-4")
      view.busy = true
      view.snapshot = generated(5, true)
      view.busy = false
      tryCompare(view.inputControl, "activeFocus", true)
      keyClick(Qt.Key_E)
      compare(view.inputControl.text, "be")
      keyClick(Qt.Key_Escape)
      compare(view.inputControl.text, "")
      keyClick(Qt.Key_Escape)
      tryCompare(openButton, "activeFocus", true)
      keyClick(Qt.Key_Return)
      tryCompare(view.inputControl, "activeFocus", true)
    }
  }
}
