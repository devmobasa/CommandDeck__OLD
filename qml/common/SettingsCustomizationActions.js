.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function saveEditors(root) {
  const preferences = root.clonePreferences()
  if (!preferences) return false
  let graphicalArguments
  let terminalArguments
  try {
    graphicalArguments = JSON.parse(root.part_graphicalEditorArguments.text)
    terminalArguments = JSON.parse(root.part_terminalEditorArguments.text)
  } catch (error) {
    return false
  }
  if (!Array.isArray(graphicalArguments) || !Array.isArray(terminalArguments)) return false
  preferences.preferredEditor = root.part_preferredEditorChoice.currentIndex === 1
    ? Protocol.EditorKinds.Terminal
    : Protocol.EditorKinds.Graphical
  preferences.graphicalEditor = {
    executable: root.part_graphicalEditorExecutable.text.trim(),
    arguments: graphicalArguments,
    terminal: false
  }
  preferences.terminalEditor = {
    executable: root.part_terminalEditorExecutable.text.trim(),
    arguments: terminalArguments,
    terminal: true
  }
  root.preferencesChanged(preferences)
  return true
}

function syncEditorControls(root) {
  const preferences = root.snapshot ? root.snapshot.preferences : null
  if (!preferences) return
  root.part_preferredEditorChoice.currentIndex = preferences.preferredEditor === Protocol.EditorKinds.Terminal ? 1 : 0
  const graphical = preferences.graphicalEditor || ({
    executable: "code", arguments: ["--goto", "{path}:{line}:{column}"]
  })
  const terminal = preferences.terminalEditor || ({
    executable: "nvim", arguments: ["+call cursor({line},{column})", "{path}"]
  })
  root.part_graphicalEditorExecutable.text = graphical.executable || ""
  root.part_graphicalEditorArguments.text = JSON.stringify(graphical.arguments || [])
  root.part_terminalEditorExecutable.text = terminal.executable || ""
  root.part_terminalEditorArguments.text = JSON.stringify(terminal.arguments || [])
}

function moveFavorite(root, index, delta) {
  if (!root.favoritesReorderAvailable || !root.snapshot || !root.snapshot.customizations) return
  const target = index + delta
  if (target < 0 || target >= root.snapshot.customizations.length) return
  const ids = root.snapshot.customizations.map(value => value.stableId)
  const swap = ids[index]
  ids[index] = ids[target]
  ids[target] = swap
  root.favoritesReordered(ids)
}

function moveSource(root, index, delta) {
  moveSourceTo(root, index, index + delta)
}

// Moves one source to another place in the order, as the arrows and a drag do,
// and saves the renumbered order in one change.
function moveSourceTo(root, from, to) {
  const preferences = root.clonePreferences()
  if (!preferences) return

  const count = preferences.sources.length
  if (from === to || from < 0 || from >= count || to < 0 || to >= count) return

  const moved = preferences.sources.splice(from, 1)[0]
  preferences.sources.splice(to, 0, moved)
  for (let position = 0; position < count; position++) {
    preferences.sources[position].position = position + 1
  }
  root.preferencesChanged(preferences)
}

function mutateSource(root, index, field, value) {
  const preferences = root.clonePreferences()
  if (!preferences || index < 0 || index >= preferences.sources.length) return
  preferences.sources[index][field] = value
  root.preferencesChanged(preferences)
}

// A choice made in Settings, or the configuration's value
// while none is made (the daemon sends those defaults with the snapshot).
function historyChoice(root, name) {
  const preferences = root.snapshot && root.snapshot.preferences ? root.snapshot.preferences : null
  const chosen = preferences ? preferences[name] : null
  if (chosen === true || chosen === false) return chosen
  const defaults = root.snapshot && root.snapshot.defaults ? root.snapshot.defaults : null
  return !!defaults && defaults[name] === true
}

function setHistoryChoice(root, name, value) {
  const preferences = root.clonePreferences()
  if (!preferences) return
  preferences[name] = value === true
  root.preferencesChanged(preferences)
}

function clonePreferences(root) {
  const preferences = root.snapshot && root.snapshot.preferences
    ? JSON.parse(JSON.stringify(root.snapshot.preferences))
    : null
  if (preferences && preferences.webFallbackQuicklinkId
      && !root.hasFallback(preferences.webFallbackQuicklinkId)) {
    preferences.webFallbackQuicklinkId = null
  }
  return preferences
}

function removeUnavailable(root, customizationId) {
  if (root.loading || root.snapshotMutationBusy) return false
  root.orphanRemoveRequested(customizationId)
  return true
}

function fallbackChoices(root) {
  const configured = [{ id: "", title: "None" }].concat(root.fallbackQuicklinks || [])
  if (!root.fallbackSelectionUnavailable) return configured
  return [{
    id: root.selectedFallbackId,
    title: "Unavailable · " + root.selectedFallbackId,
    unavailable: true
  }].concat(configured)
}

