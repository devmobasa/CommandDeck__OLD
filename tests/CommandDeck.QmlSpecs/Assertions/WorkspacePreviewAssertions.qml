import QtQuick
import "Protocol.js" as Protocol

QtObject {
  required property var host

  function run() {
    const deck = host.part_integratedDeck
    const pane = deck.testSurfaceWindow.part_detailPane
    const preferences = { workspacePreviewMode: "layout", reduceMotion: false, sources: [
      { sourceId: "windows-workspaces", enabledInSearch: true, enabledOnHome: true, position: 1 }
    ] }
    const oldPreferences = deck.part_personalization.acknowledged
    const oldResults = deck.part_searchSession.pendingResults
    const oldFilters = deck.filterDescriptors
    deck.filterDescriptors = oldFilters.concat([
      { id: "workspaces", label: "Workspaces", token: "workspace:", aliases: ["ws:"],
        providerId: Protocol.ProviderIds.Hyprland, sourceId: "windows-workspaces", kind: "workspace" }
    ])
    deck.part_personalization.resetPreferences(preferences)
    deck.daemonAvailable = true
    deck.reconciliationPending = false
    deck.closeAnswer()
    deck.part_navigation.clear()
    deck.part_searchSession.activeRequestId++
    deck.part_searchSession.pendingResults = [{ id: "workspace:-1337", kind: "workspace", title: "Workspace named",
      providerId: Protocol.ProviderIds.Hyprland, sourceId: "windows-workspaces", details: [], actions: [],
      metadata: { workspaceId: "-1337", workspaceName: "named", monitorName: "synthetic",
        workspaceKind: "ordinary" } }]
    deck.part_searchSession.commit()
    host.expect(pane.workspacePreviewEligible && pane.hasContent,
      "workspace fallback makes the real Home detail pane visible without a content cycle")

    for (const blocker of ["settingsOpen", "helpOpen", "agentOpen", "actionPanelOpen",
        "aliasDialogOpen", "confirmationOpen", "filterPaletteOpen"]) {
      deck[blocker] = true
      host.expect(!pane.workspacePreviewEligible, "workspace preview suspends behind " + blocker)
      deck[blocker] = false
      host.expect(pane.workspacePreviewEligible, "workspace preview resumes after " + blocker)
    }
    deck.part_navigation.herdrWorkspaceId = "synthetic"
    host.expect(!pane.workspacePreviewEligible, "Herdr view suspends workspace preview")
    deck.part_navigation.herdrWorkspaceId = ""
    deck.answerMode = "agent"
    host.expect(!pane.workspacePreviewEligible, "AI answer pane suspends workspace preview")
    deck.answerMode = ""
    deck.part_extensionController.opened = true
    host.expect(!pane.workspacePreviewEligible, "extension surface suspends workspace preview")
    deck.part_extensionController.opened = false
    deck.part_clipboardController.opened = true
    host.expect(!pane.workspacePreviewEligible, "clipboard surface suspends workspace preview")
    deck.part_clipboardController.opened = false
    deck.daemonAvailable = false
    host.expect(!pane.workspacePreviewEligible, "bridge loss suppresses workspace preview")
    deck.daemonAvailable = true

    const homeOnly = JSON.parse(JSON.stringify(preferences))
    homeOnly.sources[0].enabledInSearch = false
    deck.part_personalization.resetPreferences(homeOnly)
    host.expect(pane.workspacePreviewEligible, "Home follows its own workspace source preference")
    deck.testSearchInput.text = ""
    deck.testSearchInput.insert(0, "ws:")
    host.expect(deck.part_navigation.queryText === "" && deck.part_navigation.activeFilters.length === 1
        && deck.part_navigation.activeFilters[0].id === "workspaces" && !deck.queryIsEmpty,
      "the real launcher treats a converted workspace chip as search rather than Home")
    host.expect(!pane.workspacePreviewEligible, "workspace search honors its disabled source")
    const searchOnly = JSON.parse(JSON.stringify(preferences))
    searchOnly.sources[0].enabledOnHome = false
    deck.part_personalization.resetPreferences(searchOnly)
    host.expect(pane.workspacePreviewEligible, "filter-only search uses Search when Home is disabled")
    deck.part_personalization.resetPreferences(preferences)
    host.expect(pane.workspacePreviewEligible, "mixed search workspace selection is eligible")
    deck.part_navigation.activeFilters = [{ id: "workspaces", excluded: true }]
    host.expect(!pane.workspacePreviewEligible, "workspace exclusion suppresses even the previous search row")
    deck.part_navigation.activeFilters = []
    const disabled = JSON.parse(JSON.stringify(preferences))
    disabled.workspacePreviewMode = "off"
    deck.part_personalization.submitPreferences(disabled)
    host.expect(!pane.workspacePreviewEligible, "pending Off immediately suppresses workspace work")
    deck.part_personalization.rejectPreferences("synthetic rejection")
    host.expect(pane.workspacePreviewEligible, "rejected Off restores the acknowledged mode")
    deck.part_personalization.resetPreferences(disabled)
    deck.part_personalization.submitPreferences(preferences)
    host.expect(!pane.workspacePreviewEligible, "pending enable waits for daemon acknowledgement")
    deck.part_personalization.abort("")
    const snapshots = JSON.parse(JSON.stringify(preferences))
    snapshots.workspacePreviewMode = "snapshots"
    deck.part_personalization.resetPreferences(snapshots)
    host.expect(pane.workspacePreviewMode === "snapshots", "acknowledged snapshots reach the preview")
    deck.part_personalization.submitPreferences(preferences)
    host.expect(pane.workspacePreviewMode === "layout", "pending layout immediately disables pixel capture")
    deck.part_personalization.abort("")
    deck.part_personalization.resetPreferences(preferences)
    deck.part_personalization.submitPreferences(snapshots)
    host.expect(pane.workspacePreviewMode === "layout", "pending snapshots cannot enable pixel capture")
    deck.part_personalization.abort("")

    const workspaceRows = [
      { id: "workspace:10", kind: "workspace", title: "Workspace 10", score: 0.1,
        providerId: Protocol.ProviderIds.Hyprland, sourceId: "windows-workspaces",
        metadata: { workspaceId: "10", workspaceName: "10" } },
      { id: "workspace:2", kind: "workspace", title: "Workspace 2", score: 0.1,
        providerId: Protocol.ProviderIds.Hyprland, sourceId: "windows-workspaces",
        metadata: { workspaceId: "2", workspaceName: "2" } },
      { id: "workspace:1", kind: "workspace", title: "Workspace 1", score: 0.2,
        providerId: Protocol.ProviderIds.Hyprland, sourceId: "windows-workspaces",
        metadata: { workspaceId: "1", workspaceName: "1" } }
    ]
    deck.queryText = ""
    deck.openWorkspaceMruIds = ["10"]
    deck.part_searchSession.pendingResults = workspaceRows
    deck.part_searchSession.commit()
    host.expect(deck.part_rows.get(1).value.id === "workspace:10"
        && deck.part_rows.get(2).value.id === "workspace:1"
        && deck.part_rows.get(3).value.id === "workspace:2",
      "the integrated launcher orders empty workspace searches by snapshotted recent use then score")
    deck.queryText = "typed"
    deck.part_searchSession.pendingResults = workspaceRows
    deck.part_searchSession.commit()
    host.expect(deck.part_rows.get(1).value.id === "workspace:1",
      "typed workspace searches retain daemon relevance instead of live MRU")

    deck.part_personalization.resetPreferences(oldPreferences)
    deck.part_navigation.clear()
    deck.openWorkspaceMruIds = []
    deck.part_searchSession.pendingResults = oldResults
    deck.part_searchSession.commit()
    deck.filterDescriptors = oldFilters
  }
}
