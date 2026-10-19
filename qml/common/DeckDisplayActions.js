.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function applyPanelTransparency(root, enabled, percent) {
  root.part_transparencyStore.set(enabled, percent)
}

function applyDisplaySizePreset(root, preset) {
  root.part_displayScaleStore.setPreset(preset)
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
  const action = result.actions.find(value => value.id === id)
  if (action) root.invokeAction(result, action, false)
}

// Enter runs the first listed action. Ctrl+Enter runs Reveal where a row
// offers it and otherwise the second action; Ctrl+Shift+Enter runs the next
// one after those two. Each entry names its key slot.
function shortcutActions(root, result) {
  if (!result) return []
  const listed = (result.actions || []).filter(value => value.hidden !== true)
  const later = listed.slice(1)
  const secondary = later.find(value => value.id === Ui.SearchShortcut.Reveal) || later[0]
  const tertiary = later.find(value => value !== secondary)
  const shortcuts = []
  if (secondary) shortcuts.push({ id: secondary.id, title: secondary.title, keyLabel: "Ctrl+↵",
    slot: Ui.ShortcutSlot.Secondary })
  if (tertiary) shortcuts.push({ id: tertiary.id, title: tertiary.title, keyLabel: "Ctrl+Shift+↵",
    slot: Ui.ShortcutSlot.Tertiary })
  const copyPath = listed.find(value => value.id === Ui.SearchShortcut.CopyPath)
  if (copyPath) shortcuts.push({ id: copyPath.id, title: copyPath.title, keyLabel: "Ctrl+Shift+C",
    slot: Ui.ShortcutSlot.Copy })
  if (result.canCustomize) shortcuts.push({ id: Ui.SearchShortcut.Favorite,
    title: result.isFavorite ? "Unbookmark" : "Bookmark", keyLabel: "Ctrl+D", slot: Ui.ShortcutSlot.Favorite })
  return shortcuts
}