function saveSavedSearch(root) {
  const name = root.part_savedSearchName.text.trim()
  if (name.length < 1 || name.length > 64) return false
  const editing = root.editingSavedSearch
  root.savedSearchSaveRequested({
    id: editing ? editing.id : "",
    name: name,
    expression: editing ? editing.expression : root.currentExpression,
    shortcut: root.part_savedSearchShortcut.text.trim().length > 0
      ? root.part_savedSearchShortcut.text.trim()
      : null,
    showOnHome: root.part_savedSearchHome.checked,
    position: editing ? editing.position : 0
  })
  root.savedSearchAwaitingAck = true
  return true
}

function clearSavedSearchEditor(root) {
  root.editingSavedSearch = null
  root.part_savedSearchName.text = ""
  root.part_savedSearchShortcut.text = ""
  root.part_savedSearchHome.checked = true
}

function editSavedSearch(root, search) {
  if (!search || search.unavailable === true) return
  root.editingSavedSearch = search
  root.part_savedSearchName.text = search.name || ""
  root.part_savedSearchShortcut.text = search.shortcut || ""
  root.part_savedSearchHome.checked = search.showOnHome === true
  root.part_savedSearchName.forceActiveFocus()
}

function saveFilterAlias(root) {
  const alias = root.part_filterAliasInput.text.trim().replace(/:+$/, "")
  if (!/^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$/.test(alias)
      || root.filterAliasTokenIsBuiltIn(alias)
      || root.part_filterAliasTarget.currentIndex < 0) {
    return false
  }
  const target = root.filterDescriptors[root.part_filterAliasTarget.currentIndex]
  root.filterAliasSetRequested(alias, target.id)
  root.part_filterAliasInput.text = ""
  return true
}

function filterAliasTokenIsBuiltIn(root, value) {
  const token = String(value || "").trim().replace(/:+$/, "").toLowerCase()
  if (token === Ui.ReservedShortcut.Home || token === Ui.ReservedShortcut.Settings) return true
  return (root.filterDescriptors || []).some(descriptor =>
    [descriptor.token].concat(descriptor.aliases || []).some(candidate =>
      String(candidate).replace(/:+$/, "").toLowerCase() === token))
}

function hasFallback(root, fallbackId) {
  return (root.fallbackQuicklinks || []).some(value => value.id === fallbackId)
}

function shortcutStatus(root, binding) {
  if (binding.status === Protocol.ShortcutStatuses.Configured && binding.chords.length > 0) {
    return binding.chords.join(", ")
  }
  if (binding.status === Protocol.ShortcutStatuses.Unavailable) {
    return binding.detail && binding.detail.length > 0
      ? "Unavailable · " + binding.detail
      : "Unavailable"
  }
  return binding.detail && binding.detail.length > 0
    ? "Not configured · " + binding.detail
    : "Not configured"
}

function sourceIcon(root, sourceId) {
  const descriptor = root.sourceDescriptor(sourceId)
  return descriptor && descriptor.icon ? descriptor.icon : ""
}

function sourceLabel(root, sourceId) {
  const descriptor = root.sourceDescriptor(sourceId)
  if (descriptor && descriptor.label) return descriptor.label
  // Never return undefined: an unlabelled row is indistinguishable from
  // every other row.
  return sourceId ? String(sourceId) : "Unknown source"
}

// A source such as HEY mail never appears on Home; Settings shows no switch for it.
function sourceHomeAvailable(root, sourceId) {
  const descriptor = root.sourceDescriptor(sourceId)
  return !descriptor || descriptor.homeAvailable !== false
}

function sourceDescriptor(root, sourceId) {
  for (let index = 0; index < root.sourceDescriptors.length; index++) {
    if (root.sourceDescriptors[index].id === sourceId) return root.sourceDescriptors[index]
  }
  return null
}

function renderedShortcutStatusAt(root, index) {
  const item = index >= 0 && index < root.part_shortcutRepeater.count
    ? root.part_shortcutRepeater.itemAt(index)
    : null
  return item ? item.statusText : ""
}

function renderedSourceIconAt(root, index) {
  const item = root.renderedSourceAt(index)
  return item ? item.iconSource : ""
}

function renderedSourceWidthAt(root, index) {
  const item = root.renderedSourceAt(index)
  return item ? item.width : 0
}

function renderedSourceLabelAt(root, index) {
  const item = root.renderedSourceAt(index)
  return item ? item.label : ""
}

function renderedSourceAt(root, index) {
  return index >= 0 && index < root.part_sourceRepeater.count
    ? root.part_sourceRepeater.itemAt(index)
    : null
}

function renderedSavedSearchAt(root, index) {
  return index >= 0 && index < root.part_savedSearchRepeater.count
    ? root.part_savedSearchRepeater.itemAt(index)
    : null
}

function renderedFilterAliasAt(root, index) {
  return index >= 0 && index < root.part_filterAliasRepeater.count
    ? root.part_filterAliasRepeater.itemAt(index)
    : null
}

function renderedAgentGrantAt(root, index) {
  return index >= 0 && index < root.part_agentGrantRepeater.count
    ? root.part_agentGrantRepeater.itemAt(index)
    : null
}
