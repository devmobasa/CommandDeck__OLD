.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function applyCustomization(root, change) {
  const stableId = change ? change.stableId : ""
  function update(result) {
    if (!result) return result
    const catalogId = result.metadata && result.metadata.catalogResultId
      ? result.metadata.catalogResultId
      : result.id
    if (catalogId !== stableId) return result
    const copy = JSON.parse(JSON.stringify(result))
    copy.isFavorite = change.isFavorite
    copy.aliases = change.aliases || []
    return copy
  }
  root.part_searchSession.updateResults(update)
  // The navigation controller holds each row's latest result; the model copy
  // can lag in fields a row does not show, such as the invocation reference.
  const visible = []
  for (let index = 0; index < root.part_rows.count; index++) {
    const result = root.part_navigation.resultAt(index)
    if (result) visible.push(update(result))
  }
  if (visible.length > 0) {
    root.part_navigation.syncRows(root.part_navigation.buildRows(visible))
  }
  root.part_navigation.refreshSelection()
}

function finishAliasDialog(root) {
  root.aliasDialogOpen = false
  root.aliasDialogError = ""
  root.aliasTarget = null
  if (root.actionPanelOpen && root.activeActionPanel) root.activeActionPanel.restoreActionFocus()
  else root.focusRevision++
}

function setAlias(root, result, value) {
  if (!result) return
  root.enqueuePersonalization(Protocol.Operations.AliasSet, {
    resultId: result.id,
    alias: value && value.length > 0 ? value : null
  }, { kind: Ui.MutationKind.Alias, resultId: result.id })
}

function setFavorite(root, result, enabled) {
  if (!result) return
  root.enqueuePersonalization(Protocol.Operations.FavoriteSet, {
    resultId: result.id,
    enabled: enabled
  }, { kind: Ui.MutationKind.Favorite, resultId: result.id })
}

// Ctrl+Alt+Up or Down on a Home bookmark swaps it with the neighbour shown
// there, by ID, so bookmarks Home leaves out keep their places; the selection
// follows it once Home is searched again.
function moveFavorite(root, direction) {
  const navigation = root.part_navigation
  const result = navigation.selectedResult()
  if (!result || !result.canCustomize || !result.section || result.section.id !== Ui.HomeSection.Bookmarks) return 0
  const bookmarks = []
  for (let index = 0; index < navigation.rowCount(); index++) {
    const value = navigation.resultAt(index)
    if (value && value.section && value.section.id === Ui.HomeSection.Bookmarks) bookmarks.push(value.id)
  }
  const target = bookmarks.indexOf(result.id) + (direction > 0 ? 1 : -1)
  if (target < 0 || target >= bookmarks.length) return 0
  navigation.restoreSelectionId = result.id
  return root.enqueuePersonalization(Protocol.Operations.FavoriteMove, {
    resultId: result.id,
    targetResultId: bookmarks[target]
  }, { kind: Ui.MutationKind.Reorder })
}

function enqueuePersonalization(root, operation, values, context) {
  if (!root.daemonAvailable || root.reconciliationPending) {
    root.statusText = root.reconciliationWarning.length > 0
      ? root.reconciliationWarning
      : "Personalization is unavailable while the daemon is offline"
    return 0
  }
  const requestContext = context
    ? JSON.parse(JSON.stringify(context))
    : ({})
  if (operation === Protocol.Operations.CustomizationsExport) {
    requestContext.modalIntentGeneration = root.modalIntentGeneration
  }
  return root.part_personalization.enqueue(operation, values, requestContext)
}

function refreshSettingsSnapshot(root) {
  if (!root.preferencesSnapshot) {
    root.settingsSnapshot = null
    return
  }

  const snapshot = JSON.parse(JSON.stringify(root.preferencesSnapshot))
  if (root.part_personalization.draft) {
    snapshot.preferences = JSON.parse(JSON.stringify(root.part_personalization.draft))
  }
  root.settingsSnapshot = snapshot
}

function updatePreferences(root, preferences) {
  if (root.preferencesSnapshotLoading || !root.daemonAvailable || root.reconciliationPending) return
  root.part_personalization.submitPreferences(preferences)
}

function closeSettings(root) {
  root.settingsOpen = false
  root.focusRevision++
}

function requestPreferencesSnapshot(root, includeShortcuts) {
  if (!root.daemonAvailable) {
    root.preferencesSnapshotLoading = false
    root.statusText = "Settings are unavailable while the daemon is offline"
    return
  }
  if (root.part_personalization.preferenceInFlight || root.part_personalization.busy) {
    root.preferencesReloadPending = true
    return
  }
  root.preferencesReloadPending = false
  root.preferencesSnapshotLoading = true
  root.activePreferencesRequestId = ++root.requestSequence
  root.part_protocol.send({
    version: Protocol.Version,
    requestId: root.activePreferencesRequestId,
    operation: Protocol.Operations.PreferencesGet,
    includeShortcuts: includeShortcuts === undefined
      ? root.settingsOpen
      : includeShortcuts === true
  })
}
