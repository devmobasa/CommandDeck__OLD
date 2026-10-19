.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

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

function applyDisplaySizePreset(root, preset) {
  root.part_displayScaleStore.setPreset(preset)
}

// Zoom steps through the fixed presets; Auto sits where Default does, and a
// zero step returns to Auto. Each end holds.
function nextDisplaySize(preset, direction) {
  if (direction === 0) return Ui.DisplaySize.Auto
  const steps = [Ui.DisplaySize.Small, Ui.DisplaySize.Default, Ui.DisplaySize.Large, Ui.DisplaySize.ExtraLarge]
  const current = Math.max(0, steps.indexOf(preset === Ui.DisplaySize.Auto ? Ui.DisplaySize.Default : preset))
  return steps[Math.max(0, Math.min(steps.length - 1, current + (direction > 0 ? 1 : -1)))]
}

function displaySizeLabel(preset) {
  switch (preset) {
    case Ui.DisplaySize.Small: return "Small"
    case Ui.DisplaySize.Default: return "Default"
    case Ui.DisplaySize.Large: return "Large"
    case Ui.DisplaySize.ExtraLarge: return "Extra large"
    default: return "Auto"
  }
}

function stepDisplaySize(root, direction) {
  const next = nextDisplaySize(root.displaySizePreset, direction)
  root.applyDisplaySizePreset(next)
  root.statusText = "Display size: " + displaySizeLabel(next)
}

function displaySizeMultiplierFor(root, preset) {
  switch (preset) {
    case Ui.DisplaySize.Small: return 0.9
    case Ui.DisplaySize.Large: return 1.15
    case Ui.DisplaySize.ExtraLarge: return 1.3
    case Ui.DisplaySize.Default: return 1.0
    case Ui.DisplaySize.Auto:
    default: return 1.0
  }
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
