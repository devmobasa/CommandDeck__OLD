import QtQuick
import QtTest
import "../../qml/common" as Deck

Item {
  id: scene
  width: 1200
  height: 900

  Deck.Theme { id: theme; loadShellTokens: false; reduceMotion: true }
  ListModel { id: rows }
  Deck.SearchNavigationController { id: navigation; rows: rows }
  Item {
    id: palette
    x: 100
    y: 100
    width: 900
    height: 600
    Deck.CommandDeckSearchField {
      id: search
      width: parent.width
      theme: theme
      controller: navigation
      surfaceVisible: true
    }
    ListView {
      id: list
      anchors.top: search.bottom
      anchors.bottom: parent.bottom
      width: parent.width
      model: rows
      spacing: theme.rowGap
      Deck.ResultHoverTracker {
        view: list
        onIndexHovered: index => navigation.selectedIndex = index
      }
      delegate: Rectangle {
        required property int index
        width: list.width
        height: theme.rowHeight
        color: index === navigation.selectedIndex ? theme.selectedBackground : theme.background
      }
    }
  }

  DeadlineTestCase {
    name: "ResultRelayout_events"
    when: windowShown

    function test_relayout_under_resting_pointer_keeps_first_row_selected() {
      navigation.replaceResults([0, 1, 2, 3, 4, 5, 6, 7].map(index => ({
        id: "app:" + index, title: "Result " + index, kind: "application", actions: []
      })), true)
      navigation.focusRevision++
      waitForValue(search.inputControl, "activeFocus", true)
      waitForCondition(() => list.itemAtIndex(3) !== null && list.itemAtIndex(3).height > 0)
      const first = navigation.selectedIndex

      const row = list.itemAtIndex(3)
      mouseMove(row, 20, row.height / 2)
      mouseMove(row, 30, row.height / 2)
      compare(navigation.selectedIndex, 3)
      const resting = row.mapToItem(scene, 30, row.height / 2)
      for (let index = first; index < 3; index++) keyClick(Qt.Key_Up)
      compare(navigation.selectedIndex, first)
      const scroll = list.contentY

      palette.y = 120.3
      palette.x = 120.7
      palette.scale = 1.2
      theme.displayScale = 1.2
      verify(waitForFrame(palette))
      mouseMove(scene, resting.x, resting.y)
      observeFor(100, () => navigation.selectedIndex === first && list.contentY === scroll,
        "layout and font changes cannot select or scroll under a resting pointer")
      verify(search.inputControl.activeFocus)

      const target = list.itemAtIndex(2)
      mouseMove(target, 50, target.height / 2)
      compare(navigation.selectedIndex, 2, "real pointer movement still selects a row")
      keyClick(Qt.Key_X)
      compare(search.inputControl.text, "x", "typing after hover stays in search")
    }
  }
}
