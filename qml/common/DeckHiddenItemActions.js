.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

// Hidden items: "Don't show this" from a result's actions, the undo strip, and
// the Settings → Hidden items list. Hide and restore go through the same
// personalization queue as bookmarks; the daemon answers with the authoritative
// customization state and the rows are updated locally from that reply.

var hiddenIdPrefix = "hidden:"

function stableIdOf(result) {
  return result && result.metadata && result.metadata.catalogResultId
    ? result.metadata.catalogResultId
    : result ? result.id : ""
}

function setHidden(root, result) {
  if (!result || !result.canCustomize) return 0
  return root.enqueuePersonalization(Protocol.Operations.HiddenSet, {
    resultId: result.id,
    enabled: true
  }, {
    kind: Ui.MutationKind.Hidden,
    resultId: result.id,
    stableId: stableIdOf(result),
    title: result.title || ""
  })
}

function restoreHidden(root, customizationId, fromSettings) {
  if (!customizationId) return 0
  return root.enqueuePersonalization(Protocol.Operations.HiddenRestore, {
    customizationId: customizationId
  }, {
    kind: Ui.MutationKind.Hidden,
    restore: true,
    customizationId: customizationId,
    fromSettings: fromSettings === true
  })
}

// Undo restores the last hidden item without opening it. The strip is cleared
// at once so a second Ctrl+Z cannot send the restore twice.
function undoHide(root) {
  const pending = root.lastHidden
  if (!pending) return 0
  root.lastHidden = null
  return restoreHidden(root, pending.customizationId, false)
}

// Applies the daemon's reply. A hide removes the exact canonical item from the
// pending and visible rows and selects the next row; a restore refreshes the
// search so the item can come back through ordinary ranking.
function applyHiddenChange(root, change, context) {
  const stableId = change ? change.stableId : ""
  if (change && change.isHidden) {
    removeRows(root, stableId)
    root.lastUndo = null
    root.lastHidden = {
      customizationId: hiddenIdPrefix + stableId,
      stableId: stableId,
      title: context && context.title ? context.title : stableId
    }
    if (root.part_hiddenItemsController.opened) root.requestHiddenItems(root.hiddenItemsQuery, 0)
    return
  }
  root.lastHidden = null
  root.requestSearch()
  if (context && context.fromSettings && root.part_hiddenItemsController.opened)
    root.requestHiddenItems(root.hiddenItemsQuery, 0)
}

function removeRows(root, stableId) {
  root.part_searchSession.removeResults(result => !!result && stableIdOf(result) === stableId)
  // Read the latest results from the navigation controller, not the model copy.
  const visible = []
  for (let index = 0; index < root.part_rows.count; index++) {
    const result = root.part_navigation.resultAt(index)
    if (result && stableIdOf(result) !== stableId) visible.push(result)
  }
  root.part_navigation.syncRows(root.part_navigation.buildRows(visible))
  root.part_navigation.refreshSelection()
}
