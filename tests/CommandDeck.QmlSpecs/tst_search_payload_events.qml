import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/Protocol.js" as Protocol

EventPathFixture {
  id: scene
  property int sequence: 0
  property var sent: []

  Deck.SearchSessionController {
    id: session
    nextRequestId: function() { return ++scene.sequence }
    replaceResults: function() { return true }
    opened: true
    query: scene.part_controller.queryText
    filters: scene.part_controller.activeFilters
    onRequest: message => scene.sent = scene.sent.concat([message])
  }

  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_search_payload"
    function test_explicit_assistant_backend_survives_typing_data() {
      return ["claude", "codex", "cursor", "grok"].map(value => ({tag: value, prefix: value}))
    }

    function test_explicit_assistant_backend_survives_typing(data) {
      const previous = fixture.part_controller.filterDescriptors
      fixture.part_controller.filterDescriptors = previous.concat([
        {id: "ai", label: "AI", token: "ai:", aliases: ["claude:", "codex:", "cursor:", "grok:"]}
      ])
      try {
        mouseClick(fixture.part_searchField.inputControl)
        verify(fixture.part_searchField.inputControl.activeFocus)
        typeText(data.prefix + ": hi")
        session.search()
        const request = scene.sent.filter(value => value.operation === Protocol.Operations.Search).slice(-1)[0]
        compare(request.query, data.prefix + ": hi")
        verify(fixture.part_searchField.inputControl.activeFocus)
      } finally {
        session.clear()
        fixture.part_controller.filterDescriptors = previous
      }
    }

    function test_calculator_negative_operand_is_not_an_exclusion() {
      mouseClick(fixture.part_searchField.inputControl)
      verify(fixture.part_searchField.inputControl.activeFocus)
      typeText("= -1")
      session.search()
      const request = scene.sent.filter(value => value.operation === Protocol.Operations.Search).slice(-1)[0]
      compare(request.query, "= -1")
      compare(request.expression.clauses, [])
      session.clear()
    }
  }
}
