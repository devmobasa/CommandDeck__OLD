import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/DeckSearchActions.js" as SearchActions

EventPathFixture {
  id: scene
  property int sequence: 0
  property var sent: []

  QtObject {
    id: savedSearchHost
    readonly property var part_navigation: scene.part_controller
    function savedSearchText(expression) { return SearchActions.savedSearchText(savedSearchHost, expression) }
    function queryInputRequested(value) { scene.part_searchField.setInputText(value) }
    function requestSearch() { session.search() }
  }

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
    function test_saved_assistant_prompt_round_trips_through_settings_data() {
      return [
        {tag: "AI chip", prefix: "ai:", expectedPrefix: ""},
        {tag: "Claude", prefix: "claude:", expectedPrefix: "claude: "},
        {tag: "Codex", prefix: "codex:", expectedPrefix: "codex: "},
        {tag: "Cursor", prefix: "cursor:", expectedPrefix: "cursor: "},
        {tag: "Grok", prefix: "grok:", expectedPrefix: "grok: "},
        {tag: "Legacy Codex", prefix: "codex", expectedPrefix: "codex "}
      ]
    }

    function test_legacy_saved_assistant_prompt_runs_without_added_quotes() {
      const prompt = 'Is -1 negative? Keep  two spaces and "quotes".'
      const previous = fixture.part_controller.filterDescriptors
      fixture.part_controller.filterDescriptors = previous.concat([{id: "ai", label: "AI", token: "ai:"}])
      try {
        SearchActions.applySavedSearch(savedSearchHost, {expression: {
          includeFilterIds: ["ai"], excludeFilterIds: [],
          clauses: [{field: null, text: prompt, excluded: false}]
        }})
        const request = scene.sent.filter(value => value.operation === Protocol.Operations.Search).slice(-1)[0]
        compare(request.query, prompt)
        compare(request.expression.literalQuery, prompt)
        compare(request.expression.clauses, [])
      } finally {
        session.clear()
        fixture.part_controller.filterDescriptors = previous
      }
    }

    function test_saved_assistant_prompt_round_trips_through_settings(data) {
      const previous = fixture.part_controller.filterDescriptors
      fixture.part_controller.filterDescriptors = previous.concat([
        {id: "ai", label: "AI", token: "ai:", aliases: ["claude:"]}
      ])
      const prompt = 'Is -1 negative? Keep  two spaces and "quotes".'
      try {
        mouseClick(fixture.part_searchField.inputControl)
        verify(fixture.part_searchField.inputControl.activeFocus)
        typeText(data.prefix + " " + prompt)

        const panel = fixture.part_settingsPanel
        panel.currentExpression = fixture.part_controller.queryExpression()
        panel.visible = true
        panel.showPage("filters")
        tryCompare(panel.searchControl, "activeFocus", true)
        mouseClick(panel.savedSearchNameControl)
        typeText("Assistant prompt")
        keyClick(Qt.Key_Return)
        const saved = panel.savedSearchSaved
        compare(saved.expression.literalQuery, data.expectedPrefix + prompt)

        panel.visible = false
        fixture.part_controller.clear()
        SearchActions.applySavedSearch(savedSearchHost, saved)
        const request = scene.sent.filter(value => value.operation === Protocol.Operations.Search).slice(-1)[0]
        compare(request.query, data.expectedPrefix + prompt)
        compare(request.expression.clauses, [])
      } finally {
        fixture.part_settingsPanel.visible = false
        session.clear()
        fixture.part_controller.filterDescriptors = previous
      }
    }

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
