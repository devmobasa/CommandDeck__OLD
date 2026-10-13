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

function shortcutActions(root, result) {
  if (!result) return []
  const actions = result.actions || []
  const shortcuts = []
  for (const id of [Ui.SearchShortcut.Reveal, Ui.SearchShortcut.CopyPath]) {
    const action = actions.find(value => value.id === id)
    if (action) shortcuts.push({ id: id, title: action.title,
      keyLabel: id === Ui.SearchShortcut.Reveal ? "Ctrl+↵" : "Ctrl+Shift+C" })
  }
  if (result.canCustomize) shortcuts.push({ id: Ui.SearchShortcut.Favorite,
    title: result.isFavorite ? "Unbookmark" : "Bookmark", keyLabel: "Ctrl+D" })
  return shortcuts
}
