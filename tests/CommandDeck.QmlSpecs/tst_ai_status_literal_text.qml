import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// Provider errors, backend labels, the user's query, and filter names reach
// these labels from the daemon or the user. Markup in them must show as the
// characters it is made of, never as formatting.
Item {
  id: scene
  width: 900
  height: 700

  readonly property string markup: "<b>bold</b> <i>raw</i> &amp;"

  CommandDeck.Theme { id: theme; loadShellTokens: false }
  CommandDeck.AiController { id: direct }
  CommandDeck.AiAnswerPane {
    id: pane
    width: 420
    height: 320
    theme: theme
    assistantController: null
    directController: direct
    mode: "direct"
  }
  CommandDeck.AiStreamingSurface {
    id: responseSurface
    anchors.fill: parent
    theme: theme
    controller: direct
    opened: true
  }
  ListModel { id: rows }
  CommandDeck.SearchNavigationController { id: navigation; rows: rows }
  CommandDeck.CommandDeckSearchField {
    id: search
    width: 600
    theme: theme
    controller: navigation
    surfaceVisible: true
  }

  // The literal string drawn in the same font: a label that formats markup
  // measures narrower, because the tags and entities vanish.
  Text { id: literalProbe; textFormat: Text.PlainText; visible: false }

  TestCase {
    name: "AiStatusLiteralText"
    when: windowShown

    function literalWidth(item) {
      literalProbe.font = item.font
      literalProbe.text = item.text
      return literalProbe.implicitWidth
    }

    function verifyLiteral(objectName, owner) {
      const item = findChild(owner, objectName)
      verify(item !== null, objectName + " exists")
      verify(item.text.indexOf("<b>bold</b>") >= 0, objectName + " carries the markup string")
      const drawn = item.implicitWidth - item.leftPadding - item.rightPadding
      verify(Math.abs(drawn - literalWidth(item)) <= 1,
        objectName + " draws markup literally (" + drawn + " vs " + literalWidth(item) + ")")
    }

    function test_answer_pane_shows_backend_status_and_query_literally() {
      pane.backendLabel = scene.markup
      pane.query = scene.markup
      direct.state = "failed"
      direct.error = scene.markup
      verifyLiteral("aiAnswerBackend", pane)
      verifyLiteral("aiAnswerStatus", pane)
      verifyLiteral("aiAnswerQuery", pane)
    }

    function test_response_surface_shows_error_and_output_status_literally() {
      direct.state = "failed"
      direct.error = scene.markup
      direct.outputStatus = scene.markup
      verifyLiteral("aiResponseState", responseSurface)
      verifyLiteral("aiResponseOutputStatus", responseSurface)
    }

    function test_filter_hint_shows_filter_names_literally() {
      search.filterDescriptors = [{ id: "files", label: scene.markup, token: "file:", aliases: [] }]
      search.inputControl.text = "fi"
      compare(search.activeHintKind, "prefix")
      verifyLiteral("searchHintLabel", search)
      search.inputControl.text = ""
    }
  }
}
