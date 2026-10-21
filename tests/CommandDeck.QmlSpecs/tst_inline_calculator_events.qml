import QtQuick
import QtTest
import "../../qml/common/DeckSearchActions.js" as SearchActions
import "../../qml/common/QueryExpressions.js" as QueryExpressions
import "../../qml/common/DisplaySizing.js" as DisplaySizing

EventPathFixture {
  id: scene
  property bool opened: false
  onOpenedChanged: {
    part_searchField.surfaceVisible = opened
    if (opened) part_controller.focusRevision++
  }

  FontMetrics { id: wrapMetrics }

  QtObject {
    id: savedSearchHost
    readonly property var part_navigation: scene.part_controller
    function savedSearchText(expression) { return SearchActions.savedSearchText(savedSearchHost, expression) }
    function queryInputRequested(value) { scene.part_searchField.setInputText(value) }
    function requestSearch() {}
  }

  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_inline_calculator"

    function test_word_wrapping_at_default_width_data() {
      return [
        { tag: "expression", field: "expression", objectName: "calculatorExpression" },
        { tag: "result", field: "value", objectName: "calculatorValue" },
        { tag: "exact", field: "exactValue", objectName: "calculatorExactValue" }
      ]
    }

    function test_word_wrapping_at_default_width(data) {
      scene.opened = false
      scene.opened = true
      waitForValue(fixture.part_searchField.inputControl, "activeFocus", true)
      typeText("days between 10 may 2026 and 19 sep 2026")
      compare(fixture.part_controller.queryText, "days between 10 may 2026 and 19 sep 2026")
      const details = createTemporaryObject(fixture.part_calculatorComponent, fixture, {
        y: 130, width: DisplaySizing.calculate({}).contentWidth - 2 * fixture.part_theme.panelPadding - 32,
        theme: fixture.part_theme
      })
      verify(details !== null)
      const textItem = findChild(details, data.objectName)
      const lineWidths = []
      textItem.lineLaidOut.connect(function(line) { lineWidths[line.number] = line.implicitWidth })
      const range = "Sun 10 May 2026 → Sat 19 Sep 2026"
      details[data.field] = data.field === "exactValue" ? [range, range, range, range].join(" ") : range
      waitForCondition(() => textItem.lineCount > 1 && lineWidths.length === textItem.lineCount)

      wrapMetrics.font = textItem.font
      let remaining = textItem.text
      for (const width of lineWidths) {
        let boundary = -1
        for (let end = 1; end <= remaining.length; end++) {
          if (end < remaining.length && remaining[end - 1] !== " " && remaining[end] !== " ") continue
          const line = remaining.slice(0, end)
          if (Math.abs(wrapMetrics.advanceWidth(line) - width) < 1
              || Math.abs(wrapMetrics.advanceWidth(line.trim()) - width) < 1) {
            boundary = end
            break
          }
        }
        verify(boundary > 0, "line breaks only at spaces: " + remaining + " (line width " + width + ")")
        remaining = remaining.slice(boundary).trim()
      }
      compare(remaining, "", "every word is displayed")

      lineWidths.length = 0
      details[data.field] = "1234567890".repeat(20)
      waitForCondition(() => textItem.lineCount > 1 && lineWidths.length === textItem.lineCount)
      verify(lineWidths.every(width => width <= textItem.width + 1), "an unbroken number wraps within the view")
      mouseClick(findChild(details, "calculatorCopyButton"))
      verify(fixture.part_searchField.inputControl.activeFocus)
      typeText("x")
      verify(fixture.part_controller.queryText.endsWith("x"))
    }

    function test_copy_and_type_after_result_replacement() {
      scene.opened = false
      scene.opened = true
      waitForValue(fixture.part_searchField.inputControl, "activeFocus", true)
      typeText("100 * 5")
      compare(fixture.part_controller.queryText, "100 * 5")
      const row = { id: "calculator:100 * 5", kind: "calculator", title: "500", subtitle: "100 * 5",
        actions: [{ id: "copy", title: "Copy Result", kind: "copy-text" }] }
      fixture.part_controller.replaceResults([row], true)
      const details = createTemporaryObject(fixture.part_calculatorComponent, fixture, {
        y: 130, width: 700, theme: fixture.part_theme, value: row.title, expression: row.subtitle
      })
      verify(details !== null)
      details.copyRequested.connect(function() { fixture.part_controller.invokePrimary() })
      compare(findChild(details, "calculatorValue").text, "500")
      compare(findChild(details, "calculatorExpression").text, "100 * 5")
      keyClick(Qt.Key_Return)
      compare(fixture.part_controller.primaryCalls, 1)
      compare(fixture.part_controller.invokedId, row.id)
      verify(fixture.part_searchField.inputControl.activeFocus)

      fixture.part_controller.replaceResults([Object.assign({}, row)], false)
      mouseClick(findChild(details, "calculatorCopyButton"))
      compare(fixture.part_controller.primaryCalls, 2)
      verify(fixture.part_searchField.inputControl.activeFocus)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("days until 11 sep")
      compare(fixture.part_controller.queryText, "days until 11 sep")
      keyClick(Qt.Key_Escape)
      compare(fixture.part_controller.dismissCalls, 1)
      compare(fixture.part_controller.queryText, "days until 11 sep")
      scene.opened = false
      scene.opened = true
      waitForValue(fixture.part_searchField.inputControl, "activeFocus", true)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("100 * 5")
      compare(fixture.part_controller.queryText, "100 * 5")
    }

    function test_saved_date_search_restores_independent_clauses_and_exclusions() {
      scene.opened = false
      scene.opened = true
      waitForValue(fixture.part_searchField.inputControl, "activeFocus", true)
      typeText("days until 11 sep -launch")
      const expression = QueryExpressions.parse(fixture.part_controller.queryText, [])
      SearchActions.applySavedSearch(savedSearchHost, { expression: expression })
      compare(fixture.part_controller.queryText, '"days" "until" "11" "sep" -"launch"')
      const restored = QueryExpressions.parse(fixture.part_controller.queryText, [])
      compare(restored.clauses.map(value => value.text), ["days", "until", "11", "sep", "launch"])
      compare(restored.clauses.map(value => value.excluded), [false, false, false, false, true])
      verify(fixture.part_searchField.inputControl.activeFocus)
      typeText("x")
      verify(fixture.part_controller.queryText.endsWith("x"))
    }

    // DetailPane needs Quickshell's plugins, so the component suite covers its
    // binding of these details (PreviewAndPreferenceAssertions.qml).
    function test_currency_details_preserve_the_exact_value() {
      const details = createTemporaryObject(fixture.part_calculatorComponent, fixture, {
        y: 130, width: 700, theme: fixture.part_theme, value: "€89.02",
        exactValue: "89.0154886950329 EUR", exchangeRates: "ECB reference rates, 2026-10-21"
      })
      verify(details !== null)

      const exact = findChild(details, "calculatorExactValue")
      const rates = findChild(details, "calculatorExchangeRates")
      waitForValue(exact, "visible", true)
      waitForValue(rates, "visible", true)
      compare(exact.text, "Exact value: 89.0154886950329 EUR")
      compare(rates.text, "Exchange rates: ECB reference rates, 2026-10-21")
      verify(rates.y > exact.y)

      details.exactValue = ""
      details.exchangeRates = ""
      waitForValue(exact, "visible", false)
      waitForValue(rates, "visible", false)
    }
  }
}
