pragma ComponentBehavior: Bound
import QtQuick
import Quickshell
import "." as CommandDeck

Item {
  id: fixture
  required property var host
  readonly property alias aiFeaturesStoreSpecControl: aiFeaturesStoreSpec
  readonly property alias catalogSpecControl: catalogSpec
  readonly property alias coachSpecControl: coachSpec
  readonly property alias coachStoreSpecControl: coachStoreSpec
  readonly property alias colorThemeStoreSpecControl: colorThemeStoreSpec
  readonly property alias displayStoreSpecControl: displayStoreSpec
  readonly property alias emptyStateSpecControl: emptyStateSpec
  readonly property alias filterPaletteSpecControl: filterPaletteSpec
  readonly property alias noResultsSpecControl: noResultsSpec
  readonly property alias transparencyStoreSpecControl: transparencyStoreSpec

  // Slice 2 discoverability specs (A2, A3, C9, C10, D12).
  readonly property var slice2Filters: [
    { id: "applications", label: "Applications", token: "app:", aliases: [] },
    { id: "files", label: "Files", token: "file:", aliases: ["text:"] },
    { id: "services", label: "Services", token: "svc:", aliases: ["service:"] },
    { id: "ai", label: "Assistant", token: "ai:", aliases: ["codex:", "ask:"] }
  ]

  CommandDeck.FilterCatalog { id: catalogSpec }

  CommandDeck.EmptyStateView {
    id: emptyStateSpec
    parent: fixture.host.part_host
    width: 480
    height: 360
    visible: false
    theme: fixture.host.part_theme
    filterDescriptors: fixture.slice2Filters
    property string insertedToken: ""
    property int assistantRoutes: 0
    property int filtersOpens: 0
    onTokenActivated: token => insertedToken = token
    onAssistantRequested: assistantRoutes++
    onFiltersRequested: filtersOpens++
  }

  CommandDeck.NoResultsView {
    id: noResultsSpec
    parent: fixture.host.part_host
    width: 480
    height: 240
    visible: false
    theme: fixture.host.part_theme
    query: "totallyunfindable"
    filterDescriptors: fixture.slice2Filters
    property int assistantRoutes: 0
    property int filtersOpens: 0
    onAssistantRequested: assistantRoutes++
    onFiltersRequested: filtersOpens++
  }

  CommandDeck.FilterPalette {
    id: filterPaletteSpec
    parent: fixture.host.part_host
    width: 560
    height: 480
    visible: false
    theme: fixture.host.part_theme
    opened: true
    filterDescriptors: fixture.slice2Filters
    property string insertedToken: ""
    property int dismissals: 0
    onTokenInserted: token => insertedToken = token
    onDismissed: dismissals++
  }

  CommandDeck.FirstRunCoach {
    id: coachSpec
    parent: fixture.host.part_host
    width: 480
    height: 360
    theme: fixture.host.part_theme
    opened: true
    seen: false
    suppressed: false
    property int dismissals: 0
    onDismissed: dismissals++
  }

  CommandDeck.FirstRunStore {
    id: coachStoreSpec
    active: false
    stateDir: "/home/example/.local/state/commanddeck"
  }

  CommandDeck.DisplayScaleStore {
    id: displayStoreSpec
    active: false
    stateDir: "/home/example/.local/state/commanddeck"
  }

  CommandDeck.TransparencyStore {
    id: transparencyStoreSpec
    active: false
    stateDir: "/home/example/.local/state/commanddeck"
  }

  CommandDeck.ColorThemeStore {
    id: colorThemeStoreSpec
    active: false
    stateDir: "/home/example/.local/state/commanddeck"
  }

  CommandDeck.AiFeaturesStore {
    id: aiFeaturesStoreSpec
    active: false
    stateDir: "/home/example/.local/state/commanddeck"
  }
}
