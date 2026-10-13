import QtQuick
import "Protocol.js" as Protocol
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(context) {
    let customizable = context.customizable
    host.part_integratedDeck.preferencesSnapshot = {
      preferences: {
        sources: [],
        reduceMotion: false,
        webFallbackQuicklinkId: null
      },
      shortcuts: [{
        label: "Open CommandDeck",
        status: "unavailable",
        chords: [],
        detail: "hyprctl was not found"
      }],
      customizations: []
    }
    host.part_integratedDeck.enqueuePersonalization("favorite.reorder", {
      stableIds: ["app:browser"]
    }, { kind: "reorder" })
    const reorderRequest = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.PreferencesResult,
      requestId: reorderRequest.requestId,
      preferences: {
        preferences: {
          sources: [],
          reduceMotion: false,
          webFallbackQuicklinkId: null
        },
        shortcuts: [],
        customizations: []
      }
    })
    host.expect(host.part_integratedDeck.preferencesSnapshot.shortcuts.length === 1
        && host.part_integratedDeck.preferencesSnapshot.shortcuts[0].detail.indexOf("hyprctl") >= 0,
      "reorder responses preserve inspected shortcut diagnostics")
    host.part_integratedDeck.enqueuePersonalization("filter-alias.set", {
      alias: "docs",
      targetFilterId: "files"
    }, { kind: "filter-alias", alias: "docs" })
    const filterAliasRequest = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.FilterAliasResult,
      requestId: filterAliasRequest.requestId,
      filterAliases: {
        aliases: [{ alias: "docs", filterId: "files" }],
        filters: [{
          id: "files", label: "Files", token: "file:", aliases: ["docs:"]
        }],
        message: "Filter alias saved"
      }
    })
    host.expect(host.part_integratedDeck.filterAliases.length === 1
        && host.part_integratedDeck.filterDescriptors[0].aliases[0] === "docs:"
        && !host.part_integratedDeck.personalizationBusy,
      "integrated filter alias acknowledgement updates bootstrap state")
    host.part_integratedDeck.enqueuePersonalization("filter-alias.set", {
      alias: "app",
      targetFilterId: "files"
    }, { kind: "filter-alias", alias: "app" })
    const rejectedFilterAlias = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Error,
      requestId: rejectedFilterAlias.requestId,
      error: "alias conflicts with built-in syntax"
    })
    host.expect(host.part_integratedDeck.filterAliases.length === 1
        && host.part_integratedDeck.filterAliases[0].alias === "docs"
        && host.part_integratedDeck.filterAliasError.indexOf("conflicts") >= 0,
      "failed filter alias mutation preserves acknowledged state")
    const savedSearch = {
      id: "saved-search:docs",
      name: "Docs",
      expression: { includeFilterIds: ["files"], excludeFilterIds: [], clauses: [] },
      shortcut: null,
      showOnHome: true,
      position: 1
    }
    host.part_integratedDeck.enqueuePersonalization("saved-search.save", {
      savedSearch: savedSearch
    }, { kind: "saved-search", id: savedSearch.id })
    const savedSearchRequest = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.SavedSearchResult,
      requestId: savedSearchRequest.requestId,
      savedSearches: { searches: [savedSearch], message: "Saved search stored" }
    })
    host.expect(host.part_integratedDeck.savedSearches.length === 1
        && host.part_integratedDeck.savedSearches[0].id === savedSearch.id
        && !host.part_integratedDeck.personalizationBusy,
      "integrated saved search acknowledgement updates launcher state")
    host.part_integratedDeck.enqueuePersonalization("saved-search.save", {
      savedSearch: {
        id: "", name: "Conflict", expression: savedSearch.expression,
        shortcut: "CTRL+N", showOnHome: false, position: 0
      }
    }, { kind: "saved-search", id: "" })
    const rejectedSavedSearch = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Error,
      requestId: rejectedSavedSearch.requestId,
      error: "shortcut conflicts with CommandDeck"
    })
    host.expect(host.part_integratedDeck.savedSearches.length === 1
        && host.part_integratedDeck.savedSearchError.indexOf("conflicts") >= 0,
      "failed saved search mutation preserves acknowledged state")
    host.part_integratedDeck.enqueuePersonalization("customizations.reset", {
      confirmed: true
    }, { kind: "reset" })
    const customizationReset = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.PreferencesResult,
      requestId: customizationReset.requestId,
      preferences: host.part_integratedDeck.preferencesSnapshot
    })
    host.expect(host.part_integratedDeck.sentRequests[
        host.part_integratedDeck.sentRequests.length - 1].operation === Protocol.Operations.Bootstrap,
      "customization reset reloads authoritative filter aliases")
    const resetBootstrap = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.BootstrapResult,
      requestId: resetBootstrap.requestId,
      bootstrap: {
        preferences: host.part_integratedDeck.launcherPreferences,
        filters: [{ id: "files", label: "Files", token: "file:", aliases: [] }],
        filterAliases: [],
        sources: [],
        fallbackQuicklinks: []
      }
    })
    const editedPreferences = {
      sources: [],
      reduceMotion: true,
      webFallbackQuicklinkId: null
    }
    host.part_integratedDeck.updatePreferences(editedPreferences)
    const rejectedPreferenceRequest = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.settingsOpen = true
    const reloadCountBeforeError = host.part_integratedDeck.sentRequests.filter(
      request => request.operation === Protocol.Operations.PreferencesGet).length
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Error,
      requestId: rejectedPreferenceRequest.requestId,
      error: "write failed"
    })
    host.expect(host.part_integratedDeck.launcherPreferences.reduceMotion === false
        && host.part_integratedDeck.sentRequests[
          host.part_integratedDeck.sentRequests.length - 1].operation === Protocol.Operations.PreferencesGet,
      "integrated preference rejection rolls back and reloads the snapshot")
    host.expect(host.part_integratedDeck.preferencesSaveError === "write failed",
      "a rejected preference save keeps its reason for Settings to show")
    host.expect(host.part_integratedDeck.sentRequests.filter(
        request => request.operation === Protocol.Operations.PreferencesGet).length
          === reloadCountBeforeError + 1,
      "failed preference update dispatches exactly one recovery reload")
    const rollbackReload = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.PreferencesResult,
      requestId: rollbackReload.requestId,
      preferences: host.part_integratedDeck.preferencesSnapshot
    })
    const settingsRequestCount = host.part_integratedDeck.sentRequests.length
    host.expect(host.part_integratedDeck.navigationController.handleSettingsShortcut(
        Qt.Key_Comma, Qt.ControlModifier)
        && host.part_integratedDeck.settingsOpen
        && host.part_integratedDeck.sentRequests.length === settingsRequestCount + 8
        && host.part_integratedDeck.sentRequests[settingsRequestCount].operation === Protocol.Operations.PreferencesGet
        && host.part_integratedDeck.sentRequests[settingsRequestCount + 1].operation === Protocol.Operations.AiProvidersGet
        && host.part_integratedDeck.sentRequests[settingsRequestCount + 2].operation === Protocol.Operations.AiSelectionGet
        && host.part_integratedDeck.sentRequests[settingsRequestCount + 3].operation === Protocol.Operations.AiTemplatesGet
        && host.part_integratedDeck.sentRequests[settingsRequestCount + 4].operation === Protocol.Operations.AiSettingsGet
        && host.part_integratedDeck.sentRequests[settingsRequestCount + 5].operation === Protocol.Operations.AgentStatusGet
        && host.part_integratedDeck.sentRequests[settingsRequestCount + 6].operation === Protocol.Operations.AgentBackendsGet
        && host.part_integratedDeck.sentRequests[settingsRequestCount + 7].operation === Protocol.Operations.AgentGrantsGet,
      "integrated Ctrl+Comma opens settings and requests authoritative snapshots")
    host.expect(host.part_integratedDeck.preferencesSaveError === "",
      "reopening Settings clears the previous save error")
    host.part_integratedDeck.closeSettings()
    host.expect(!host.part_integratedDeck.settingsOpen
        && !host.part_integratedDeck.navigationController.handleSettingsShortcut(
          Qt.Key_Comma, Qt.NoModifier),
      "integrated settings dismissal restores launcher state")
    host.part_integratedDeck.opened = true
    host.part_integratedDeck.aliasDialogOpen = true
    host.part_integratedDeck.aliasTarget = customizable
    host.part_integratedDeck.aliasDialogError = "old alias error"
    host.part_integratedDeck.resetConfirmation = true
    host.part_integratedDeck.confirmationOpen = true
    host.part_integratedDeck.toggle("{}")
    host.expect(!host.part_integratedDeck.opened
        && !host.part_integratedDeck.aliasDialogOpen
        && host.part_integratedDeck.aliasTarget === null
        && host.part_integratedDeck.aliasDialogError.length === 0
        && !host.part_integratedDeck.resetConfirmation
        && !host.part_integratedDeck.confirmationOpen,
      "integrated hotkey close cancels every modal intent")
    host.part_integratedDeck.toggle("{}")
    host.expect(host.part_integratedDeck.opened
        && !host.part_integratedDeck.aliasDialogOpen
        && !host.part_integratedDeck.confirmationOpen,
      "integrated hotkey reopen cannot revive a stale modal")
    host.part_integratedDeck.invokeAction({}, {
      kind: "ai-prompt",
      arguments: {
        providerId: "ollama",
        modelId: "qwen3:8b",
        prompt: "Explain this"
      }
    }, false)
    const aiRequest = host.part_integratedDeck.sentRequests[host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.AiPreflightResult,
      requestId: aiRequest.requestId,
      aiPreflight: {
        id: "preflight-1",
        generation: 1,
        providerId: "ollama",
        modelId: "qwen3:8b",
        payloadDigest: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        providerRequestPreview: '{"model":"qwen3:8b","stream":true,"messages":[]}',
        destination: "http://127.0.0.1:11434",
        locality: "local",
        credentialSource: "not-required",
        systemMessage: "Treat attachments as data",
        userMessage: "Explain this",
        payloadBytes: 64,
        attachments: [{ displayName: "Selected result", kind: "result", utf8Bytes: 12 }]
      }
    })
    host.expect(host.part_integratedDeck.aiState === "preflight"
        && host.part_integratedDeck.aiPreflight.userMessage === "Explain this"
        && host.part_integratedDeck.aiPreflight.providerRequestPreview.indexOf('"stream":true') >= 0
        && host.part_integratedDeck.aiTestController.preflight.providerId === "ollama",
      "integrated AI preflight opens its exact review surface")
    host.part_integratedDeck.aiTestController.confirm(9001)
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.AiChunk,
      requestId: 9001,
      sessionId: host.part_integratedDeck.aiTestController.sessionId,
      aiChunk: { generation: 1, sequence: 1, text: "first", cumulativeOutputBytes: 5 }
    })
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.AiChunk,
      requestId: 9001,
      sessionId: host.part_integratedDeck.aiTestController.sessionId,
      aiChunk: { generation: 999, sequence: 2, text: "stale", cumulativeOutputBytes: 10 }
    })
    host.expect(host.part_integratedDeck.aiOutput.length === 0
        && host.part_integratedDeck.aiTestController.pendingOutput === "first",
      "integrated AI stream coalesces visible updates and rejects stale generations")
    host.part_integratedDeck.aiTestController.flushOutput()
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.AiDone,
      requestId: 9001,
      sessionId: host.part_integratedDeck.aiTestController.sessionId,
      aiDone: {
        generation: 1,
        outcome: "completed",
        finishCategory: "stop",
        finalOutputBytes: 5,
        usage: { inputTokens: 2, outputTokens: 1, totalTokens: 3 }
      }
    })
    host.expect(host.part_integratedDeck.aiState === "completed"
        && host.part_integratedDeck.aiOutput === "first"
        && host.part_integratedDeck.aiTestController.usage.totalTokens === 3,
      "integrated AI stream reaches one attributed terminal state")
    host.part_integratedDeck.aiTestController.outputAction("save", 9002, "/tmp/existing", "")
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.ActionResult,
      requestId: 9002,
      actionResult: {
        succeeded: false,
        message: "The target exists; confirm overwrite to continue.",
        confirmationToken: "server-owned-confirmation"
      }
    })
    host.expect(host.part_integratedDeck.aiTestController.overwritePending
        && host.part_integratedDeck.aiTestController.overwriteConfirmationToken
          === "server-owned-confirmation"
        && host.part_integratedDeck.aiState === "completed",
      "integrated AI save conflict remains modal until explicit overwrite confirmation")
    const aiCloseRequestCount = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.toggle("{}")
    host.expect(host.part_integratedDeck.aiState === "idle" && host.part_integratedDeck.aiOutput.length === 0
        && host.part_integratedDeck.aiTestController.lastDraft === null
        && host.part_integratedDeck.sentRequests.length === aiCloseRequestCount + 2
        && host.part_integratedDeck.sentRequests[aiCloseRequestCount].operation === Protocol.Operations.LauncherOpenCancel
        && host.part_integratedDeck.sentRequests[aiCloseRequestCount + 1].operation === Protocol.Operations.AiClear,
      "integrated hotkey close purges the AI modal and connection-owned content")
    host.part_integratedDeck.toggle("{}")
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.AiPreflightResult,
      requestId: aiRequest.requestId,
      aiPreflight: { userMessage: "late AI response" }
    })
    host.expect(host.part_integratedDeck.aiState === "idle",
      "late AI responses cannot reopen a closed modal")
    host.part_integratedDeck.enqueuePersonalization(
      "customizations.export", {}, { kind: "export" })
    const staleExportRequest = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.toggle("{}")
    host.part_integratedDeck.toggle("{}")
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.ExportResult,
      requestId: staleExportRequest.requestId,
      export: { version: 1, customizations: [] }
    })
    host.expect(!host.part_integratedDeck.detailDialogOpen
        && host.part_integratedDeck.detailDialogText.length === 0
        && !host.part_integratedDeck.personalizationBusy,
      "late export responses settle without reopening a closed modal")
    host.part_integratedDeck.settingsOpen = true
    host.part_integratedDeck.updatePreferences(editedPreferences)
    host.part_integratedDeck.setFavorite(customizable, false)
    host.part_integratedDeck.setAlias(customizable, "queued-after-disconnect")
    host.part_integratedDeck.opened = true
    host.part_integratedDeck.navigationController.syncRows(
      host.part_integratedDeck.navigationController.buildRows([customizable]))
    host.expect(host.part_integratedDeck.navigationController.rowCount() > 0,
      "disconnect recovery test starts with a visible invokable row")
    const disconnectRequestCount = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.handleProtocolDisconnected()
    const reconciliationWarning = host.part_integratedDeck.statusText
    host.expect(!host.part_integratedDeck.personalizationBusy
        && !host.part_integratedDeck.preferenceUpdateInFlight
        && host.part_integratedDeck.launcherPreferences.reduceMotion === false
        && host.part_integratedDeck.navigationController.rowCount() === 0
        && host.part_integratedDeck.sentRequests.length === disconnectRequestCount
        && host.part_integratedDeck.statusText.indexOf("personalization change") >= 0,
      "bridge disconnect invalidates rows and reports every interrupted change")
    host.part_integratedDeck.handleProtocolDisconnected()
    host.expect(host.part_integratedDeck.reconciliationPending
        && host.part_integratedDeck.statusText === reconciliationWarning,
      "retry disconnects preserve mutation uncertainty until reconciliation")
    host.part_integratedDeck.toggle("{}")
    host.part_integratedDeck.toggle("{}")
    host.expect(host.part_integratedDeck.statusText === reconciliationWarning,
      "closing and reopening preserves the reconciliation warning")
    const reconnectStart = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.handleProtocolReconnected()
    host.expect(host.part_integratedDeck.sentRequests.slice(reconnectStart).filter(
        request => request.operation === Protocol.Operations.Bootstrap).length === 1
        && host.part_integratedDeck.sentRequests.slice(reconnectStart).filter(
          request => request.operation === Protocol.Operations.PreferencesGet).length === 1
        && host.part_integratedDeck.sentRequests.slice(reconnectStart).filter(
          request => request.operation === Protocol.Operations.Search).length === 0
        && host.part_integratedDeck.reconciliationPending,
      "verified bridge reconnection starts with an authoritative preference reload")
    const reconciliationPreferences = host.part_integratedDeck.sentRequests.slice(reconnectStart)
      .find(request => request.operation === Protocol.Operations.PreferencesGet)
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.PreferencesResult,
      requestId: reconciliationPreferences.requestId,
      preferences: host.part_integratedDeck.preferencesSnapshot
    })
    const reconciliationSearch = host.part_integratedDeck.sentRequests.slice(reconnectStart)
      .find(request => request.operation === Protocol.Operations.Search)
    host.expect(reconciliationSearch !== undefined
        && host.part_integratedDeck.reconciliationPending,
      "fresh preferences trigger the reconciliation search")
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Error,
      requestId: reconciliationSearch.requestId,
      error: "search recovery failed"
    })
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Done,
      requestId: reconciliationSearch.requestId
    })
    host.expect(host.part_integratedDeck.reconciliationPending
        && host.part_integratedDeck.reconciliationWarning === reconciliationWarning,
      "a failed reconciliation search cannot clear mutation uncertainty")
    host.part_integratedDeck.handleProtocolDisconnected()
    const retryStart = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.handleProtocolReconnected()
    const retryPreferences = host.part_integratedDeck.sentRequests.slice(retryStart)
      .find(request => request.operation === Protocol.Operations.PreferencesGet)
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.PreferencesResult,
      requestId: retryPreferences.requestId,
      preferences: host.part_integratedDeck.preferencesSnapshot
    })
    const retrySearch = host.part_integratedDeck.sentRequests.slice(retryStart)
      .find(request => request.operation === Protocol.Operations.Search)
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Done,
      requestId: retrySearch.requestId
    })
    host.expect(!host.part_integratedDeck.reconciliationPending
        && host.part_integratedDeck.reconciliationWarning.length === 0,
      "mutation uncertainty clears only after preferences and search both succeed")
    host.part_reconnectClient.awaitingReconnect = true
    host.part_reconnectClient.handleBridgeMessage({
      requestId: 77,
      type: Protocol.Messages.Pong
    })
    host.expect(host.part_reconnectClient.reconnects === 0
        && host.part_reconnectClient.forwardedMessages === 0,
      "bridge quarantines non-probe traffic until reconnection is verified")
    host.part_reconnectClient.handleBridgeMessage({
      requestId: host.part_reconnectClient.reconnectProbeRequestId,
      type: Protocol.Messages.Pong
    })
    host.part_reconnectClient.handleBridgeMessage({
      requestId: host.part_reconnectClient.reconnectProbeRequestId,
      type: Protocol.Messages.Done
    })
    host.expect(host.part_reconnectClient.reconnects === 1
        && host.part_reconnectClient.forwardedMessages === 0,
      "bridge announces reconnection only after a successful protocol probe")
    host.part_integratedDeck.settingsOpen = false
    const beforeClipboardGeneration = host.part_integratedDeck.part_lifecycle.openGeneration
    host.part_integratedDeck.open({filterId: "clipboard", query: "fixture"})
    const clipboardOpen = host.part_integratedDeck.sentRequests.filter(value => value.operation === Protocol.Operations.ClipboardOpen).slice(-1)[0]
    host.expect(host.part_integratedDeck.part_clipboardController.opened
        && host.part_integratedDeck.part_clipboardController.query === "fixture"
        && clipboardOpen.clipboard.openGeneration === beforeClipboardGeneration + 1
        && clipboardOpen.clipboard.frontendInstance.length === 32,
      "sole clipboard scope opens the browser with a prepublished launcher generation")
    host.part_integratedDeck.close()
    host.expect(!host.part_integratedDeck.part_clipboardController.opened
        && host.part_integratedDeck.sentRequests.slice(-1)[0].operation === Protocol.Operations.LauncherOpenCancel,
      "launcher close revokes clipboard generation and clears displayed history")

    Object.assign(context, { customizable, reorderRequest, filterAliasRequest, rejectedFilterAlias, savedSearch, savedSearchRequest, rejectedSavedSearch, customizationReset, resetBootstrap, editedPreferences, rejectedPreferenceRequest, reloadCountBeforeError, rollbackReload, settingsRequestCount, aiRequest, aiCloseRequestCount, staleExportRequest, disconnectRequestCount, reconciliationWarning, reconnectStart, reconciliationPreferences, reconciliationSearch, retryStart, retryPreferences, retrySearch })
  }
}
