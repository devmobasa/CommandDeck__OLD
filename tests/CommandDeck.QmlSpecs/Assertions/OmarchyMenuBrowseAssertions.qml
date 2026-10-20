import QtQuick
import "Protocol.js" as Protocol

// The integrated launcher sends an Omarchy submenu as the search's exact
// scope, keeps the views it came from, and leaves the scope on Back to the
// first view, Shift+Escape, and closing.
QtObject {
  required property var host

  function submenu(id, path) {
    const keys = Protocol.ResultMetadataKeys
    const values = {}
    values[keys.OmarchyMenu] = id
    values[keys.OmarchyMenuPath] = path
    return {
      id: "omarchy:" + id, kind: Protocol.ResultKinds.Omarchy, sourceId: Protocol.SourceIds.OmarchyControl,
      title: path.split(" › ").pop(), metadata: values,
      actions: [{ id: "open-submenu", title: "Open Submenu", kind: Protocol.ActionKinds.BrowseOmarchyMenu, arguments: values }]
    }
  }

  function run() {
    const deck = host.part_integratedDeck
    const last = () => deck.sentRequests[deck.sentRequests.length - 1]
    const theme = submenu("style.theme", "Style › Theme")
    const variants = submenu("style.theme.variants", "Style › Theme › Variants")
    const keepSeconds = deck.keepSearchSeconds
    deck.keepSearchSeconds = 60
    deck.open("{}")
    deck.reconciliationPending = false
    deck.daemonAvailable = true
    deck.queryText = "theme"
    deck.actionPanelOpen = true

    deck.invokeAction(theme, theme.actions[0], false)
    let request = last()
    host.expect(request.operation === Protocol.Operations.Search && request.omarchyMenuId === "style.theme"
        && request.query === "" && request.limit === 200 && !request.herdrWorkspaceId,
      "opening an Omarchy submenu searches its exact menu scope with an empty query")
    host.expect(deck.omarchyMenuOpen && !deck.actionPanelOpen && deck.part_navigation.omarchyMenuPath === "Style › Theme",
      "the submenu view opens with its path and closes the action panel")

    deck.queryText = "va"
    deck.invokeAction(variants, variants.actions[0], false)
    request = last()
    host.expect(request.omarchyMenuId === "style.theme.variants" && request.query === ""
        && deck.part_navigation.omarchyParents.length === 2, "a nested submenu pushes another view")

    deck.part_navigation.dismiss()
    request = last()
    host.expect(request.omarchyMenuId === "style.theme" && request.query === "va"
        && deck.part_navigation.restoreSelectionId === variants.id,
      "Escape on an empty submenu returns to the parent menu with its query and selection")
    deck.part_navigation.dismiss()
    host.expect(last().omarchyMenuId === "style.theme" && last().query === "",
      "Escape clears the submenu's text before going back")
    deck.part_navigation.dismiss()
    request = last()
    host.expect(!request.omarchyMenuId && request.query === "theme" && !deck.omarchyMenuOpen && request.limit === 50,
      "going back from the first submenu restores the search it was opened from")

    deck.invokeAction(theme, theme.actions[0], false)
    deck.backToRoot()
    request = last()
    host.expect(!request.omarchyMenuId && request.query === "" && !deck.omarchyMenuOpen
        && deck.part_navigation.omarchyParents.length === 0, "Shift+Escape's Home leaves every submenu")

    deck.invokeAction(theme, theme.actions[0], false)
    deck.queryText = "dark"
    deck.close()
    host.expect(deck.lastSearch === null, "a closed submenu search is not kept for reopening")
    deck.open("{}")
    host.expect(!last().omarchyMenuId && !deck.omarchyMenuOpen, "reopening starts outside any submenu")
    deck.close()
    deck.keepSearchSeconds = keepSeconds
  }
}
