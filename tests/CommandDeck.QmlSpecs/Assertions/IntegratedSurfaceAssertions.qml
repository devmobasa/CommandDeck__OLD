import QtQuick
import "Protocol.js" as Protocol
import "UiConstants.js" as Ui
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(done){
    host.part_integratedDeck.daemonAvailable = true
    host.part_integratedDeck.reconciliationPending = false
    const shortcutFile = {
      id: "file:shortcut", kind: "file", title: "notes.txt", canCustomize: true,
      actions: [
        { id: "open", title: "Open", kind: "open-path" },
        { id: "reveal", title: "Reveal", kind: "reveal-path" },
        { id: "copy-path", title: "Copy Path", kind: Protocol.ActionKinds.CopyText }
      ]
    }
    host.part_integratedDeck.part_searchSession.activeIdentity = host.part_integratedDeck.part_searchSession.currentIdentity()
    host.part_integratedDeck.part_searchSession.pendingResults = [shortcutFile]
    host.part_integratedDeck.part_searchSession.commit()
    host.part_integratedDeck.invokeShortcut("reveal")
    const firstShortcut = host.part_integratedDeck.sentRequests.slice(-1)[0]
    host.part_integratedDeck.invokeShortcut("copy-path")
    host.expect(host.part_integratedDeck.sentRequests.filter(request => request.resultId === shortcutFile.id
        && request.operation === Protocol.Operations.Invoke).length === 1,
      "direct shortcuts cannot overlap an admitted action")
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: firstShortcut.requestId,
      actionResult: { succeeded: false, message: "Reveal failed" } })
    host.part_integratedDeck.invokeShortcut("copy-path")
    const invocations = host.part_integratedDeck.sentRequests.filter(request => request.resultId === shortcutFile.id && request.operation === Protocol.Operations.Invoke)
    host.expect(invocations.length === 2 && invocations[0].actionId === "reveal"
        && invocations[1].actionId === "copy-path" && invocations.every(request => request.operation === Protocol.Operations.Invoke),
      "direct file shortcuts use the same result and action invocation as the menu")
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: invocations[1].requestId,
      actionResult: { succeeded: false, message: "Copy failed" } })
    host.expect(host.part_integratedDeck.directActions.map(action => action.slot + ":" + action.id).join(",")
          === "secondary:reveal,tertiary:copy-path,copy:copy-path,favorite:favorite,alias:alias,hide:hide"
        && host.part_integratedDeck.shortcutActions({ canCustomize: true, isFavorite: true, actions: [] })[0].title === "Unbookmark",
      "contextual shortcuts follow supported actions and current bookmark state")
    host.part_integratedDeck.invokeShortcut(Ui.ResultAction.Alias)
    host.expect(host.part_integratedDeck.aliasDialogOpen && host.part_integratedDeck.aliasTarget !== null
        && host.part_integratedDeck.aliasTarget.id === shortcutFile.id,
      "the alias chord opens the alias editor for the selected result")
    host.part_integratedDeck.aliasDialogOpen = false
    host.part_integratedDeck.aliasTarget = null
    host.part_integratedDeck.daemonAvailable = false
    host.part_integratedDeck.invokeShortcut("reveal")
    host.expect(host.part_integratedDeck.sentRequests.filter(request => request.resultId === shortcutFile.id && request.operation === Protocol.Operations.Invoke).length === 2,
      "direct shortcuts remain blocked while the daemon is unavailable")
    host.part_integratedDeck.daemonAvailable = true
    host.part_integratedDeck.part_searchSession.activeRequestId = 901
    const calculator = {
      id: "calculator:5+5", kind: "calculator", title: "10", subtitle: "5 + 5",
      actions: [{ id: "copy", title: "Copy Result", kind: Protocol.ActionKinds.CopyText }]
    }
    host.part_integratedDeck.part_searchSession.pendingResults = [calculator]
    host.part_integratedDeck.part_searchSession.commit()
    host.expect(host.part_integratedDeck.calculatorOnly,
      "a sole calculator result uses the full-width answer view")
    host.part_integratedDeck.part_searchSession.activeRequestId = 902
    host.part_integratedDeck.part_searchSession.pendingResults = [calculator, {
      id: "app:notes", kind: "application", title: "Notes", actions: []
    }]
    host.part_integratedDeck.part_searchSession.commit()
    host.expect(!host.part_integratedDeck.calculatorOnly,
      "mixed results keep the selectable result list")
    host.part_integratedDeck.part_searchSession.activeRequestId = 903
    host.part_integratedDeck.part_searchSession.pendingResults = []
    host.part_integratedDeck.part_searchSession.commit()
    host.expect(!host.part_integratedDeck.calculatorOnly,
      "clearing results removes calculator-only mode")
    host.part_integratedDeck.runtimeStatus = {
      catalog: { itemCount: 12 },
      inspection: {
        stats: { itemCount: 12 },
        totalExclusionDiagnosticCount: 0,
        failedItemCount: 1,
        staleItemCount: 0,
        pendingItemCount: 1,
        workItems: []
      },
      indexing: {
        started: true,
        watchedRootCount: 1,
        unwatchedRoots: ["/missing"],
        pendingEventCount: 0,
        reconciliationRequired: false,
        extractorVersion: "test",
        roots: [{
          id: "missing-root",
          path: "/missing",
          watched: false,
          paused: false,
          pendingEventCount: 0,
          indexedItemCount: 0,
          lastError: "Configured root does not exist.",
          remediation: "Check the path, permissions, and inotify limits."
        }]
      },
      providers: [{
        id: "clipboard-copy",
        label: "Clipboard copy",
        state: "degraded",
        detail: "wl-copy is not on PATH.",
        remediation: "Install with: sudo pacman -S --needed wl-clipboard"
      }]
    }
    // Keep the first-run coach from grabbing focus during the surface pass; its
    // once-only logic is exercised on the standalone coach spec instead.
    host.part_integratedDeck.coachSeen = true
    host.part_integratedDeck.testSurfaceEnabled = true
    host.part_integratedDeck.opened = true
    Qt.callLater(function() {
      Qt.callLater(function() {
        host.verifyWorkspacePreview()
        host.expect(host.part_integratedDeck.testSearchInput !== null
            && host.part_integratedDeck.testAliasDialog !== null
            && host.part_integratedDeck.testRuntimeStatusSurface !== null,
          "integrated test mode instantiates the real palette surfaces")
        host.expect(host.part_integratedDeck.testEmptyState !== null
            && host.part_integratedDeck.testNoResults !== null
            && host.part_integratedDeck.testFilterPalette !== null
            && host.part_integratedDeck.testFirstRunCoach !== null,
          "integrated test mode instantiates the slice 2 discoverability surfaces")
        host.expect(host.part_integratedDeck.testEmptyState.renderedChipCount > 0
            && host.part_integratedDeck.testEmptyState.chipTokenAt(0) === "=",
          "integrated empty-state renders filter chips led by the calculator")
        host.part_integratedDeck.testRuntimeStatusSurface.inspectorRequested()
        host.expect(host.part_integratedDeck.testRuntimeStatusSurface.visible
            && host.part_integratedDeck.testRuntimeStatusSurface.summary.indexOf("issues") >= 0
            && host.part_integratedDeck.testRuntimeStatusSurface.details.indexOf("inotify limits") >= 0
            && host.part_integratedDeck.testIndexInspector.healthDetails.indexOf(
              "sudo pacman -S --needed wl-clipboard") >= 0
            && host.part_integratedDeck.testRuntimeStatusSurface.bannerText.indexOf("inotify") < 0
            && host.part_integratedDeck.testRuntimeStatusSurface.Accessible.role === Accessible.Button,
          "compact runtime status keeps full remediation in the inspector")
        host.part_integratedDeck.testIndexInspector.dismissed()
        const footer = host.part_integratedDeck.testFooterActions
        host.expect(footer.prefixesControl.visible && footer.helpControl.visible,
          "integrated launcher always exposes prefix and help discovery")
        const daemonWasAvailable = host.part_integratedDeck.daemonAvailable
        host.part_integratedDeck.daemonAvailable = false
        host.expect(!footer.actionsEnabled && footer.prefixesControl.enabled && footer.helpControl.enabled,
          "integrated discovery stays available while daemon actions are offline")
        footer.helpControl.clicked()
        host.expect(host.part_integratedDeck.helpOpen
            && host.part_integratedDeck.helpFocusTarget === Ui.FocusReturnControl.FooterHelp,
          "footer Help opens the real sheet with its invoking control")
        host.part_integratedDeck.closeHelp()
        footer.prefixesControl.clicked()
        host.expect(host.part_integratedDeck.filterPaletteOpen
            && host.part_integratedDeck.filterFocusTarget === Ui.FocusReturnControl.FooterPrefixes,
          "footer prefixes opens the real palette with its invoking control")
        host.part_integratedDeck.testFilterPalette.tokenInserted("file:")
        host.expect(!host.part_integratedDeck.filterPaletteOpen && host.part_integratedDeck.filterFocusTarget === ""
            && host.part_integratedDeck.navigationController.activeFilters.some(value => value.id === "files"),
          "inserting a footer prefix returns to the query rather than the footer")
        host.part_integratedDeck.navigationController.activeFilters = []
        host.part_integratedDeck.daemonAvailable = daemonWasAvailable
        const bulkyRoots = []
        for (let rootIndex = 0; rootIndex < 12; rootIndex++) {
          bulkyRoots.push({
            id: "missing-root-" + rootIndex,
            path: "/missing/" + rootIndex,
            watched: false,
            paused: false,
            pendingEventCount: 0,
            indexedItemCount: 0,
            lastError: "Configured root does not exist.",
            remediation: "Check the path, permissions, and inotify limits."
          })
        }
        host.part_integratedDeck.runtimeStatus = {
          catalog: { itemCount: 12 },
          inspection: host.part_integratedDeck.runtimeStatus.inspection,
          indexing: {
            started: true,
            watchedRootCount: 0,
            unwatchedRoots: bulkyRoots.map(value => value.path),
            pendingEventCount: 0,
            reconciliationRequired: false,
            extractorVersion: "test",
            roots: bulkyRoots
          },
          providers: [
            { id: "hyprland", label: "Windows & Workspaces", state: "unavailable",
              detail: "hyprctl is not on PATH.",
              remediation: "Install with: sudo pacman -S --needed hyprland" },
            { id: "clipboard-copy", label: "Clipboard copy", state: "degraded",
              detail: "wl-copy is not on PATH.",
              remediation: "Install with: sudo pacman -S --needed wl-clipboard" },
            { id: "extractor-pdf", label: "PDF extraction", state: "degraded",
              detail: "pdftotext is not on PATH.",
              remediation: "Install with: sudo pacman -S --needed poppler" },
            { id: "calculator-qalc", label: "Qalc calculator", state: "degraded",
              detail: "qalc is not on PATH.",
              remediation: "Install with: sudo pacman -S --needed libqalculate" }
          ]
        }
        host.expect(host.part_integratedDeck.testRuntimeStatusSurface.height
            <= host.part_integratedDeck.testRuntimeStatusSurface.maximumBannerHeight + 1,
          "a crowded health banner stays capped so the result list keeps height")
        host.part_integratedDeck.testRuntimeStatusSurface.inspectorRequested()
        host.expect(host.part_integratedDeck.indexInspectorOpen
            && host.part_integratedDeck.testIndexInspector.opened,
          "integrated runtime health opens the index inspector")
        host.expect(host.part_integratedDeck.testIndexInspector.heading === "Index inspector · "
            + host.part_integratedDeck.testRuntimeStatusSurface.summary,
          "the inspector repeats the banner total including root and service issues")
        host.part_integratedDeck.testIndexInspector.dismissed()
        host.expect(!host.part_integratedDeck.indexInspectorOpen,
          "index inspector dismissal restores the palette")
        host.part_integratedDeck.settingsOpen = true
        Qt.callLater(function() {
          host.expect(host.part_integratedDeck.testSettingsPanel !== null,
            "integrated test mode builds the real Settings panel when it opens")
          const panel = host.part_integratedDeck.testSurfaceWindow.part_palette
          const bounds = panel.parent
          const settingsStyle = host.part_integratedDeck.testSettingsPanel.theme
          host.expect(settingsStyle.densityScale === 1
              && Math.abs(settingsStyle.displayScale - host.part_integratedDeck.effectiveDisplayScale) < 0.001,
            "settings keeps full typography with its window's coherent fit factor")
          host.expect(Math.abs(panel.y - (bounds.height - panel.height) / 2) <= 1
              && Math.abs(panel.x - (bounds.width - panel.width) / 2) <= 1,
            "settings is centered horizontally and vertically on its screen")
          host.expect(panel.y >= 0 && panel.y + panel.height <= bounds.height,
            "centered settings stays within the screen")
          host.part_integratedDeck.settingsOpen = false
          Qt.callLater(function() {
            host.expect(Math.abs(panel.y - panel.topInset) <= 1,
              "closing settings restores the launcher's top offset")
            host.part_integratedDeck.settingsOpen = true
            host.part_integratedDeck.testSettingsPanel.extensionsControl.clicked()
            const management = host.part_integratedDeck.testExtensionManagementView
            host.expect(management.opened && !host.part_integratedDeck.testSettingsPanel.visible,
              "Settings opens native extension management as the active surface")
            const inventoryRequest = host.part_integratedDeck.sentRequests[host.part_integratedDeck.sentRequests.length - 1]
            host.expect(inventoryRequest.operation === Protocol.Operations.ExtensionsManage && inventoryRequest.extension.action === "list",
              "opening extension management requests the real daemon inventory")
            host.part_integratedDeck.handleMessage({ requestId: inventoryRequest.requestId, type: Protocol.Messages.ExtensionsResult,
              extensions: { revision: 8, enabled: true, packages: [], total: 0, offset: 0 } })
            host.expect(management.controller.inventory.revision === 8,
              "daemon inventory responses reach the active management surface")
            management.dismissed()
            host.expect(host.part_integratedDeck.testSettingsPanel.visible && !management.opened,
              "closing extension management restores Settings")
            host.part_integratedDeck.settingsOpen = false
            const extension = host.part_integratedDeck.extensionTestController
            extension.open({ id: "extension:package:reference", title: "Reference" })
            extension.sessionId = "session"
            extension.busy = false
            extension.snapshot = { revision: 1, content: { actions: [{ kind: Protocol.ExtensionActionKinds.OpenUrl, handle: "url" }] } }
            extension.activate(1, "", "url")
            host.expect(!host.part_integratedDeck.testSurfaceWindow.visible && host.part_integratedDeck.opened,
              "URL handoff hides the overlay while keeping the session alive")
            extension.handleMessage({ requestId: extension.activeActionRequestId, type: Protocol.Messages.Error, error: "Denied" })
            host.expect(host.part_integratedDeck.testSurfaceWindow.visible && host.part_integratedDeck.opened,
              "a rejected URL restores the visible extension")
            extension.handleMessage({ requestId: extension.activeActionRequestId, type: Protocol.Messages.Done })
            extension.activate(1, "", "url")
            extension.handleMessage({ requestId: extension.activeActionRequestId, type: Protocol.Messages.Done })
            host.expect(!host.part_integratedDeck.opened && !extension.opened,
              "successful URL handoff dismisses the launcher and closes the session")
            const loginDeck = host.part_integratedDeck
            loginDeck.agentPreferences = { enabled: true, backendId: "codex.local", trustLevel: 2 }
            loginDeck.agentStatus = {
              backend: { id: "codex.local", displayName: "Codex", authenticationState: "unauthenticated" },
              login: { state: "unauthenticated", supportedMethods: ["device-code", "api-key"] }
            }
            loginDeck.settingsOpen = true
            loginDeck.testSettingsPanel.showPage("assistant")
            const loginRequestCount = loginDeck.sentRequests.filter(
              request => request.operation === Protocol.Operations.AgentLoginStart).length
            loginDeck.testSettingsPanel.agentLoginControl.clicked()
            const loginRequests = loginDeck.sentRequests.filter(
              request => request.operation === Protocol.Operations.AgentLoginStart)
            const loginRequest = loginRequests[loginRequests.length - 1]
            host.expect(loginDeck.agentOpen && !loginDeck.settingsOpen
                && loginRequests.length === loginRequestCount + 1
                && loginRequest.operation === Protocol.Operations.AgentLoginStart
                && loginRequest.loginMethod === "device-code"
                && loginRequest.backendId === "codex.local",
              "Settings sign-in opens the assistant and starts the device-code flow in one click")
            host.expect(loginDeck.testAgentSurface !== null
                && loginDeck.testAgentSurface.Accessible.name === "Assistant conversation",
              "integrated test mode instantiates the assistant conversation surface")
            loginDeck.part_agentController.handleMessage({
              type: Protocol.Messages.Error, requestId: loginRequest.requestId, error: "Sign-in failed"
            })
            loginDeck.part_agentController.status = {
              warnings: ["CommandDeck skipped a Codex launcher shim. It found /usr/bin/codex. Sign-in will check this executable."]
            }
            loginDeck.testAgentSurface.part_backendSettingsButton.clicked()
            host.expect(loginDeck.settingsOpen && !loginDeck.agentOpen
                && loginDeck.testSettingsPanel.currentPage === "ai"
                && loginDeck.testSettingsPanel.part_aiHub.section === "assistant",
              "the sign-in path warning opens the assistant backend choices")
            loginDeck.agentStatus = {
              backend: { id: "codex.local", displayName: "Codex", authenticationState: "pending" },
              login: { state: "pending", supportedMethods: ["device-code"] }
            }
            loginDeck.settingsOpen = true
            loginDeck.testSettingsPanel.agentLoginControl.clicked()
            host.expect(loginDeck.sentRequests.filter(
                request => request.operation === Protocol.Operations.AgentLoginStart).length === loginRequestCount + 1,
              "Continue sign-in resumes a pending code without starting another request")
            loginDeck.agentOpen = false
            loginDeck.settingsOpen = false
            host.part_integratedDeck.opened = false
            host.part_integratedDeck.testSurfaceEnabled = false
            // Finish destroying delegates before later protocol-only tests reopen.
            Qt.callLater(done)
          })
        })
      })
    })
  }
}
