import QtQuick
import "Protocol.js" as Protocol
import "." as CommandDeck

// The launcher's result list delegate over a real ListView: each row builds a
// header or a result, a refresh keeps delegates whose rows did not change, and
// a row that turns from result to header swaps what it shows.
QtObject {
  id: spec
  required property var host
  property int repaints: 0
  property bool compactLayout: false
  property bool controlHeld: false

  property ListModel rowsFixture: ListModel {
    id: listRows
  }

  property CommandDeck.SearchNavigationController navigationFixture: CommandDeck.SearchNavigationController {
    id: listNavigation
    rows: listRows
  }

  property Connections repaintCounter: Connections {
    target: listRows
    function onDataChanged() { spec.repaints++ }
  }

  property ListView listFixture: ListView {
    id: resultList
    width: 600
    height: 1200
    model: listRows
    delegate: CommandDeck.ResultListDelegate {
      id: listRow
      width: resultList.width
      theme: spec.host.part_theme
      selected: !listRow.isHeader && listRow.index === listNavigation.selectedIndex
      compactLayout: spec.compactLayout
      controlHeld: spec.controlHeld
    }
  }

  function result(id, title, section, reference) {
    return {
      id: id, kind: Protocol.ResultKinds.File, sourceId: Protocol.SourceIds.Files,
      title: title, subtitle: "/tmp/" + title, invocationReference: reference,
      section: { id: section, title: section }, aliases: [], matchedRanges: [], actions: []
    }
  }

  function show(results, reset) {
    listNavigation.replaceResults(results, reset)
    resultList.forceLayout()
  }

  function loaded(index) {
    const row = resultList.itemAtIndex(index)
    return row ? row.item : null
  }

  function isResultRow(item) {
    return item !== null && item.titleControl !== undefined
  }

  function run() {
    const first = [result("file:a", "a.txt", "Files", "one"), result("file:b", "b.txt", "Files", "one")]
    show(first, true)
    listNavigation.selectedIndex = 2
    host.expect(resultList.count === 3 && !isResultRow(loaded(0)) && loaded(0).title === "Files"
        && isResultRow(loaded(1)) && isResultRow(loaded(2)),
      "result list rows build a section header or a result row, not both")
    host.expect(!loaded(1).selected && loaded(2).selected,
      "only the selected result row is highlighted")

    const rowItems = [loaded(0), loaded(1), loaded(2)]
    repaints = 0
    show(first.map(value => Object.assign({}, value, { invocationReference: "two" })), false)
    host.expect(repaints === 0 && rowItems.every((item, index) => loaded(index) === item)
        && listNavigation.currentResult.id === "file:b" && listNavigation.currentResult.invocationReference === "two",
      "a refresh with new invocation references keeps every row and the selection")

    show([first[0], result("file:b", "renamed.txt", "Files", "three")], false)
    host.expect(repaints === 1 && loaded(1) === rowItems[1] && loaded(2) === rowItems[2]
        && loaded(2).titleText === "renamed.txt",
      "a renamed result repaints only its own row")

    show([first[0], result("file:c", "c.txt", "Other", "four")], true)
    host.expect(!isResultRow(loaded(2)) && loaded(2).title === "Other" && isResultRow(loaded(3)),
      "a row that becomes a section header swaps its result row for a header")
    show(first, true)
    host.expect(isResultRow(loaded(2)) && loaded(2).titleText === "b.txt" && resultList.count === 3,
      "a header row that becomes a result shows the result again")

    verifyTitleHighlighting()
    verifyCompactLayout()
    verifyOmarchyState()
  }

  // An Omarchy row the daemon marks checked shows ✓, and one whose guard
  // failed is dimmed unless selected. A refresh that only changes that state
  // still repaints the row.
  function verifyOmarchyState() {
    const omarchy = (id, metadata) => Object.assign(result(id, id, "Omarchy", "eight"), {
      kind: Protocol.ResultKinds.Omarchy, metadata: metadata })
    show([omarchy("dark", { omarchyChecked: "true" }), omarchy("stale", { omarchyUnavailable: "true" })], true)
    listNavigation.selectedIndex = 1
    const dark = loaded(1)
    const stale = loaded(2)
    host.expect(dark.checkedMarkControl.visible && dark.accessibilityName.indexOf(", checked") >= 0
        && !stale.checkedMarkControl.visible && stale.accessibilityName.indexOf("unavailable here") >= 0,
      "a checked Omarchy row shows a check mark and both states are announced")
    host.expect(stale.contentOpacity === 0.5 && dark.contentOpacity === 1,
      "an unavailable Omarchy row is dimmed")
    listNavigation.selectedIndex = 2
    host.expect(stale.contentOpacity === 1, "a selected unavailable row is shown in full")
    show([omarchy("dark", {}), omarchy("stale", { omarchyUnavailable: "true" })], false)
    host.expect(!loaded(1).checkedMarkControl.visible, "a refresh that clears the check repaints the row")
  }

  // The compact layout puts every row on one line and shows its Ctrl+number
  // badge only while Ctrl is held; the default layout always shows it.
  function verifyCompactLayout() {
    show([result("file:a", "a.txt", "Files", "seven")], true)
    const row = loaded(1)
    const twoLines = row.implicitHeight
    host.expect(!row.compact && row.ordinalBadgeControl.visible,
      "a file row takes two lines and shows its badge in the default layout")
    compactLayout = true
    host.expect(row.compact && !row.ordinalBadgeControl.visible && row.implicitHeight < twoLines,
      "the compact layout puts a file row on one line and hides its badge ("
        + twoLines + " to " + row.implicitHeight + ")")
    controlHeld = true
    host.expect(row.ordinalBadgeControl.visible, "holding Ctrl shows the badge in the compact layout")
    compactLayout = false
    controlHeld = false
  }

  // Marks each highlighted run with brackets, whatever the accent colour.
  function highlightRuns(markup) {
    return markup.replace(/<font color="#[0-9a-f]{6}"><b>/g, "[").replace(/<\/b><\/font>/g, "]")
  }

  // A title shares the matched-field highlighter: overlapping and touching
  // ranges make one run, and matched markup stays literal.
  function verifyTitleHighlighting() {
    show([Object.assign(result("file:m", "abcdefgh", "Files", "five"), {
      matchedRanges: [{ start: 0, length: 5 }, { start: 3, length: 2 }, { start: 5, length: 1 }]
    })], true)
    const merged = highlightRuns(loaded(1).titleControl.text)
    host.expect(merged === "[abcdef]gh",
      "overlapping and touching title ranges highlight one run (" + merged + ")")

    show([Object.assign(result("file:x", "x<y>&z", "Files", "six"), {
      matchedRanges: [{ start: 1, length: 3 }]
    })], true)
    const literal = highlightRuns(loaded(1).titleControl.text)
    host.expect(literal === "x[&lt;y&gt;]&amp;z", "a highlighted title keeps its markup literal (" + literal + ")")
  }
}
