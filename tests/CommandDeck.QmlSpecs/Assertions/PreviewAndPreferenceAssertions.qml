import QtQuick
import "Protocol.js" as Protocol
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(context) {
    host.expect(!host.part_unsupportedPreview.supported,
      "window preview fails closed without an advertised compositor capability")
    host.expect(host.part_preview.normalizeAddress("0xAbC123") === "abc123",
      "window preview normalizes prefixed mixed-case addresses")
    host.expect(host.part_preview.normalizeAddress("ABC123") === "abc123",
      "window preview normalizes bare mixed-case addresses")
    host.expect(host.part_preview.findToplevel("0xabc123", []) === null,
      "window preview tolerates an empty toplevel list")
    const match = { address: "ABC123", marker: "matched" }
    host.expect(host.part_preview.findToplevel("0xabc123", [match]) === match,
      "window preview matches normalized toplevel addresses")
    host.part_preview.opened = true
    host.part_preview.toplevels = [{ address: "ABC123", wayland: null }]
    host.part_preview.settledAddress = "abc123"
    host.part_preview.selected = true
    host.expect(host.part_preview.captureLoaded && host.part_preview.showCaptureArea,
      "window preview creates capture only for the active matched result")
    host.part_preview.opened = false
    host.expect(!host.part_preview.captureLoaded,
      "window preview releases capture when the overlay closes")
    host.expect(host.part_resultIcon.candidates(host.part_resultIcon.iconName, host.part_resultIcon.kind)[0]
        === "resolved-window-icon",
      "window icons honor the backend-resolved icon first")
    host.expect(host.part_filePreview.contentText.indexOf("<script>") >= 0,
      "file preview preserves content as literal text")
    host.expect(host.part_filePreview.language === "markdown" && host.part_filePreview.truncated,
      "file preview exposes language and truncation state")
    host.part_actions.openFor({
      title: "Brave",
      isFavorite: false,
      aliases: [],
      canCustomize: true,
      actions: [
        { id: "launch", title: "Launch", kind: "launch-desktop" },
        { id: "close", title: "Close", kind: "close", isDestructive: true }
      ]
    })
    host.expect(host.part_actions.visibleActionCount === 5
        && host.part_actions.filteredActions.some(action => action.kind === "hide-result"
          && action.title === "Don't show this"),
      "action panel combines executable, customization, and hide actions")
    host.part_actions.move(1)
    host.expect(host.part_actions.selectedIndex === 1,
      "action panel keyboard navigation moves the active action")
    host.part_actions.invokeOrdinal(1)
    host.expect(host.part_actions.chosenActionId === "launch",
      "action panel ordinal shortcuts invoke the visible action")
    host.expect(host.part_actions.accessibilityDescription({
        isDestructive: true,
        requiresConfirmation: true
      }).indexOf("Destructive") >= 0
        && host.part_actions.accessibilityDescription({
          isDestructive: true,
          requiresConfirmation: true
        }).indexOf("confirmation") >= 0,
      "action accessibility announces destructive confirmation")
    host.part_actions.filterText = "bookmark"
    host.expect(host.part_actions.visibleActionCount === 1
        && host.part_actions.filteredActions[0].kind === "favorite-toggle",
      "action panel filters common actions by title")
    host.part_actions.filterText = "missing action"
    host.expect(host.part_actions.visibleActionCount === 0,
      "action panel exposes an empty filtered state")
    host.part_actions.filterText = ""
    host.part_actions.selectedIndex = 1
    host.part_actions.filterText = "close"
    host.part_actions.filterText = ""
    host.expect(host.part_actions.filteredActions[host.part_actions.selectedIndex].id === "close",
      "action filtering restores the previously selected action")
    host.part_actions.openFor({
      id: "app:browser",
      title: "Browser",
      isFavorite: true,
      aliases: [],
      canCustomize: true,
      actions: []
    })
    host.part_actions.updateCustomization({
      stableId: "app:browser",
      isFavorite: true,
      aliases: ["browser"]
    })
    host.expect(host.part_actions.filteredActions.some(action =>
        action.kind === "alias-edit" && action.title.indexOf("Edit Alias") >= 0),
      "action panel reflects a newly saved first alias")
    host.part_aliasDialog.reservedValues = ["app", "home", "settings"]
    host.expect(host.part_aliasDialog.locallyValid("brave-1")
        && !host.part_aliasDialog.locallyValid("bad alias")
        && !host.part_aliasDialog.locallyValid("app")
        && !host.part_aliasDialog.locallyValid("HOME")
        && !host.part_aliasDialog.locallyValid("settings"),
      "alias dialog applies deterministic local validation")
    host.expect(host.part_aliasDialog.validationError("app").indexOf("reserved") >= 0,
      "alias dialog explains reserved launcher syntax")
    const renderedAi = host.part_aiMarkdown.render(
      "**bold** `code` [link](https://example.test) <img src='file:///secret'>\n```sh\nrm -rf /\n```")
    host.expect(renderedAi.indexOf("<b>bold</b>") >= 0
        && renderedAi.indexOf("<code>code</code>") >= 0
        && renderedAi.indexOf("href=") < 0
        && renderedAi.indexOf("<img") < 0
        && renderedAi.indexOf("&lt;img") >= 0
        && renderedAi.indexOf("<pre>") >= 0,
      "AI Markdown renderer supports a bounded inert subset")
    host.part_aliasDialog.result = { title: "Brave", aliases: ["b"] }
    host.part_aliasDialog.opened = true
    host.expect(host.part_aliasDialog.text === "b",
      "alias dialog prepares the primary alias when opened")
    host.expect(host.part_aliasDialog.focusTrapConfigured,
      "alias dialog traps forward and backward focus")
    host.part_aliasDialog.opened = false
    host.expect(host.part_accessibleResult.accessibilityName.indexOf("3") >= 0
        && host.part_accessibleResult.accessibilityName.indexOf("Applications") >= 0,
      "result accessibility includes ordinal and section")
    host.expect(host.part_settings.sourceLabel("omarchy-system") === "System Menu",
      "settings maps stable source IDs to labels")
    host.expect(host.part_settings.sourceLabel("not-a-source") === "not-a-source"
        && host.part_settings.sourceLabel(undefined) === "Unknown source",
      "settings never labels a source with undefined")
    host.expect(host.part_settings.renderedSourceCount === 3,
      "settings renders one row per configured source")
    host.expect(host.part_settings.renderedSourceLabelAt(0) === "Applications"
        && host.part_settings.renderedSourceLabelAt(1) === "Files"
        && host.part_settings.renderedSourceLabelAt(2) === "System Menu",
      "settings source rows carry distinct names")
    host.expect(host.part_settings.renderedSourceIconAt(0).length > 0
        && host.part_settings.renderedSourceIconAt(1).length > 0
        && host.part_settings.renderedSourceIconAt(2).length > 0,
      "settings source rows resolve an icon")
    host.expect(host.part_settings.part_sourcesCard.width > 400,
      "settings source card receives the panel width")
    host.expect(host.part_settings.shortcutStatus({
        status: "unavailable",
        chords: [],
        detail: "hyprctl was not found"
      }).indexOf("hyprctl was not found") >= 0,
      "settings renders missing shortcut dependencies visibly")
    host.expect(host.part_settings.removeUnavailable("customization:missing")
        && host.part_settings.removedCustomizationId === "customization:missing",
      "settings dispatches unavailable customization removal")
    host.expect(host.part_settings.renderedFilterAliasCount === 1
        && host.part_settings.renderedFilterAliasAt(0).Accessible.name.indexOf("docs") >= 0,
      "settings renders persisted filter aliases accessibly")
    host.expect(host.part_settings.renderedSavedSearchCount === 1
        && host.part_settings.renderedSavedSearchAt(0).Accessible.name.indexOf("Docs") >= 0,
      "settings renders saved searches and shortcuts accessibly")
    const savedSearches = host.part_settings.savedSearches
    host.part_settings.savedSearches = savedSearches.concat([{ id: "saved-search:old", name: "Old",
      reservedShortcut: "CTRL+D", showOnHome: false, position: 2,
      expression: { includeFilterIds: [], excludeFilterIds: [], clauses: [] } }])
    host.expect(host.part_settings.renderedSavedSearchAt(1).Accessible.name
        === "Old, CTRL+D is reserved by CommandDeck; edit to choose another",
      "settings names a saved shortcut CommandDeck has since reserved")
    host.part_settings.savedSearches = savedSearches
    host.part_settings.synchronizeAiChoices()
    host.expect(host.part_settings.aiProviderControl.currentIndex === 1
        && host.part_settings.aiModelControl.currentIndex === 1,
      "settings reconciles persisted AI provider and model selection")
    host.expect(host.part_settings.renderedAiTemplateCount === 1,
      "settings renders persisted AI prompt templates")
    host.part_settings.editAiTemplate(null)
    let emojiName = ""
    for (let emojiIndex = 0; emojiIndex < 64; emojiIndex++) emojiName += "\uD83D\uDE42"
    host.part_settings.aiTemplateNameControl.text = emojiName
    host.expect(host.part_settings.aiTemplateNameControl.text === emojiName,
      "settings preserves 64 Unicode template-name characters for daemon validation")
    host.part_settings.aiTemplateIdControl.text = "custom-safe"
    host.part_settings.aiTemplateNameControl.text = "Custom safe"
    host.part_settings.aiTemplateUserControl.text = "Review {{input}}"
    host.expect(host.part_settings.saveAiTemplate()
        && host.part_settings.savedAiTemplate.id === "custom-safe"
        && host.part_settings.savedAiTemplate.userTemplate === "Review {{input}}",
      "settings validates and dispatches AI template edits")
    host.expect(host.part_settings.filterAliasTokenIsBuiltIn("APP:")
        && host.part_settings.filterAliasTokenIsBuiltIn("text")
        && !host.part_settings.filterAliasTokenIsBuiltIn("project"),
      "settings rejects built-in filter tokens before mutation")
    host.part_settings.filterAliasRemoveRequested("docs")
    host.expect(host.part_settings.removedFilterAlias === "docs",
      "settings dispatches filter alias removal")
    const ordinarySnapshot = host.part_settings.snapshot
    host.part_settings.snapshot = {
      preferences: ordinarySnapshot.preferences,
      shortcuts: [],
      customizations: [
        { stableId: "file:one", title: "One", aliases: [], isAvailable: true },
        { stableId: "file:two", title: "Two", aliases: [], isAvailable: true }
      ],
      totalCustomizationCount: 257,
      customizationsTruncated: true
    }
    host.expect(host.part_settings.customizationOverflow
        && !host.part_settings.favoritesReorderAvailable
        && host.part_settings.customizationOverflowText.indexOf("257") >= 0
        && host.part_settings.customizationOverflowText.indexOf("preferences export") >= 0,
      "settings explains legacy customization overflow and disables reorder")
    host.part_settings.moveFavorite(0, 1)
    host.expect(host.part_settings.favoriteReorderDispatches === 0,
      "legacy overflow cannot dispatch an incomplete favorite permutation")
    host.part_settings.snapshot = ordinarySnapshot
    host.expect(host.part_helpSheet.renderedSectionCount === 5
        && host.part_helpSheet.renderedFilterCount === 3,
      "help sheet lists every shortcut section and filter")
    host.expect(host.part_helpSheet.renderedFilterTokensAt(1) === "file:  text:  files:"
        && host.part_helpSheet.renderedFilterTokensAt(2)
          === "workspace:  ws:  workspaces:",
      "help sheet shows filter tokens with their aliases")
    host.expect(host.part_helpSheet.shortcutListed("←")
        && host.part_helpSheet.shortcutListed("Ctrl+/")
        && !host.part_helpSheet.shortcutListed("app:"),
      "help sheet lists bound chords without duplicating daemon filter tokens")
    host.expect(host.part_integratedDeck.helpOpen === false
        && host.part_integratedDeck.navigationController.handleHelpShortcut(Qt.Key_F1, Qt.NoModifier)
        && host.part_integratedDeck.helpOpen,
      "F1 opens the help sheet")
    host.expect(host.part_integratedDeck.navigationController.handleHelpShortcut(
        Qt.Key_F1, Qt.NoModifier)
        && host.part_integratedDeck.helpOpen === false,
      "F1 closes the help sheet again")
    host.expect(host.part_integratedDeck.navigationController.handleHelpShortcut(
          Qt.Key_Slash, Qt.ControlModifier)
        && host.part_integratedDeck.helpOpen,
      "Ctrl+/ opens the help sheet")
    host.part_integratedDeck.openSettings()
    host.expect(host.part_integratedDeck.helpOpen === false,
      "opening settings closes the help sheet")
    host.part_integratedDeck.closeSettings()
    host.expect(!host.part_integratedDeck.navigationController.handleHelpShortcut(
        Qt.Key_Slash, Qt.NoModifier)
        && !host.part_integratedDeck.navigationController.handleHelpShortcut(
          Qt.Key_A, Qt.ControlModifier),
      "help shortcut ignores a bare slash and unrelated chords")
    host.expect(host.part_confirmation.focusTrapConfigured,
      "confirmation dialog traps forward and backward focus")
    host.part_filterChip.activate()
    host.expect(host.part_filterChip.removals === 1
        && host.part_filterChip.accessibilityName === "Files filter, remove",
      "filter chips share an accessible removal action")
    host.part_announcements.publish("1 result")
    host.part_announcements.publish("2 results")
    host.part_announcements.flush()
    host.expect(host.part_announcements.announcement === "2 results",
      "accessibility announcements coalesce to the latest status")
    const baselinePreferences = {
      sources: [
        { sourceId: "applications", enabledInSearch: true, enabledOnHome: true, position: 1 },
        { sourceId: "files", enabledInSearch: true, enabledOnHome: true, position: 2 }
      ],
      reduceMotion: false,
      webFallbackQuicklinkId: null
    }
    host.part_preferenceQueue.reset(baselinePreferences)
    const firstEdit = JSON.parse(JSON.stringify(host.part_preferenceQueue.draft))
    firstEdit.sources[0].enabledInSearch = false
    host.part_preferenceQueue.submit(firstEdit)
    const secondEdit = JSON.parse(JSON.stringify(host.part_preferenceQueue.draft))
    secondEdit.sources[1].enabledOnHome = false
    host.part_preferenceQueue.submit(secondEdit)
    host.expect(host.part_preferenceQueue.dispatched.length === 1
        && host.part_preferenceQueue.dispatched[0].sources[0].enabledInSearch === false,
      "preference updates serialize the first rapid edit")
    host.part_preferenceQueue.accept(firstEdit)
    host.expect(host.part_preferenceQueue.dispatched.length === 2
        && host.part_preferenceQueue.dispatched[1].sources[0].enabledInSearch === false
        && host.part_preferenceQueue.dispatched[1].sources[1].enabledOnHome === false,
      "preference updates coalesce rapid edits without losing prior changes")
    host.part_preferenceQueue.accept(secondEdit)
    host.part_preferenceQueue.reset(baselinePreferences)
    const rejectedEdit = JSON.parse(JSON.stringify(host.part_preferenceQueue.draft))
    rejectedEdit.reduceMotion = true
    host.part_preferenceQueue.submit(rejectedEdit)
    host.part_preferenceQueue.reject("write failed")
    host.expect(host.part_preferenceQueue.draft.reduceMotion === false
        && host.part_preferenceQueue.rollbacks === 1,
      "preference errors restore the acknowledged snapshot")
    host.part_mutationQueue.submit(
      { requestId: 41, operation: Protocol.Operations.AliasSet, alias: "browser" },
      { kind: "alias" })
    host.part_mutationQueue.submit(
      { requestId: 42, operation: Protocol.Operations.FavoriteReorder, stableIds: ["a", "b"] },
      { kind: "reorder" })
    host.part_mutationQueue.submit(
      { requestId: 43, operation: Protocol.Operations.CustomizationsReset, confirmed: true },
      { kind: "reset" })
    host.expect(host.part_mutationQueue.dispatched.length === 1
        && host.part_mutationQueue.dispatched[0].request.requestId === 41,
      "personalization mutations dispatch one request at a time")
    host.part_mutationQueue.settle(42)
    host.expect(host.part_mutationQueue.dispatched.length === 1,
      "out-of-order mutation responses cannot advance the queue")
    host.part_mutationQueue.settle(41)
    host.part_mutationQueue.settle(42)
    host.expect(host.part_mutationQueue.dispatched.length === 3
        && host.part_mutationQueue.dispatched[2].request.requestId === 43,
      "alias reorder and reset mutations preserve submission order")
    host.part_mutationQueue.settle(43)
    host.expect(!host.part_mutationQueue.busy,
      "personalization mutation queue reaches a terminal idle state")
    host.part_mutationQueue.submit(
      { requestId: 44, operation: Protocol.Operations.AliasSet }, { kind: "alias" })
    host.part_mutationQueue.submit(
      { requestId: 45, operation: Protocol.Operations.FavoriteSet }, { kind: "favorite" })
    host.part_mutationQueue.abort("bridge disconnected")
    host.expect(!host.part_mutationQueue.busy && host.part_mutationQueue.active === null
        && host.part_mutationQueue.pending.length === 0 && host.part_mutationQueue.aborts === 1
        && host.part_mutationQueue.abortedEntries.length === 2
        && host.part_mutationQueue.abortedEntries[0].request.requestId === 44
        && host.part_mutationQueue.abortedEntries[1].request.requestId === 45,
      "personalization queue returns active and unsent work for targeted feedback")
    const historyRequestStart = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.openQueryHistory()
    host.expect(host.part_integratedDeck.queryHistoryOpen
        && host.part_integratedDeck.sentRequests.length === historyRequestStart + 1
        && host.part_integratedDeck.sentRequests[historyRequestStart].operation === Protocol.Operations.QueryHistoryGet,
      "integrated query history opening requests an authoritative snapshot")
    const historyRequest = host.part_integratedDeck.sentRequests[historyRequestStart]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.QueryHistoryResult,
      requestId: historyRequest.requestId,
      queryHistory: {
        enabled: true,
        retentionDays: 90,
        maximumEntries: 1000,
        totalEntries: 1,
        entries: [{ id: 12, query: "report", stableId: "file:report", actionId: "open" }]
      }
    })
    host.expect(!host.part_integratedDeck.queryHistoryLoading
        && host.part_integratedDeck.queryHistorySnapshot.totalEntries === 1,
      "integrated query history accepts its correlated acknowledgement")
    host.part_integratedDeck.deleteQueryHistory(12)
    const failedHistoryRequest = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Error,
      requestId: failedHistoryRequest.requestId,
      error: "history write failed"
    })
    host.expect(host.part_integratedDeck.queryHistorySnapshot.totalEntries === 1
        && host.part_integratedDeck.queryHistoryError.indexOf("failed") >= 0,
      "failed history mutation preserves the acknowledged snapshot and reports rollback")
    host.part_integratedDeck.closeQueryHistory()
    host.expect(host.part_motionTheme.durationFast === 0
        && host.part_motionTheme.durationNormal === 0
        && host.part_motionTheme.highlightMoveDuration === 0,
      "reduced motion disables all new animation durations")
    host.expect(host.part_motionTheme.displayScale === 1
        && host.part_motionTheme.scale === 1
        && host.part_motionTheme.fontBody === 12
        && host.part_motionTheme.rowHeight === 46,
      "the fallback theme defaults to a 1x display scale and base metrics")
    host.part_motionTheme.displayScale = 1.5
    host.expect(Math.abs(host.part_motionTheme.scale - 1.5) < 0.001
        && Math.abs(host.part_motionTheme.fontDisplayScale - 1.25) < 0.001
        && host.part_motionTheme.fontBody === 15
        && host.part_motionTheme.rowHeight === 69
        && host.part_motionTheme.paletteWidth === 1350,
      "the display scale grows layout fully and fonts gently (1.5x boxes, 1.25x fonts)")
    host.part_motionTheme.densityScale = 0.75
    host.expect(host.part_motionTheme.fontBody === 11
        && host.part_motionTheme.rowHeight === 52
        && host.part_motionTheme.paletteWidth === 1013
        && host.part_motionTheme.inputHeight === 52,
      "compact density reduces fonts, controls, and panel dimensions together")

    Object.assign(context, { match, renderedAi, emojiName, ordinarySnapshot, baselinePreferences, firstEdit, secondEdit, rejectedEdit, historyRequestStart, historyRequest, failedHistoryRequest })
  }
}
