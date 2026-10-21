.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol
.import "DisplaySizing.js" as Sizing

function reconcileScreens(root) {
  if (!root.opened || root.openedScreenName.length === 0
      || root.compositor.screens.some(screen => screen.name === root.openedScreenName)) return

  // Retain the draft and navigation state for a reopen; no invisible layer may
  // keep exclusive keyboard focus after its output disappears.
  root.suspendedForScreen = true
  root.opened = false
  if (root.shell && typeof root.shell.hide === "function") root.shell.hide(root.integrationIds.Plugin)
}

function applyPanelTransparency(root, enabled, percent) {
  root.part_transparencyStore.set(enabled, percent)
}

function applyCompactLayout(root, enabled) {
  root.part_layoutStore.setCompact(enabled)
}

// A hidden pane loads no file preview; showing it again loads the selected one.
function applyPreviewPane(root, visible) {
  root.part_layoutStore.setPreview(visible)
  if (visible) root.requestPreview(root.currentResult)
  else root.cancelPreview()
}

function togglePreviewPane(root) {
  applyPreviewPane(root, !root.previewPaneVisible)
  root.statusText = root.previewPaneVisible ? "Preview pane shown" : "Preview pane hidden"
}

function applyDisplaySizePercent(root, percent) {
  root.part_displayScaleStore.setPercent(percent)
}

// Zoom holds at each end; zero resets the percentage within the current profile.
function nextDisplaySizePercent(percent, direction) {
  if (direction === 0) return Ui.DisplaySizePercent.Default
  const next = percent + (direction > 0 ? Ui.DisplaySizePercent.ZoomStep : -Ui.DisplaySizePercent.ZoomStep)
  return Math.max(Ui.DisplaySizePercent.Minimum, Math.min(Ui.DisplaySizePercent.Maximum, next))
}

function displaySizeLabel(percent, legacy) {
  return String(percent) + "%" + (percent === Ui.DisplaySizePercent.Default && !legacy ? " (Recommended)" : "")
}

function stepDisplaySize(root, direction) {
  const next = nextDisplaySizePercent(root.displaySizePercent, direction)
  root.applyDisplaySizePercent(next)
  const window = root.presentingWindow
  root.statusText = "Display size: " + displaySizeLabel(next, root.part_displayScaleStore.legacy)
    + (window ? Sizing.fitDescription(window.launcherSizing, window.maximumFittingPercent) : "")
}

function invokeShortcut(root, id) {
  if (!root.daemonAvailable || root.reconciliationPending) return
  const result = root.part_navigation.selectedResult()
  if (!root.shortcutActions(result).some(action => action.id === id)) return
  if (id === Ui.SearchShortcut.Favorite) {
    if (!root.personalizationBusy) root.setFavorite(result, !result.isFavorite)
    return
  }
  if (id === Ui.ResultAction.Alias) {
    root.aliasTarget = result
    root.aliasDialogError = ""
    root.aliasDialogOpen = true
    return
  }
  if (id === Ui.ResultAction.Hide) {
    root.setHidden(result)
    return
  }
  const action = result.actions.find(value => value.id === id)
  if (action) root.invokeAction(result, action, false)
}

// Enter runs the first listed action. Ctrl+Enter runs Reveal where a row
// offers it and otherwise the second action; Ctrl+Shift+Enter runs the next
// one after those two. The other slots keep one chord for the same meaning on
// every result: Ctrl+Shift+C copies the path, or else the first Copy action.
// Each entry names its key slot.
function shortcutActions(root, result) {
  if (!result) return []
  const listed = (result.actions || []).filter(value => value.hidden !== true)
  // Reset Ranking keeps its own chord and is never a second or third action.
  const later = listed.slice(1).filter(value => value.kind !== Protocol.ActionKinds.ResetRanking)
  const secondary = later.find(value => value.id === Ui.SearchShortcut.Reveal) || later[0]
  const tertiary = later.find(value => value !== secondary)
  const shortcuts = []
  if (secondary) shortcuts.push({ id: secondary.id, title: secondary.title, keyLabel: "Ctrl+↵",
    slot: Ui.ShortcutSlot.Secondary })
  if (tertiary) shortcuts.push({ id: tertiary.id, title: tertiary.title, keyLabel: "Ctrl+Shift+↵",
    slot: Ui.ShortcutSlot.Tertiary })
  const copy = listed.find(value => value.id === Ui.SearchShortcut.CopyPath)
    || listed.find(value => value.kind === Protocol.ActionKinds.CopyText)
  if (copy) shortcuts.push({ id: copy.id, title: copy.title, keyLabel: "Ctrl+Shift+C", slot: Ui.ShortcutSlot.Copy })
  const reset = listed.find(value => value.kind === Protocol.ActionKinds.ResetRanking)
  if (reset) shortcuts.push({ id: reset.id, title: reset.title, keyLabel: "Alt+Shift+R",
    slot: Ui.ShortcutSlot.ResetRanking })
  if (!result.canCustomize) return shortcuts

  const aliased = result.aliases && result.aliases.length > 0
  shortcuts.push({ id: Ui.SearchShortcut.Favorite, title: result.isFavorite ? "Unbookmark" : "Bookmark",
    keyLabel: "Ctrl+D", slot: Ui.ShortcutSlot.Favorite })
  shortcuts.push({ id: Ui.ResultAction.Alias, title: aliased ? "Edit Alias" : "Set Alias",
    keyLabel: "Alt+Shift+A", slot: Ui.ShortcutSlot.Alias })
  shortcuts.push({ id: Ui.ResultAction.Hide, title: "Don't show this", keyLabel: "Alt+Shift+D",
    slot: Ui.ShortcutSlot.Hide })
  return shortcuts
}
