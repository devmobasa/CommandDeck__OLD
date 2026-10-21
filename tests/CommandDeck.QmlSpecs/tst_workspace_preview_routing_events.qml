import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/WorkspacePreviewPolicy.js" as Policy
import "../../qml/common/Protocol.js" as Protocol

Item {
  id: scene
  width: 700
  height: 500
  property bool opened: false
  property var preferences: null
  readonly property var workspaceResult: ({ id: "workspace:1", kind: "workspace",
    providerId: Protocol.ProviderIds.Hyprland, sourceId: "windows-workspaces",
    metadata: { workspaceId: "1", workspaceName: "1", monitorName: "DP-1", workspaceKind: "ordinary" } })

  Deck.Theme { id: theme; loadShellTokens: false }
  ListModel { id: rows }
  Deck.SearchNavigationController {
    id: navigation
    rows: rows
    filterDescriptors: [{ id: "workspaces", label: "Workspaces", token: "workspace:", aliases: ["ws:"] }]
  }
  Deck.CommandDeckSearchField {
    id: search
    width: parent.width
    theme: theme
    controller: navigation
    filterDescriptors: navigation.filterDescriptors
    surfaceVisible: scene.opened
    onSurfaceVisibleChanged: if (surfaceVisible) navigation.focusRevision++
  }
  Deck.WorkspacePreview {
    id: preview
    y: 100
    width: 500
    theme: theme
    result: scene.workspaceResult
    eligible: scene.opened && Policy.eligibleForQuery(result, scene.preferences,
      scene.preferences, navigation.queryText, navigation.activeFilters)
  }

  DeadlineTestCase {
    name: "WorkspacePreviewRouting"
    when: windowShown

    function setPreferences(home, searchEnabled) {
      scene.preferences = { workspacePreviewMode: "layout", sources: [
        { sourceId: "windows-workspaces", enabledOnHome: home, enabledInSearch: searchEnabled }
      ] }
    }

    function init() {
      scene.opened = false
      navigation.clear()
      setPreferences(false, true)
      scene.opened = true
      waitForValue(search.inputControl, "activeFocus", true)
    }

    function cleanup() { scene.opened = false }

    function typeWorkspacePrefix() {
      keyClick(Qt.Key_W)
      keyClick(Qt.Key_S)
      keyClick(Qt.Key_Colon)
      compare(navigation.queryText, "")
      compare(search.inputControl.text, "")
      compare(navigation.activeFilters.length, 1)
      compare(navigation.activeFilters[0].id, "workspaces")
      compare(search.filterChipCount, 1)
      verify(search.inputControl.activeFocus)
    }

    function test_filter_only_query_uses_search_preference_and_keeps_typing() {
      verify(!preview.hasContent)
      typeWorkspacePrefix()
      verify(preview.hasContent)
      mouseClick(preview, 40, 10)
      keyClick(Qt.Key_A)
      compare(navigation.queryText, "a")
      verify(search.inputControl.activeFocus)
      verify(preview.hasContent)
      keyClick(Qt.Key_Backspace)
      verify(preview.hasContent)
      keyClick(Qt.Key_Backspace)
      compare(navigation.activeFilters.length, 0)
      verify(!preview.hasContent)
    }

    function test_home_only_source_does_not_enable_filter_only_search() {
      setPreferences(true, false)
      verify(preview.hasContent)
      typeWorkspacePrefix()
      verify(!preview.hasContent)
      keyClick(Qt.Key_Backspace)
      compare(navigation.activeFilters.length, 0)
      verify(preview.hasContent)
      scene.opened = false
      scene.opened = true
      waitForValue(search.inputControl, "activeFocus", true)
      typeWorkspacePrefix()
      verify(!preview.hasContent)
    }
  }
}
