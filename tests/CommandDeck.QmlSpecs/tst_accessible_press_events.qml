import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// A screen reader presses a button through its accessibility press action.
// The hand-built buttons (a Rectangle with its own keys and mouse area) must
// answer it exactly like Enter or a click.
Item {
  width: 900
  height: 900

  CommandDeck.Theme { id: theme; loadShellTokens: false }

  CommandDeck.FilterChip {
    id: chip
    property int removals: 0
    theme: theme
    label: "Files"
    onRemoveRequested: removals++
  }

  CommandDeck.NoResultsView {
    id: noResults
    property int assistantRequests: 0
    property int filterRequests: 0
    y: 40
    width: 800
    height: 300
    theme: theme
    query: "report"
    filterDescriptors: [{ id: "files", label: "Files", token: "file:", aliases: [] }]
    onAssistantRequested: assistantRequests++
    onFiltersRequested: filterRequests++
  }

  CommandDeck.EmptyStateView {
    id: emptyState
    property int assistantRequests: 0
    property int filterRequests: 0
    property var tokens: []
    y: 360
    width: 800
    height: 520
    theme: theme
    filterDescriptors: [{ id: "files", label: "Files", token: "file:", aliases: [] }]
    onAssistantRequested: assistantRequests++
    onFiltersRequested: filterRequests++
    onTokenActivated: token => tokens = tokens.concat([token])
  }

  TestCase {
    name: "AccessiblePress"
    when: windowShown

    function test_press_action_activates_a_hand_built_button_data() {
      return [
        { tag: "filter chip", control: () => chip, count: () => chip.removals },
        { tag: "no-results assistant", control: () => noResults.assistantControl,
          count: () => noResults.assistantRequests },
        { tag: "no-results filters", control: () => noResults.filtersControl,
          count: () => noResults.filterRequests },
        { tag: "empty-state assistant", control: () => emptyState.assistantControl,
          count: () => emptyState.assistantRequests },
        { tag: "empty-state filter chip", control: () => emptyState.chipAt(0),
          count: () => emptyState.tokens.filter(token => token === emptyState.chipTokenAt(0)).length },
        { tag: "empty-state filters", control: () => emptyState.filtersControl,
          count: () => emptyState.filterRequests }
      ]
    }

    function test_press_action_activates_a_hand_built_button(data) {
      const control = data.control()
      verify(control !== null)
      compare(control.Accessible.role, Accessible.Button)
      const before = data.count()

      control.Accessible.pressAction()
      compare(data.count(), before + 1, "the press action activates the button once")
    }
  }
}
