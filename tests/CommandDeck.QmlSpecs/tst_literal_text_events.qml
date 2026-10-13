import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// Result titles, action titles, and file names come from the daemon, from
// extensions, and from the file system. Text that looks like markup is shown
// as written, with real keys still acting on it.
EventPathFixture {
  id: scene

  LiteralTextProbe { id: literal }

  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_literal_text"

    function test_the_action_panel_shows_titles_as_written() {
      const resultTitle = "<i>Invoice</i> &amp; <b>receipt</b>"
      const payTitle = "<font color='red'>Pay</font> <span style='font-size:40px'>now</span>"
      const result = { id: "markup", title: resultTitle, kind: "application",
        actions: [{ id: "pay", title: payTitle, kind: "run-argv" },
          { id: "hide", title: "<s>Hide</s> &lt;this&gt;", kind: "hide" }] }
      fixture.part_controller.replaceResults([result], true)
      fixture.part_actionPanel.openFor(fixture.part_controller.selectedResult())
      tryVerify(function() { return fixture.part_actionPanel.filterControl.activeFocus })
      verify(waitForRendering(fixture.part_actionPanel))

      compare(literal.problem(fixture.part_actionPanel.actionItemAt(0), payTitle), "")
      compare(literal.problem(fixture.part_actionPanel.actionItemAt(1), "<s>Hide</s> &lt;this&gt;"), "")
      compare(literal.problem(fixture.part_actionPanel, resultTitle), "", "the panel names its result as written")

      keyClick(Qt.Key_Return)
      compare(fixture.part_actionPanel.chosen, "pay")
    }

    function test_the_footer_shows_the_primary_action_title_as_written() {
      const title = "<b>Open</b> &amp; <i>close</i>"
      fixture.part_footerActions.primaryActionTitle = title
      verify(waitForRendering(fixture.part_footerActions))
      compare(literal.problem(fixture.part_footerActions, title), "")
    }

    function test_file_chips_show_file_names_as_written() {
      const name = "<b>report</b> &amp; notes.txt"
      const bar = createTemporaryObject(fixture.part_fileSelectionComponent, fixture, {
        width: 800, theme: fixture.part_theme, entries: [{ id: "file:a", title: name, path: "/tmp/a" }]
      })
      verify(bar !== null)
      verify(waitForRendering(bar))
      compare(literal.problem(bar, name + " ×"), "")

      bar.firstControl.forceActiveFocus()
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Return)
      compare(bar.removed.id, "file:a")
    }
  }
}
