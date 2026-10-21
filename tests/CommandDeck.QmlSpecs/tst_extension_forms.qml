import QtQuick
import QtQuick.Controls
import QtTest
import "../../qml/common" as CommandDeck

Item {
  width: 800; height: 650
  CommandDeck.Theme { id: theme; loadShellTokens: false }
  Button { id: opener; text: "Open draft"; onClicked: host.opened = true }
  CommandDeck.ExtensionViewHost {
    id: host
    anchors.fill: parent
    theme: theme
    returnFocus: opener
    onBackRequested: opened = false
  }
  SignalSpy { id: submissions; target: host; signalName: "formSubmitted" }
  DeadlineTestCase {
    name: "ExtensionForms"
    when: windowShown
    function form(revision, depth) {
      return { revision: revision, content: { id: "draft", kind: "form", title: "Draft", depth: depth || 1,
        fields: [{ id: "title", kind: "text", label: "Title", required: true, maxLength: 12 },
          { id: "body", kind: "multiline", label: "Body" },
          { id: "category", kind: "choice", label: "Category", choices: [{ value: "note", label: "Note" }, { value: "task", label: "Task" }] },
          { id: "done", kind: "checkbox", label: "Done" }],
        submit: { id: "save", title: "Save", handle: "save-" + revision } } }
    }
    function editor(index) { return host.formControl.fieldsControl.itemAt(index).editor }
    function init() { host.opened = false; host.busy = false; host.snapshot = { revision: 0, content: {} }; host.snapshot = form(1); submissions.clear() }
    function test_open_validate_edit_submit_cancel_keyboard() {
      mouseClick(opener)
      waitForValue(editor(0), "activeFocus", true)
      mouseClick(host.formControl.submitControl)
      compare(submissions.count, 0)
      compare(host.formControl.errors.title, "This field is required.")
      waitForValue(editor(0), "activeFocus", true)
      keyClick(Qt.Key_A)
      keyClick(Qt.Key_Tab)
      verify(editor(1).activeFocus)
      keyClick(Qt.Key_B)
      keyClick(Qt.Key_Return)
      keyClick(Qt.Key_C)
      keyClick(Qt.Key_Tab)
      verify(editor(2).activeFocus)
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Tab)
      verify(editor(3).activeFocus)
      keyClick(Qt.Key_Space)
      keyClick(Qt.Key_Tab)
      verify(host.formControl.submitControl.activeFocus)
      keyClick(Qt.Key_Space)
      compare(submissions.count, 1)
      compare(submissions.signalArguments[0][3].title, "a")
      compare(submissions.signalArguments[0][3].body, "b\nc")
      compare(submissions.signalArguments[0][3].done, true)
      host.busy = true
      keyClick(Qt.Key_Space)
      compare(submissions.count, 1)
      keyClick(Qt.Key_Escape)
      compare(host.opened, false)
      waitForValue(opener, "activeFocus", true)
    }
    function test_keyboard_focus_is_visible_on_every_form_control() {
      mouseClick(opener)
      waitForValue(editor(0), "activeFocus", true)
      compare(editor(0).background.border.width, 2)
      compare(editor(0).background.border.color, theme.accent)
      compare(host.formControl.submitControl.kind, "primary")
      compare(host.formControl.cancelControl.kind, "secondary")

      keyClick(Qt.Key_Tab)
      waitForValue(editor(1), "activeFocus", true)
      compare(editor(1).background.border.width, 2)
      compare(editor(1).background.border.color, theme.accent)

      keyClick(Qt.Key_Tab)
      waitForValue(editor(2), "activeFocus", true)
      compare(editor(2).background.border.width, 2)
      compare(editor(2).background.border.color, theme.accent)

      keyClick(Qt.Key_Tab)
      waitForValue(editor(3), "activeFocus", true)
      compare(editor(3).background.border.width, 2)
      compare(editor(3).background.border.color, theme.accent)

      keyClick(Qt.Key_Tab)
      waitForValue(host.formControl.submitControl, "activeFocus", true)
      compare(host.formControl.submitControl.kind, "primary")
      compare(host.formControl.cancelControl.kind, "secondary")
      compare(host.formControl.submitControl.focusRingVisible, true)
      compare(host.formControl.cancelControl.focusRingVisible, false)

      keyClick(Qt.Key_Tab)
      waitForValue(host.formControl.cancelControl, "activeFocus", true)
      compare(host.formControl.submitControl.kind, "primary")
      compare(host.formControl.cancelControl.kind, "secondary")
      compare(host.formControl.submitControl.focusRingVisible, false)
      compare(host.formControl.cancelControl.focusRingVisible, true)
    }
    function test_form_buttons_emphasize_hover_and_keyboard_focus() {
      mouseClick(opener)
      waitForValue(editor(0), "activeFocus", true)

      const cancel = host.formControl.cancelControl
      mouseMove(cancel, cancel.width / 2, cancel.height / 2)
      waitForValue(cancel, "hovered", true)
      compare(cancel.background.border.width, 3)
      compare(cancel.background.border.color, theme.text)
      compare(cancel.background.color, Qt.rgba(theme.accent.r, theme.accent.g, theme.accent.b, 0.38))

      mouseMove(host, host.width - 4, 4)
      for (let index = 0; index < 5; index++) keyClick(Qt.Key_Tab)
      waitForValue(cancel, "activeFocus", true)
      compare(cancel.background.border.width, 3)
      compare(cancel.background.border.color, theme.text)
      compare(cancel.background.color, Qt.rgba(theme.accent.r, theme.accent.g, theme.accent.b, 0.58))
      compare(cancel.background.children[0].border.width, 4)
    }
    function test_refresh_preserves_edits_and_ignores_stale_validation() {
      mouseClick(opener)
      waitForValue(editor(0), "activeFocus", true)
      keyClick(Qt.Key_A)
      const revision = host.formControl.formRevision
      const next = form(2)
      next.content.validatedFormRevision = revision - 1
      next.content.fieldErrors = { title: "Old error" }
      host.snapshot = next
      waitForValue(editor(0), "activeFocus", true)
      compare(editor(0).text, "a")
      compare(host.formControl.errors.title, undefined)
      const current = form(3)
      current.content.validatedFormRevision = revision
      current.content.fieldErrors = { title: "Current error" }
      host.snapshot = current
      compare(host.formControl.errors.title, "Current error")
      mouseClick(host.formControl.submitControl)
      compare(submissions.count, 1)
    }
    function test_push_form_starts_fresh_and_back_restores_parent_edits() {
      mouseClick(opener)
      waitForValue(editor(0), "activeFocus", true)
      keyClick(Qt.Key_A)
      host.snapshot = form(2, 2)
      waitForValue(editor(0), "activeFocus", true)
      compare(editor(0).text, "")
      keyClick(Qt.Key_B)
      host.snapshot = form(3, 1)
      waitForValue(editor(0), "text", "a")
      waitForValue(editor(0), "activeFocus", true)
    }
    function test_new_form_after_list_starts_with_fresh_values() {
      mouseClick(opener)
      waitForValue(editor(0), "activeFocus", true)
      keyClick(Qt.Key_A)
      host.snapshot = { revision: 2, content: { id: "list", kind: "list", title: "List", items: [] } }
      host.snapshot = form(3)
      waitForValue(editor(0), "activeFocus", true)
      compare(editor(0).text, "")
    }
    function test_removed_focused_field_falls_back_to_first_input() {
      mouseClick(opener)
      waitForValue(editor(0), "activeFocus", true)
      keyClick(Qt.Key_Tab)
      verify(editor(1).activeFocus)
      const next = form(2)
      next.content.fields = [next.content.fields[0]]
      host.snapshot = next
      waitForValue(editor(0), "activeFocus", true)
      keyClick(Qt.Key_A)
      compare(editor(0).text, "a")
    }
  }
}
