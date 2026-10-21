import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

Item {
  width: 800
  height: 600
  CommandDeck.Theme { id: theme; loadShellTokens: false }
  CommandDeck.DeckButton { id: opener; theme: theme; text: "Open command"; onClicked: host.opened = true }
  CommandDeck.ExtensionViewHost {
    id: host
    anchors.fill: parent
    theme: theme
    returnFocus: opener
    onBackRequested: opened = false
  }
  SignalSpy { id: actions; target: host; signalName: "actionRequested" }
  DeadlineTestCase {
    name: "ExtensionNavigation"
    when: windowShown
    function list(revision, page) {
      return { revision: revision, content: { id: "list", kind: "list", depth: 1, title: "UUIDs",
        nextPage: page === 1 ? "next-" + revision : null,
        previousPage: page === 2 ? "previous-" + revision : null,
        items: (page === 1 ? ["alpha", "beta"] : ["gamma", "delta"]).map(id => ({
          id: id, title: id, actions: [
            { id: "copy", title: "Copy", handle: "copy-" + id + "-" + revision },
            { id: "detail", title: "Details", handle: "detail-" + id + "-" + revision }
          ]
        })) } }
    }
    function detail(revision) {
      return { revision: revision, content: { id: "detail", kind: "detail", depth: 2,
        backHandle: "back-" + revision, title: "Beta details",
        body: "**Beta** `value`\n<img src=\"file:///etc/passwd\">\n[link](https://example.com)",
        metadata: [{ label: "Type", value: "UUID" }],
        actions: [{ id: "copy", title: "Copy UUID", handle: "detail-copy-" + revision }] } }
    }
    function init() {
      host.opened = false
      host.busy = false
      host.recoveryVisible = false
      host.completed = false
      host.noView = false
      host.snapshot = { revision: 0, content: {} }
      host.snapshot = list(1, 1)
      actions.clear()
    }
    function test_package_images_render_without_taking_keyboard_focus() {
      const image = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAASElEQVR4nGNwi7r0fyAxw6gDRkNgNARGQ4BQCFAKKC6IiDGEEr2jDhgNgdEQGA2B0RAYGiEwoJXRaLN8NARGQ2A0BNxo3D0HAMBAGv92W3CjAAAAAElFTkSuQmCC"
      const page = list(1, 1)
      page.content.items[0].imageSource = image
      host.snapshot = page
      mouseClick(opener)
      waitForValue(host.inputControl, "activeFocus", true)
      verify(waitForFrame(host))
      const rowImage = findChild(host.listControl.itemsControl.itemAtIndex(0), "packageImage")
      verify(rowImage !== null)
      waitForValue(rowImage, "status", Image.Ready)
      const next = detail(2)
      next.content.imageSource = image
      host.snapshot = next
      waitForValue(host.detailControl.bodyControl, "activeFocus", true)
      const detailImage = findChild(host.detailControl, "packageImage")
      waitForValue(detailImage, "status", Image.Ready)
      host.snapshot = { revision: 3, content: Object.assign({}, next.content, { imageSource: "https://example.com/image.png" }) }
      compare(detailImage.source.toString(), "")
    }
    function test_no_view_running_can_be_cancelled_with_escape() {
      mouseClick(opener)
      host.busy = true
      host.noView = true
      waitForValue(host.completionControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      compare(host.opened, false)
      waitForValue(opener, "activeFocus", true)
    }
    function test_no_view_completion_focus_and_dismissal() {
      mouseClick(opener)
      host.noView = true
      waitForValue(host.completionControl, "activeFocus", true)
      compare(host.completionControl.text, "Cancel · Esc")
      mouseClick(host, 20, 20)
      verify(host.completionControl.activeFocus)
      host.completed = true
      waitForValue(host.completionControl, "activeFocus", true)
      compare(host.listControl.opened, false)
      keyClick(Qt.Key_Return)
      compare(host.opened, false)
      waitForValue(opener, "activeFocus", true)
      host.completed = false
      host.noView = false
      mouseClick(opener)
      waitForValue(host.inputControl, "activeFocus", true)
      host.completed = true
      waitForValue(host.completionControl, "activeFocus", true)
      mouseClick(host.completionControl)
      compare(host.opened, false)
      waitForValue(opener, "activeFocus", true)
    }
    function test_pushed_list_starts_fresh_and_back_restores_parent() {
      mouseClick(opener)
      waitForValue(host.inputControl, "activeFocus", true)
      keyClick(Qt.Key_B)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Space)
      const child = list(2, 2)
      child.content.id = "child"
      child.content.depth = 2
      child.content.backHandle = "back-2"
      host.snapshot = child
      waitForValue(host.inputControl, "text", "")
      compare(host.selectedId, "gamma")
      waitForValue(host.inputControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      compare(actions.signalArguments[1][2], "back-2")
      host.snapshot = list(3, 1)
      waitForValue(host.inputControl, "text", "b")
      compare(host.selectedId, "beta")
      waitForCondition(function() { return host.viewActionsControl.itemAt(0).activeFocus })
    }
    function longDetail(revision, depth) {
      const view = detail(revision)
      view.content.id = "detail-" + depth
      view.content.depth = depth
      view.content.body = Array(100).fill("A long detail line.").join("\n\n")
      return view
    }
    function test_new_detail_resets_scroll_after_returning_to_list() {
      mouseClick(opener)
      waitForValue(host.inputControl, "activeFocus", true)
      host.snapshot = longDetail(2, 2)
      waitForValue(host.detailControl.bodyControl, "activeFocus", true)
      verify(waitForFrame(host.detailControl))
      keyClick(Qt.Key_PageDown)
      verify(host.detailControl.captureState().scroll > 0)
      keyClick(Qt.Key_Escape)
      host.snapshot = list(3, 1)
      waitForValue(host.inputControl, "activeFocus", true)
      host.snapshot = longDetail(4, 2)
      waitForValue(host.detailControl.bodyControl, "activeFocus", true)
      waitForCondition(function() { return host.detailControl.captureState().scroll === 0 })
    }
    function test_nested_detail_resets_scroll_and_back_restores_parent() {
      mouseClick(opener)
      host.snapshot = longDetail(2, 2)
      waitForValue(host.detailControl.bodyControl, "activeFocus", true)
      verify(waitForFrame(host.detailControl))
      keyClick(Qt.Key_PageDown)
      const parentScroll = host.detailControl.captureState().scroll
      verify(parentScroll > 0)
      keyClick(Qt.Key_Tab)
      verify(host.detailControl.actionsControl.itemAt(0).activeFocus)
      keyClick(Qt.Key_Space)
      host.snapshot = longDetail(3, 3)
      waitForCondition(function() { return host.detailControl.captureState().scroll === 0 })
      waitForValue(host.detailControl.bodyControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      host.snapshot = longDetail(4, 2)
      waitForCondition(function() { return host.detailControl.captureState().scroll === parentScroll })
      waitForCondition(function() { return host.detailControl.actionsControl.itemAt(0).activeFocus })
    }
    function test_list_detail_back_restores_filter_selection_and_action_focus() {
      mouseClick(opener)
      waitForValue(host.inputControl, "activeFocus", true)
      keyClick(Qt.Key_B)
      compare(host.selectedId, "beta")
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      verify(host.viewActionsControl.itemAt(0).activeFocus)
      keyClick(Qt.Key_Space)
      compare(actions.signalArguments[0][2], "detail-beta-1")
      host.busy = true
      host.snapshot = detail(2)
      host.busy = false
      waitForValue(host.detailControl.bodyControl, "activeFocus", true)
      verify(host.detailControl.bodyControl.text.indexOf("<img") < 0)
      verify(host.detailControl.bodyControl.text.indexOf("<a ") < 0)
      keyClick(Qt.Key_Return)
      compare(actions.signalArguments[1][2], "detail-copy-2")
      keyClick(Qt.Key_Tab)
      verify(host.detailControl.actionsControl.itemAt(0).activeFocus)
      keyClick(Qt.Key_Escape)
      compare(actions.signalArguments[2][2], "back-2")
      host.busy = true
      host.snapshot = list(3, 1)
      host.busy = false
      waitForValue(host.inputControl, "text", "b")
      compare(host.selectedId, "beta")
      waitForCondition(function() { return host.viewActionsControl.itemAt(0).activeFocus })
      keyClick(Qt.Key_Space)
      compare(actions.signalArguments[3][2], "detail-beta-3")
      host.snapshot = detail(4)
      waitForValue(host.detailControl.bodyControl, "activeFocus", true)
      mouseClick(host.detailControl.actionsControl.itemAt(0))
      compare(actions.signalArguments[4][2], "detail-copy-4")
      mouseClick(host.detailControl.backControl)
      compare(actions.signalArguments[5][2], "back-4")
      host.snapshot = list(5, 1)
      waitForCondition(function() { return host.viewActionsControl.itemAt(0).activeFocus })
      keyClick(Qt.Key_Escape)
      compare(host.inputControl.text, "")
      keyClick(Qt.Key_Escape)
      compare(host.opened, false)
      waitForValue(opener, "activeFocus", true)
      keyClick(Qt.Key_Return)
      waitForValue(host.inputControl, "activeFocus", true)
    }
    function test_pages_replace_and_use_the_latest_local_filter() {
      mouseClick(opener)
      waitForValue(host.inputControl, "activeFocus", true)
      keyClick(Qt.Key_Backtab)
      keyClick(Qt.Key_Backtab)
      verify(host.listControl.nextPageControl.activeFocus)
      keyClick(Qt.Key_Space)
      compare(actions.signalArguments[0][2], "next-1")
      host.busy = true
      mouseClick(host.inputControl)
      keyClick(Qt.Key_G)
      compare(host.filtered.length, 0)
      host.snapshot = list(2, 2)
      host.busy = false
      compare(host.inputControl.text, "g")
      compare(host.filtered.length, 1)
      compare(host.selectedId, "gamma")
      verify(host.inputControl.activeFocus)
      keyClick(Qt.Key_Return)
      compare(actions.signalArguments[1][2], "copy-gamma-2")
      keyClick(Qt.Key_Escape)
      verify(waitForFrame(host))
      mouseClick(host.listControl.previousPageControl)
      compare(actions.signalArguments[2][2], "previous-2")
      host.snapshot = list(3, 1)
      compare(host.selectedId, "alpha")
      compare(host.filtered.length, 2)
      host.externalError = "This page has expired. Generate again."
      compare(host.filtered.length, 2)
      host.externalError = ""
      mouseClick(host.listControl.backControl)
      waitForValue(opener, "activeFocus", true)
    }
    function test_mouse_pagination_keeps_typing_in_search() {
      mouseClick(opener)
      waitForValue(host.inputControl, "activeFocus", true)
      verify(waitForFrame(host))
      mouseClick(host.listControl.nextPageControl)
      compare(actions.signalArguments[0][2], "next-1")
      host.busy = true
      host.snapshot = list(2, 2)
      host.busy = false
      waitForValue(host.inputControl, "activeFocus", true)
      keyClick(Qt.Key_G)
      compare(host.inputControl.text, "g")
      compare(host.selectedId, "gamma")
    }
    function test_last_page_keeps_keyboard_focus_in_pagination() {
      mouseClick(opener)
      waitForValue(host.inputControl, "activeFocus", true)
      keyClick(Qt.Key_Backtab)
      keyClick(Qt.Key_Backtab)
      verify(host.listControl.nextPageControl.activeFocus)
      keyClick(Qt.Key_Space)
      host.busy = true
      host.snapshot = list(2, 2)
      host.busy = false
      waitForValue(host.listControl.previousPageControl, "activeFocus", true)
      keyClick(Qt.Key_Space)
      compare(actions.signalArguments[1][2], "previous-2")
      host.busy = true
      host.snapshot = list(3, 1)
      host.busy = false
      waitForValue(host.listControl.nextPageControl, "activeFocus", true)
    }
  }
}
