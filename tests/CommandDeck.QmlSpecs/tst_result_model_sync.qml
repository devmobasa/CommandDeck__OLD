import QtQuick
import QtTest
import "../../qml/common" as Deck

// Refreshing the result list rewrites only rows whose shown content changed.
// The daemon issues a fresh invocation reference for every row on every
// search, so an unchanged refresh must still hand out the new references.
TestCase {
  id: testCase
  name: "ResultModelSync"

  property int dataChanges: 0
  property int structureChanges: 0

  ListModel { id: rows }

  Deck.SearchNavigationController {
    id: navigation
    rows: rows
  }

  Connections {
    target: rows
    function onDataChanged() { testCase.dataChanges++ }
    function onRowsInserted() { testCase.structureChanges++ }
    function onRowsRemoved() { testCase.structureChanges++ }
    function onModelReset() { testCase.structureChanges++ }
  }

  function result(index, reference, overrides) {
    return Object.assign({
      id: "file:" + index,
      kind: "file",
      sourceId: "files",
      title: "report-" + index + ".txt",
      subtitle: "/home/user/reports/report-" + index + ".txt",
      icon: "text-x-generic",
      invocationReference: reference + "-" + index,
      matchedRanges: [{ start: 0, length: 6 }],
      aliases: [],
      isFavorite: false,
      canCustomize: true,
      section: { id: "files", title: "Files" },
      metadata: { path: "/home/user/reports/report-" + index + ".txt" },
      actions: [{ id: "open", title: "Open", kind: "open-path" }]
    }, overrides || {})
  }

  function results(count, reference) {
    const list = []
    for (let index = 0; index < count; index++) list.push(result(index, reference))
    return list
  }

  function select(id) {
    navigation.selectedIndex = navigation.indexOfResultId(id)
    compare(navigation.currentResult.id, id)
  }

  function resetCounts() {
    dataChanges = 0
    structureChanges = 0
  }

  function init() {
    navigation.replaceResults([], true)
    resetCounts()
  }

  function test_rows_carry_a_flat_result_id() {
    navigation.replaceResults(results(3, "first"), true)
    compare(rows.get(0).resultId, "")
    compare(rows.get(1).resultId, "file:0")
    compare(navigation.indexOfResultId("file:2"), 3)
  }

  function test_an_identical_refresh_rewrites_no_row() {
    const first = results(50, "first")
    navigation.replaceResults(first, true)
    select("file:7")
    resetCounts()

    navigation.replaceResults(first, false)

    compare(dataChanges, 0)
    compare(structureChanges, 0)
    compare(navigation.currentResult.id, "file:7")
  }

  function test_fresh_invocation_references_reach_the_selection_without_a_repaint() {
    navigation.replaceResults(results(50, "first"), true)
    select("file:7")
    resetCounts()

    navigation.replaceResults(results(50, "second"), false)

    compare(dataChanges, 0)
    compare(navigation.currentResult.invocationReference, "second-7")
    compare(navigation.selectedResult().invocationReference, "second-7")
    compare(navigation.resultAt(navigation.indexOfResultId("file:30")).invocationReference, "second-30")
  }

  function test_fields_a_row_does_not_show_still_reach_the_selection() {
    navigation.replaceResults(results(5, "first"), true)
    select("file:2")
    const refreshed = results(5, "first")
    refreshed[2] = result(2, "first", {
      actions: [{ id: "stop", title: "Stop", kind: "systemd-unit" }],
      canCustomize: false
    })
    resetCounts()

    navigation.replaceResults(refreshed, false)

    compare(dataChanges, 0)
    compare(navigation.currentResult.actions[0].id, "stop")
    verify(!navigation.currentResult.canCustomize)
  }

  function test_a_changed_shown_field_rewrites_only_its_row() {
    const first = results(50, "first")
    navigation.replaceResults(first, true)
    select("file:3")
    const changed = first.slice()
    changed[3] = result(3, "first", { title: "renamed.txt" })
    changed[9] = result(9, "first", { metadata: { herdrAgentStatus: "blocked" } })
    resetCounts()

    navigation.replaceResults(changed, false)

    compare(dataChanges, 2)
    compare(structureChanges, 0)
    compare(rows.get(navigation.indexOfResultId("file:3")).value.title, "renamed.txt")
    compare(navigation.currentResult.title, "renamed.txt")
  }

  function test_selection_follows_its_id_when_rows_move_between_sections() {
    const first = results(6, "first")
    navigation.replaceResults(first, true)
    select("file:4")
    const promoted = [result(4, "second", { section: { id: "top", title: "Top Matches" } })]
      .concat(results(4, "second")).concat([result(5, "second")])

    navigation.replaceResults(promoted, false)

    compare(navigation.rowAt(0).sectionTitle, "Top Matches")
    compare(navigation.currentResult.id, "file:4")
    compare(navigation.selectedIndex, 1)
    compare(navigation.currentResult.invocationReference, "second-4")
  }

  // Closing the launcher or losing the daemon clears the model directly.
  function test_clearing_the_list_releases_the_cached_results() {
    navigation.replaceResults(results(4, "first"), true)
    compare(navigation.resultModel.syncedRows.length, 5)

    rows.clear()

    compare(navigation.resultModel.syncedRows.length, 0)
  }

  function test_shorter_results_remove_the_tail_and_a_direct_clear_is_recovered() {
    navigation.replaceResults(results(8, "first"), true)
    resetCounts()
    navigation.replaceResults(results(3, "first"), false)
    compare(rows.count, 4)
    compare(dataChanges, 1)

    rows.clear()
    compare(navigation.selectedResult(), null)
    compare(navigation.indexOfResultId("file:0"), -1)
    navigation.replaceResults(results(2, "second"), true)
    compare(rows.count, 3)
    compare(navigation.selectedResult().invocationReference, "second-0")
  }
}
