import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// Every list answers the same keys: Up/Down and Ctrl+P/N move a row, Page
// Up/Down a page, and Ctrl+Home/End the ends. Home and End reach the ends too
// where no text field has focus; a text field keeps them, and Shift+arrows,
// for editing. Real keys on each list's own focused control.
Item {
  id: scene
  width: 900
  height: 700

  CommandDeck.Theme { id: theme; loadShellTokens: false }

  function rows(count, make) {
    const values = []
    for (let index = 0; index < count; index++) values.push(make(index))
    return values
  }

  ListModel { id: searchRows }

  CommandDeck.SearchNavigationController {
    id: searchNavigation
    rows: searchRows
  }

  CommandDeck.CommandDeckSearchField {
    id: searchField
    width: 700
    theme: theme
    controller: searchNavigation
    surfaceVisible: true
  }

  CommandDeck.ActionPanel {
    id: actionPanel
    y: 60
    width: 400
    height: 420
    theme: theme
  }

  CommandDeck.PickerView {
    id: picker
    width: 500
    height: 420
    visible: false
    theme: theme
    session: ({ prompt: "Choose a branch", choices: scene.rows(30, index => ({ label: "Branch " + index })) })
  }

  CommandDeck.ExtensionListView {
    id: extensionList
    anchors.fill: parent
    visible: opened
    theme: theme
    snapshot: ({ revision: 1, content: { id: "keys", title: "Key probe", search: { mode: "local" },
      items: scene.rows(30, index => ({ id: "item" + index, title: "Item " + index, actions: [] })) } })
  }

  CommandDeck.FilterPalette {
    id: palette
    anchors.fill: parent
    theme: theme
    filterDescriptors: scene.rows(12, index => ({ id: "filter" + index, label: "Filter " + index,
      token: "f" + index + ":", aliases: [] }))
  }

  CommandDeck.AiModelPicker {
    id: modelPicker
    anchors.fill: parent
    theme: theme
    models: scene.rows(30, index => ({ id: "model-" + index, displayName: "Model " + index }))
  }

  TestCase {
    name: "ListKeys"
    when: windowShown

    function searchIndex() {
      const result = searchNavigation.selectedResult()
      return result ? Number(result.id.substring(1)) : -1
    }

    function lists() {
      return {
        search: {
          open: () => {
            searchNavigation.replaceResults(scene.rows(30, index => ({ id: "r" + index, kind: "file",
              sourceId: "files", title: "Result " + index, actions: [] })), true)
            return searchField.inputControl
          },
          index: () => searchIndex(),
          count: 30,
          text: true
        },
        actionField: {
          open: () => {
            actionPanel.openFor({ title: "Target", actions: scene.rows(30, index => ({ id: "action" + index,
              title: "Action " + index, kind: "custom" })) })
            return actionPanel.filterControl
          },
          index: () => actionPanel.selectedIndex,
          count: 30,
          text: true
        },
        actionRow: {
          open: () => {
            actionPanel.openFor({ title: "Target", actions: scene.rows(30, index => ({ id: "action" + index,
              title: "Action " + index, kind: "custom" })) })
            return actionPanel.actionItemAt(0)
          },
          index: () => actionPanel.selectedIndex,
          count: 30,
          text: false
        },
        picker: {
          open: () => {
            picker.visible = true
            picker.selectedIndex = 0
            return picker.inputControl
          },
          close: () => { picker.visible = false },
          index: () => picker.selectedIndex,
          count: 30,
          text: true
        },
        extensionList: {
          open: () => {
            extensionList.opened = true
            extensionList.selectedId = "item0"
            return extensionList.inputControl
          },
          close: () => { extensionList.opened = false },
          index: () => extensionList.selectedIndex,
          count: 30,
          text: true
        },
        palette: {
          open: () => {
            palette.opened = true
            return palette
          },
          close: () => { palette.opened = false },
          index: () => palette.selectedIndex,
          count: palette.entries.length,
          text: false
        },
        modelPicker: {
          open: () => {
            modelPicker.opened = true
            return modelPicker.searchControl
          },
          close: () => { modelPicker.opened = false },
          index: () => modelPicker.listControl.currentIndex,
          count: 30,
          text: true
        }
      }
    }

    function test_every_list_answers_the_same_keys_data() {
      return ["search", "actionField", "actionRow", "picker", "extensionList", "palette", "modelPicker"]
        .map(name => ({ tag: name, list: name }))
    }

    function test_every_list_answers_the_same_keys(data) {
      const list = lists()[data.list]
      const control = list.open()
      waitForRendering(control)
      control.forceActiveFocus()
      verify(control.activeFocus, "the list's own control has focus")
      compare(list.index(), 0)

      keyClick(Qt.Key_Down)
      compare(list.index(), 1, "Down")
      keyClick(Qt.Key_N, Qt.ControlModifier)
      compare(list.index(), 2, "Ctrl+N")
      keyClick(Qt.Key_Up)
      compare(list.index(), 1, "Up")
      keyClick(Qt.Key_P, Qt.ControlModifier)
      compare(list.index(), 0, "Ctrl+P")

      keyClick(Qt.Key_PageDown)
      const page = list.index()
      verify(page > 1 && page < list.count - 1, "Page Down moves a page (" + page + ")")
      keyClick(Qt.Key_PageUp)
      compare(list.index(), 0, "Page Up")

      keyClick(Qt.Key_End, Qt.ControlModifier)
      compare(list.index(), list.count - 1, "Ctrl+End")
      keyClick(Qt.Key_Home, Qt.ControlModifier)
      compare(list.index(), 0, "Ctrl+Home")

      keyClick(Qt.Key_End)
      compare(list.index(), list.text ? 0 : list.count - 1,
        list.text ? "End stays with the text field" : "End reaches the last row")
      keyClick(Qt.Key_Home)
      compare(list.index(), 0)
      if (list.text) {
        keyClick(Qt.Key_Down, Qt.ShiftModifier)
        compare(list.index(), 0, "Shift+Down stays with the text field")
      }

      if (list.close) list.close()
    }
  }
}
