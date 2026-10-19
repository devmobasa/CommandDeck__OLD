pragma ComponentBehavior: Bound
import QtQuick
import "ActionMeaning.js" as ActionMeaning
import "ListKeys.js" as ListKeys
import "ShortcutSlots.js" as ShortcutSlots
import "UiConstants.js" as Ui
import QtQuick.Layouts

Rectangle {
  id: root

  required property var theme
  property var result: null
  property int selectedIndex: 0
  property var filteredActions: []
  // The direct shortcut entries for this result; their chords run the same
  // action here as from the result list, and the rows show them.
  property var shortcuts: []
  property alias filterText: actionFilter.text
  readonly property alias filterControl: actionFilter
  readonly property int visibleActionCount: filteredActions.length
  signal actionChosen(var action)
  signal dismissed()

  color: root.theme.popupBackground
  radius: root.theme.radius
  border.width: 1
  border.color: root.theme.border
  clip: true
  // Natural height for the current action count; the owner caps this to fit the
  // screen and the list scrolls beyond that, so a result with many actions never
  // spills outside the panel.
  implicitHeight: Math.round(20 * root.theme.scale)
    + titleText.implicitHeight
    + actionFilter.implicitHeight
    + (root.visibleActionCount > 0
        ? root.visibleActionCount * (root.theme.actionRowHeight + 4)
        : emptyText.implicitHeight)
    + Math.round(16 * root.theme.scale)
  visible: root.result !== null
  Accessible.role: Accessible.Pane
  Accessible.name: "Actions for " + (root.result ? root.result.title : "result")

  function allActions(target) {
    const result = target === undefined ? root.result : target
    // Hidden actions (such as a HEY change's undo) are invoked by the launcher itself.
    const actions = result && result.actions ? result.actions.filter(action => action.hidden !== true) : []
    if (result && result.ranking
        && ((result.ranking.contributions && result.ranking.contributions.length > 0)
          || (result.ranking.matches && result.ranking.matches.length > 0))) {
      actions.push({
        id: Ui.ResultAction.WhyResult,
        title: "Why this result?",
        kind: Ui.ResultAction.RankingExplanation,
        common: true,
        isDestructive: false,
        requiresConfirmation: false
      })
    }
    if (result && result.canCustomize) {
      actions.push({
        id: Ui.ResultAction.Favorite,
        title: result.isFavorite ? "Remove Bookmark" : "Add Bookmark",
        kind: Ui.ResultAction.FavoriteToggle,
        common: true,
        isDestructive: false,
        requiresConfirmation: false
      })
      actions.push({
        id: Ui.ResultAction.Alias,
        title: result.aliases && result.aliases.length > 0 ? "Edit Alias…" : "Set Alias…",
        kind: Ui.ResultAction.AliasEdit,
        common: true,
        isDestructive: false,
        requiresConfirmation: false
      })
      // A reversible display preference: the item leaves every result list
      // until it is restored from the undo strip or Settings → Hidden items.
      actions.push({
        id: Ui.ResultAction.Hide,
        title: "Don't show this",
        kind: Ui.ResultAction.HideResult,
        common: true,
        isDestructive: false,
        requiresConfirmation: false
      })
    }
    return actions
  }

  ActionMatcher { id: matcher }

  function refreshFilter() {
    const previous = filteredActions.length > 0 && selectedIndex < filteredActions.length
      ? filteredActions[selectedIndex].id
      : ""
    filteredActions = matcher.filter(allActions(), actionFilter.text)
    const restored = filteredActions.findIndex(action => action.id === previous)
    selectedIndex = restored >= 0 ? restored : 0
  }

  function openFor(target) {
    result = target
    actionFilter.text = ""
    refreshFilter()
    Qt.callLater(function() { actionFilter.forceActiveFocus() })
  }

  function updateCustomization(change) {
    if (!result || !change) return
    const catalogId = result.metadata && result.metadata.catalogResultId
      ? result.metadata.catalogResultId
      : result.id
    if (catalogId !== change.stableId) return
    const updated = JSON.parse(JSON.stringify(result))
    updated.isFavorite = change.isFavorite
    updated.aliases = change.aliases || []
    result = updated
    refreshFilter()
  }

  // A refresh re-stores the row under a fresh invocation reference and the
  // daemon revokes the one this menu opened with. Follow the refreshed row,
  // keeping the filter, the selected action, and a focused action row.
  function retarget(next) {
    if (!result || !next || next.id !== result.id) return false

    const focusedId = focusedActionId()
    const selected = filteredActions[selectedIndex]
    const focused = filteredActions.find(action => action.id === focusedId)
    const fresh = allActions(next)
    for (const action of [selected, focused]) {
      if (action && !ActionMeaning.same(action, fresh.find(value => value.id === action.id))) return false
    }
    result = next
    if (focusedId.length === 0) return true

    const row = actionItemAt(filteredActions.findIndex(action => action.id === focusedId))
    if (row) row.forceActiveFocus()
    else actionFilter.forceActiveFocus()
    return true
  }

  function focusedActionId() {
    for (let index = 0; index < actionRepeater.count; index++) {
      const row = actionRepeater.itemAt(index)
      if (row && row.activeFocus) return filteredActions[index].id
    }
    return ""
  }

  function move(delta) {
    if (filteredActions.length === 0) return
    selectedIndex = Math.max(0, Math.min(filteredActions.length - 1, selectedIndex + delta))
  }

  // From the filter field (a text field) or a focused action row.
  function handleListKey(event, textInput) {
    return ListKeys.handleIndex(event, selectedIndex, filteredActions.length,
      ListKeys.pageSize(actionsFlick.height, root.theme.actionRowHeight + actionsColumn.spacing), textInput,
      index => { selectedIndex = index })
  }

  function invokeSelected() {
    if (filteredActions.length > 0) actionChosen(filteredActions[selectedIndex])
  }

  function shortcutLabel(action, index) {
    return ShortcutSlots.labelFor(root.shortcuts, action.id) || "Ctrl+" + (index + 1)
  }

  // A direct shortcut chord chooses its action, even while the filter hides it.
  function handleShortcutKey(event) {
    const entry = ShortcutSlots.entryForSlot(root.shortcuts,
      ShortcutSlots.slotForKey(event.key, event.modifiers))
    const action = entry ? allActions().find(value => value.id === entry.id) : null
    if (!action) return false
    event.accepted = true
    actionChosen(action)
    return true
  }

  // Rows group as the primary action, the result's other actions, then the
  // launcher's own; a divider marks each change of group.
  function startsGroup(index) {
    const action = filteredActions[index]
    const previous = index > 0 ? filteredActions[index - 1] : null
    if (action.common === true) return previous === null || previous.common !== true
    return index === 1 && actionFilter.text.length === 0
  }

  function invokeOrdinal(number) {
    const index = number - 1
    if (index >= 0 && index < filteredActions.length) {
      selectedIndex = index
      invokeSelected()
    }
  }

  function actionItemAt(index) {
    return index >= 0 && index < actionRepeater.count
      ? actionRepeater.itemAt(index)
      : null
  }

  function accessibilityDescription(action) {
    const descriptions = []
    if (action && action.isDestructive === true) descriptions.push("Destructive action")
    if (action && action.requiresConfirmation === true) descriptions.push("Requires confirmation")
    return descriptions.join(". ")
  }

  function restoreActionFocus() {
    const row = actionRepeater.itemAt(selectedIndex)
    Qt.callLater(function() {
      if (row) row.forceActiveFocus()
      else actionFilter.forceActiveFocus()
    })
  }

  onResultChanged: refreshFilter()

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: 10
    spacing: 4

    Text {
      id: titleText
      Layout.fillWidth: true
      Layout.margins: 8
      textFormat: Text.PlainText
      text: root.result && root.result.title ? root.result.title : ""
      color: root.theme.text
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontTitle
      font.weight: Font.DemiBold
      elide: Text.ElideRight
    }

    SettingsField {
      id: actionFilter
      Layout.fillWidth: true
      theme: root.theme
      placeholderText: "Filter actions…"
      Accessible.name: "Filter actions"
      onTextChanged: root.refreshFilter()

      Keys.onPressed: event => {
        if (root.handleListKey(event, true)) return
        if (root.handleShortcutKey(event)) return

        if (event.modifiers === Qt.ControlModifier && event.key >= Qt.Key_1 && event.key <= Qt.Key_9) {
          root.invokeOrdinal(event.key - Qt.Key_0)
          event.accepted = true
        } else if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter) {
          root.invokeSelected()
          event.accepted = true
        } else if (event.key === Qt.Key_Escape || event.key === Qt.Key_Left) {
          root.dismissed()
          event.accepted = true
        }
      }
    }

    Text {
      id: emptyText
      Layout.fillWidth: true
      Layout.margins: 8
      visible: root.filteredActions.length === 0
      text: "No matching actions"
      color: root.theme.textMuted
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontBody
      horizontalAlignment: Text.AlignHCenter
    }

    Flickable {
      id: actionsFlick
      Layout.fillWidth: true
      Layout.fillHeight: true
      clip: true
      contentWidth: width
      contentHeight: actionsColumn.height
      boundsBehavior: Flickable.StopAtBounds
      interactive: contentHeight > height

      Column {
        id: actionsColumn
        width: actionsFlick.width
        spacing: 4

        Repeater {
          id: actionRepeater
          model: root.filteredActions

          delegate: Rectangle {
            id: actionDelegate

            required property var modelData
            required property int index

            width: actionsColumn.width
            height: root.theme.actionRowHeight
            radius: root.theme.radiusSmall
            color: index === root.selectedIndex
              ? root.theme.selectedBackground
              : "transparent"
            Accessible.role: Accessible.ListItem
            Accessible.name: modelData.title + ", shortcut " + root.shortcutLabel(modelData, index)
            Accessible.description: root.accessibilityDescription(modelData)
            Accessible.selected: index === root.selectedIndex
            activeFocusOnTab: true

            Keys.onPressed: event => {
              if (root.handleListKey(event, false)) {
                root.restoreActionFocus()
                return
              }
              if (root.handleShortcutKey(event)) return

              if (event.modifiers === Qt.ControlModifier && event.key >= Qt.Key_1 && event.key <= Qt.Key_9) {
                root.invokeOrdinal(event.key - Qt.Key_0)
                root.restoreActionFocus()
                event.accepted = true
              } else if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter) {
                root.invokeSelected()
                event.accepted = true
              } else if (event.key === Qt.Key_Escape || event.key === Qt.Key_Left) {
                root.dismissed()
                event.accepted = true
              }
            }

            Rectangle {
              visible: root.startsGroup(actionDelegate.index)
              width: parent.width
              height: 1
              anchors.top: parent.top
              anchors.topMargin: -2
              color: root.theme.separator
            }

            RowLayout {
              anchors.fill: parent
              anchors.leftMargin: 10
              anchors.rightMargin: 10
              spacing: Math.round(10 * root.theme.scale)

              ActionIcon {
                Layout.preferredWidth: Math.round(16 * root.theme.scale)
                theme: root.theme
                action: actionDelegate.modelData
                bookmarked: root.result !== null && root.result.isFavorite === true
                color: actionDelegate.modelData.isDestructive ? root.theme.urgent : root.theme.textMuted
              }

              Text {
                Layout.fillWidth: true
                textFormat: Text.PlainText
                text: actionDelegate.modelData.title
                color: actionDelegate.modelData.isDestructive
                  ? root.theme.urgent
                  : (actionDelegate.index === root.selectedIndex ? root.theme.selectedText : root.theme.text)
                font.family: root.theme.fontFamily
                font.pixelSize: root.theme.fontBody
                elide: Text.ElideRight
              }

              KeyCap {
                theme: root.theme
                label: root.shortcutLabel(actionDelegate.modelData, actionDelegate.index)
              }
            }

            MouseArea {
              anchors.fill: parent
              onClicked: {
                root.selectedIndex = actionDelegate.index
                root.actionChosen(actionDelegate.modelData)
              }
            }
          }
        }
      }
    }
  }

  // Keep the selected action within the scroll viewport as the user navigates.
  onSelectedIndexChanged: root.ensureActionVisible()
  function ensureActionVisible() {
    if (root.visibleActionCount === 0) return
    const rowH = root.theme.actionRowHeight + 4
    const top = root.selectedIndex * rowH
    const bottom = top + rowH
    if (top < actionsFlick.contentY) {
      actionsFlick.contentY = top
    } else if (bottom > actionsFlick.contentY + actionsFlick.height) {
      actionsFlick.contentY = bottom - actionsFlick.height
    }
  }
}
