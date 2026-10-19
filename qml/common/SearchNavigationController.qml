import QtQml
import "QueryExpressions.js" as QueryExpressions
import "Protocol.js" as Protocol

QtObject {
  id: root

  required property var rows
  property var filterDescriptors: []
  property var activeFilters: []
  readonly property string activeFilterId: {
    const first = activeFilters.find(value => !value.excluded)
    return first ? first.id : ""
  }
  property string queryText: ""
  property int selectedIndex: 0
  property int focusRevision: 0
  property var currentResult: null
  property bool convertingFilter: false
  property string herdrWorkspaceId: ""
  property string herdrWorkspaceTitle: ""
  property var herdrParentSearch: null
  property string restoreSelectionId: ""
  // The query text the last recall produced; Up keeps stepping back through
  // history while the query still reads exactly this.
  property string recalledText: ""
  // Escape clears a non-empty search before it closes the launcher.
  property bool escapeClearsQuery: false

  signal searchRequested()
  signal settingsRequested()
  signal helpRequested()
  signal dismissRequested()
  // Shift+Escape: back to an empty search on Home from any filter or view.
  signal rootRequested()
  signal primaryRequested()
  signal selectionIdentityChanged(var result)

  onSelectedIndexChanged: refreshSelection()

  property ResultModelController resultModel: ResultModelController {
    rows: root.rows
  }

  function filterDescriptor(id) {
    for (let index = 0; index < filterDescriptors.length; index++) {
      if (filterDescriptors[index].id === id) return filterDescriptors[index]
    }
    return null
  }

  function filterLabel(value) {
    const descriptor = filterDescriptor(value.id)
    return descriptor && descriptor.label ? descriptor.label : (value.label || value.id)
  }

  function searchAccessibilityDescription() {
    const placeholder = "Search apps, files, config, windows, commands"
    if (activeFilters.length === 0) return placeholder
    const labels = activeFilters.map(value => {
      const label = filterLabel(value)
      return (value.excluded ? "exclude " : "include ") + label
    })
    return labels.join(", ") + ". " + placeholder
  }

  function handleSettingsShortcut(key, modifiers) {
    const control = (modifiers & Qt.ControlModifier) !== 0
    if (!control || key !== Qt.Key_Comma) return false
    settingsRequested()
    return true
  }

  function handleHelpShortcut(key, modifiers) {
    const control = (modifiers & Qt.ControlModifier) !== 0
    if (key !== Qt.Key_F1 && !(control && key === Qt.Key_Slash)) return false
    helpRequested()
    return true
  }

  function openSettings() {
    settingsRequested()
  }

  function dismiss() {
    if (escapeClearsQuery && herdrWorkspaceId.length === 0
        && (queryText.length > 0 || activeFilters.length > 0)) {
      activeFilters = []
      queryText = ""
      searchRequested()
      focusRevision++
      return
    }
    if (herdrWorkspaceId.length > 0) {
      if (queryText.length > 0) {
        queryText = ""
        searchRequested()
        focusRevision++
      } else {
        backFromHerdrSpace()
      }
      return
    }
    dismissRequested()
  }

  function browseHerdrSpace(result) {
    if (!result || result.sourceId !== Protocol.SourceIds.Herdr || !result.metadata
        || !result.metadata.herdrWorkspaceId) return
    if (!herdrParentSearch) {
      herdrParentSearch = { query: queryText, filters: activeFilters.slice(), selectedId: result.id }
    }
    herdrWorkspaceId = result.metadata.herdrWorkspaceId
    herdrWorkspaceTitle = result.title
    activeFilters = [{ id: "herdr-tabs", excluded: false }]
    queryText = ""
    searchRequested()
    focusRevision++
  }

  function backFromHerdrSpace() {
    const previous = herdrParentSearch
    clearHerdrBrowse()
    if (previous) {
      activeFilters = previous.filters
      queryText = previous.query
      restoreSelectionId = previous.selectedId
    }
    searchRequested()
    focusRevision++
  }

  function clearHerdrBrowse() {
    herdrWorkspaceId = ""
    herdrWorkspaceTitle = ""
    herdrParentSearch = null
    restoreSelectionId = ""
  }

  function showHerdrAttention() {
    activeFilters = [{ id: "herdr-attention", excluded: false }]
    queryText = ""
    searchRequested()
    focusRevision++
  }

  function removeFilter(id, excluded) {
    if (id === undefined) {
      activeFilters = []
    } else {
      activeFilters = activeFilters.filter(value =>
        value.id !== id || value.excluded !== (excluded === true))
    }
    searchRequested()
    focusRevision++
  }

  function removeFilterFromBackspace(cursorPosition) {
    if (cursorPosition !== 0 || activeFilters.length === 0) return false
    const last = activeFilters[activeFilters.length - 1]
    removeFilter(last.id, last.excluded)
    return true
  }

  // A row may name the whole query Tab should produce (a HEY refinement value);
  // otherwise Tab completes to the row's title.
  function completionText() {
    const completion = selectedResult()
    if (!completion) return ""
    const replacement = completion.metadata ? completion.metadata[Protocol.ResultMetadataKeys.CompletionText] : undefined
    return replacement ? String(replacement) : (completion.title || "")
  }

  function tryConvertPrefix(value) {
    if (convertingFilter || filterDescriptors.length === 0) {
      return false
    }
    const excluded = value.length > 1 && value[0] === "-"
    const candidate = excluded ? value.substring(1) : value
    const colon = candidate.indexOf(":")
    if (colon <= 0) return false
    const token = candidate.substring(0, colon + 1).toLowerCase()
    // These aliases choose a backend as well as a source. Keep their spelling
    // in the query so the daemon can distinguish them from the shared AI chip.
    if (!excluded && /^(codex|claude|cursor|grok):$/.test(token)) return false
    for (let index = 0; index < filterDescriptors.length; index++) {
      const descriptor = filterDescriptors[index]
      const tokens = [descriptor.token].concat(descriptor.aliases || [])
      if (tokens.some(candidate => String(candidate).toLowerCase() === token)) {
        convertingFilter = true
        if (!activeFilters.some(value => value.id === descriptor.id
                                      && value.excluded === excluded)) {
          activeFilters = activeFilters.concat([{ id: descriptor.id, excluded: excluded }])
        }
        queryText = candidate.substring(colon + 1).replace(/^\s+/, "")
        convertingFilter = false
        return true
      }
    }
    return false
  }

  function queryExpression() {
    return QueryExpressions.parse(queryText, activeFilters)
  }

  function applySuggestion(suggestion) {
    const expression = suggestion && suggestion.expression ? suggestion.expression : ({})
    activeFilters = (expression.includeFilterIds || []).map(value => ({
      id: value,
      excluded: false
    })).concat((expression.excludeFilterIds || []).map(value => ({
      id: value,
      excluded: true
    })))
    queryText = suggestion && suggestion.text ? suggestion.text : ""
  }

  function buildRows(list) { return resultModel.buildRows(list) }

  function replaceResults(results, resetSelection, completed = true) {
    // A missing ID in a partial refresh is not a removed result. Keep the
    // visible list usable until done, then restore the latest user selection.
    if (!resetSelection && !completed) return false
    const previous = resetSelection ? null : selectedResult()
    const previousId = restoreSelectionId || (previous ? previous.id : "")
    syncRows(buildRows(results))
    const restored = previousId.length > 0 ? indexOfResultId(previousId) : -1
    selectedIndex = restored >= 0 ? restored : firstSelectableIndex()
    if (completed) restoreSelectionId = ""
    refreshSelection()
    return true
  }

  function syncRows(next) {
    resultModel.syncRows(next)
    refreshSelection()
  }

  function firstSelectableIndex() { return resultModel.firstSelectableIndex() }
  function lastSelectableIndex() { return resultModel.lastSelectableIndex() }
  function indexOfResultId(id) { return resultModel.indexOfResultId(id) }
  function selectedResult() { return resultModel.selectedResult(selectedIndex) }
  function resultAt(index) { return resultModel.selectedResult(index) }
  function rowAt(index) { return resultModel.rowAt(index) }
  function rowCount() { return rows.count }

  function moveSelection(delta) {
    selectedIndex = resultModel.moveSelection(selectedIndex, delta)
  }

  function jumpSection(direction) {
    selectedIndex = resultModel.sectionJump(selectedIndex, direction)
  }

  function invokeOrdinal(number) {
    const index = resultModel.indexOfOrdinal(number)
    if (index < 0) return
    selectedIndex = index
    primaryRequested()
  }

  function invokePrimary() {
    primaryRequested()
  }

  function refreshSelection() {
    const next = selectedResult()
    const previousId = currentResult && currentResult.id ? currentResult.id : ""
    const nextId = next && next.id ? next.id : ""
    currentResult = next
    if (previousId !== nextId) selectionIdentityChanged(next)
  }

  // Up from the first result asks for an older query when the search is empty
  // or still shows a recalled one; anywhere else it moves the selection.
  function recallWanted() {
    if (selectedIndex > firstSelectableIndex()) return ""
    if (queryText.length === 0 && activeFilters.length === 0) return "restart"
    return recalledText.length > 0 && queryText === recalledText ? "continue" : ""
  }

  function clear() {
    clearHerdrBrowse()
    recalledText = ""
    activeFilters = []
    queryText = ""
    selectedIndex = 0
    currentResult = null
  }
}
