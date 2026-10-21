import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

// Hidden items with real key events: the action offered for customizable
// results, and the Settings → Hidden items card searching, restoring, and
// keeping focus across an asynchronous refresh.
EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_hidden_items"

    // The customization page scrolls; a mouse click can only land on a control
    // inside the viewport, so bring it into view the way a user would.
    function scrollIntoView(panel, item) {
      const flickable = panel.parts.customizationScroll.contentItem
      const top = item.mapToItem(flickable.contentItem, 0, 0).y
      flickable.contentY = Math.max(0, Math.min(top - 40, flickable.contentHeight - flickable.height))
      waitForFrame(panel)
    }

    function hiddenList() {
      return {
        query: "", total: 2, truncated: false,
        items: [
          { id: "hidden:file:notes", stableId: "file:notes", title: "notes.txt", subtitle: "/tmp/notes.txt",
            sourceId: "files", icon: "", isAvailable: true },
          { id: "hidden:app:old.desktop", stableId: "app:old.desktop", title: "Old App", subtitle: "",
            sourceId: "applications", icon: "", isAvailable: false }
        ]
      }
    }
    function focusByTab(control) {
      for (let index = 0; index < 120 && !control.activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(control.activeFocus, "The hidden-items control must be reachable with Tab")
    }
    function openHiddenCard() {
      const panel = fixture.part_settingsPanel
      panel.part_hiddenItemsCard.searchControl.text = ""
      panel.visible = true
      waitForValue(panel.searchControl, "activeFocus", true)
      typeText("hidden items")
      keyClick(Qt.Key_Escape)
      panel.hiddenItems = hiddenList()
      waitForValue(panel.part_hiddenItemsCard, "renderedCount", 2)
      return panel.part_hiddenItemsCard
    }

    function test_hidden_restore_buttons_support_arrow_navigation() {
      const card = openHiddenCard()
      focusByTab(card.searchControl)
      keyClick(Qt.Key_Tab)
      verify(card.restoreControlAt(0).activeFocus)
      keyClick(Qt.Key_Down)
      verify(card.restoreControlAt(1).activeFocus)
      keyClick(Qt.Key_Up)
      verify(card.restoreControlAt(0).activeFocus)
      fixture.part_settingsPanel.visible = false
    }

    function test_mouse_restore_keeps_search_ready_for_typing() {
      const card = openHiddenCard()
      focusByTab(card.searchControl)
      scrollIntoView(fixture.part_settingsPanel, card.restoreControlAt(0))
      mouseClick(card.restoreControlAt(0))
      compare(fixture.part_settingsPanel.hiddenItemRestored, "hidden:file:notes")
      typeText("note")
      compare(card.searchControl.text, "note")
      verify(card.searchControl.activeFocus)
      waitForValue(fixture.part_settingsPanel, "hiddenItemsQueryRequested", "note")
      fixture.part_settingsPanel.visible = false
    }

    function test_action_panel_offers_dont_show_this_for_customizable_results() {
      const target = {
        id: "file:notes", title: "notes.txt", canCustomize: true, isFavorite: false, aliases: [],
        actions: [{ id: "open", title: "Open" }]
      }
      fixture.part_actionPanel.openFor(target)
      fixture.part_actionPanel.filterControl.forceActiveFocus()
      typeText("dont")
      compare(fixture.part_actionPanel.visibleActionCount, 1)
      compare(fixture.part_actionPanel.filteredActions[0].kind, Ui.ResultAction.HideResult)
      compare(fixture.part_actionPanel.filteredActions[0].title, "Don't show this")
      keyClick(Qt.Key_Return)
      compare(fixture.part_actionPanel.chosen, Ui.ResultAction.Hide)

      fixture.part_actionPanel.openFor({
        id: "window:1", title: "Terminal", canCustomize: false, actions: [{ id: "focus", title: "Focus" }]
      })
      verify(!fixture.part_actionPanel.filteredActions.some(action => action.kind === Ui.ResultAction.HideResult))
    }

    function test_hidden_items_card_loads_searches_and_restores_with_real_keys() {
      const panel = fixture.part_settingsPanel
      panel.hiddenItemsRequests = 0
      panel.hiddenItemRestored = ""
      panel.part_hiddenItemsCard.searchControl.text = ""
      panel.visible = true
      waitForValue(panel.searchControl, "activeFocus", true)
      typeText("hidden items")
      compare(panel.currentPage, "customization")
      waitForCondition(() => panel.hiddenItemsRequests >= 1)
      compare(panel.hiddenItemsQueryRequested, "")
      compare(panel.hiddenItemsOffsetRequested, 0)
      keyClick(Qt.Key_Escape)

      const card = panel.part_hiddenItemsCard
      panel.hiddenItems = hiddenList()
      waitForValue(card, "renderedCount", 2)
      compare(card.renderedItemAt(1).available, false)

      focusByTab(card.searchControl)
      keyClick(Qt.Key_Tab)
      verify(card.restoreControlAt(0).activeFocus)
      keyClick(Qt.Key_Tab)
      verify(card.restoreControlAt(1).activeFocus)
      keyClick(Qt.Key_Space)
      compare(panel.hiddenItemRestored, "hidden:app:old.desktop")

      // A replacement preserves keyboard focus by identity, including reordering.
      const reordered = hiddenList()
      reordered.items.reverse()
      panel.hiddenItems = reordered
      waitForCondition(() => card.restoreControlAt(0).activeFocus)
      compare(card.renderedItemAt(0).modelData.id, "hidden:app:old.desktop")

      // Typing from a focused restore button reaches search immediately.
      const before = panel.hiddenItemsRequests
      typeText("note")
      compare(card.searchControl.text, "note")
      waitForCondition(() => panel.hiddenItemsRequests > before)
      compare(panel.hiddenItemsQueryRequested, "note")
      compare(panel.hiddenItemsOffsetRequested, 0)
      panel.hiddenItems = { query: "note", total: 1, truncated: false, items: [hiddenList().items[0]] }
      waitForValue(card, "renderedCount", 1)
      verify(card.searchControl.activeFocus)

      // Mouse path: the refreshed rows must be laid out and in view first.
      scrollIntoView(panel, card.restoreControlAt(0))
      mouseClick(card.restoreControlAt(0))
      compare(panel.hiddenItemRestored, "hidden:file:notes")
      typeText("x")
      compare(card.searchControl.text, "notex")
      verify(card.searchControl.activeFocus)
      waitForValue(panel, "hiddenItemsQueryRequested", "notex")

      panel.hiddenItems = { query: "notex", total: 70, truncated: true, items: hiddenList().items }
      waitForValue(card.loadMoreControl, "visible", true)
      scrollIntoView(panel, card.loadMoreControl)
      mouseClick(card.loadMoreControl)
      compare(panel.hiddenItemsOffsetRequested, 2)
      typeText("y")
      compare(card.searchControl.text, "notexy")
      verify(card.searchControl.activeFocus)
      waitForValue(panel, "hiddenItemsQueryRequested", "notexy")
      compare(panel.hiddenItemsOffsetRequested, 0)

      // If the focused restored row disappears, search becomes the useful fallback.
      focusByTab(card.restoreControlAt(0))
      keyClick(Qt.Key_Return)
      panel.hiddenItems = { query: "notexy", total: 0, truncated: false, items: [] }
      waitForValue(card.searchControl, "activeFocus", true)

      panel.visible = false
      panel.visible = true
      waitForValue(panel.searchControl, "activeFocus", true)
      compare(card.searchControl.text, "notexy")

      panel.hiddenItems = null
      panel.visible = false
    }
  }
}
