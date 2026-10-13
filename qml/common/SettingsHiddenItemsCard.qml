pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Layouts

// Settings → Customization → Hidden items. Search the list, restore an entry
// without opening it, and see which entries no longer exist. Pages are loaded
// on demand from the daemon, never as part of the preferences snapshot, and
// focus stays on the search box or the pressed Restore by control identity.
// Icons go through the panel's resolver so this card stays free of shell-only
// modules and runs in the offscreen event specs.
SettingsCard {
  id: card

  required property var host
  readonly property var hiddenItems: card.host.hiddenItems
  readonly property var items: card.hiddenItems ? card.hiddenItems.items || [] : []
  readonly property int total: card.hiddenItems ? card.hiddenItems.total || 0 : 0
  readonly property bool truncated: card.hiddenItems ? card.hiddenItems.truncated === true : false
  readonly property bool shouldLoad: card.host.visible
    && card.host.currentPage === Ui.SettingsPage.Customization
  readonly property alias searchControl: hiddenSearch
  readonly property alias loadMoreControl: loadMoreButton
  readonly property int renderedCount: rowRepeater.count
  property string pendingFocusId: ""

  function renderedItemAt(index) { return rowRepeater.itemAt(index) as HiddenRow }
  function restoreControlAt(index) {
    const row = rowRepeater.itemAt(index) as HiddenRow
    return row ? row.restoreControl : null
  }
  function refresh(offset) { card.host.hiddenItemsRequested(hiddenSearch.text, offset || 0) }
  function restoreFocus() {
    if (!card.shouldLoad || !card.pendingFocusId) return
    const id = card.pendingFocusId
    card.pendingFocusId = ""
    for (let index = 0; index < rowRepeater.count; index++) {
      const row = card.renderedItemAt(index)
      if (row && row.modelData.id === id) {
        row.restoreControl.forceActiveFocus()
        return
      }
    }
    hiddenSearch.forceActiveFocus()
  }
  function focusRow(index) {
    const target = card.restoreControlAt(index)
    if (target) target.forceActiveFocus()
  }
  onItemsChanged: Qt.callLater(card.restoreFocus)

  component HiddenRow: RowLayout {
    id: hiddenRow
    required property var modelData
    required property int index
    readonly property alias restoreControl: restoreButton
    readonly property bool available: hiddenRow.modelData.isAvailable === true
    readonly property string iconSource: card.host.iconResolver(
      hiddenRow.modelData.icon && hiddenRow.modelData.icon.length > 0
        ? hiddenRow.modelData.icon
        : card.host.sourceIcon(hiddenRow.modelData.sourceId))
    Layout.fillWidth: true
    spacing: Math.round(10 * card.theme.scale)
    Accessible.role: Accessible.ListItem
    Accessible.name: hiddenRow.modelData.title + (hiddenRow.available ? "" : ", unavailable")
    Keys.priority: Keys.AfterItem
    Keys.forwardTo: [hiddenSearch]
    Keys.onUpPressed: card.focusRow(hiddenRow.index - 1)
    Keys.onDownPressed: card.focusRow(hiddenRow.index + 1)

    Rectangle {
      Layout.preferredWidth: Math.round(30 * card.theme.scale)
      Layout.preferredHeight: Layout.preferredWidth
      Layout.alignment: Qt.AlignVCenter
      radius: Math.max(4, card.theme.radiusSmall)
      color: card.theme.surfaceStrong

      Image {
        id: rowIcon
        anchors.centerIn: parent
        width: Math.round(20 * card.theme.scale)
        height: width
        source: hiddenRow.iconSource
        sourceSize: Qt.size(width, height)
        fillMode: Image.PreserveAspectFit
        visible: hiddenRow.iconSource.length > 0 && status === Image.Ready
        asynchronous: true
        smooth: true
      }

      Text {
        anchors.centerIn: parent
        visible: !rowIcon.visible
        text: hiddenRow.modelData.title.length > 0 ? hiddenRow.modelData.title.charAt(0).toUpperCase() : "•"
        color: card.theme.textMuted
        font.family: card.theme.fontFamily
        font.pixelSize: card.theme.fontBody
        font.weight: Font.DemiBold
      }
    }

    ColumnLayout {
      Layout.fillWidth: true
      spacing: Math.round(2 * card.theme.scale)

      Text {
        Layout.fillWidth: true
        textFormat: Text.PlainText
        text: hiddenRow.modelData.title
        color: hiddenRow.available ? card.theme.text : card.theme.textMuted
        elide: Text.ElideRight
        font.family: card.theme.fontFamily
        font.pixelSize: Math.round(card.theme.fontBody * 1.1)
        font.weight: Font.Medium
      }

      Text {
        Layout.fillWidth: true
        textFormat: Text.PlainText
        text: card.host.sourceLabel(hiddenRow.modelData.sourceId)
          + (hiddenRow.modelData.subtitle ? " · " + hiddenRow.modelData.subtitle : "")
        color: card.theme.textFaint
        elide: Text.ElideMiddle
        font.family: card.theme.fontFamily
        font.pixelSize: card.theme.fontSmall
      }
    }

    Text {
      visible: !hiddenRow.available
      text: "Unavailable"
      color: card.theme.urgent
      font.family: card.theme.fontFamily
      font.pixelSize: card.theme.fontSmall
      Accessible.role: Accessible.StaticText
      Accessible.name: hiddenRow.modelData.title + " is unavailable"
    }

    DeckButton {
      id: restoreButton
      theme: card.theme
      compact: true
      text: "Restore"
      focusPolicy: Qt.TabFocus
      enabled: !card.host.loading && !card.host.snapshotMutationBusy
      Accessible.name: "Restore " + hiddenRow.modelData.title
      Accessible.description: hiddenRow.available
        ? "Shows this item in results again"
        : "Forgets this hidden entry; the item itself no longer exists"
      onClicked: card.host.hiddenItemRestoreRequested(hiddenRow.modelData.id)
    }
  }

  searchQuery: card.host.part_settingsSearch.text
  searchTerms: "customization hidden items hide restore don't show unavailable"
  theme: card.host.theme
  title: "Hidden items"
  description: "Results you chose not to show. Restore brings one back without opening it. An unavailable entry no longer exists; restoring it only forgets the preference."

  onShouldLoadChanged: {
    if (card.shouldLoad) card.refresh(0)
    else {
      debounce.stop()
      card.pendingFocusId = ""
      card.host.hiddenItemsDismissed()
    }
  }
  Component.onCompleted: if (card.shouldLoad) card.refresh(0)

  SettingsField {
    id: hiddenSearch
    theme: card.theme
    Layout.fillWidth: true
    placeholderText: "Search hidden items by name or location…"
    Accessible.name: "Search hidden items"
    onTextChanged: if (card.shouldLoad) debounce.restart()
    onTextEdited: forceActiveFocus()
  }

  Timer {
    id: debounce
    interval: 200
    onTriggered: if (card.shouldLoad) card.refresh(0)
  }

  Text {
    Layout.fillWidth: true
    visible: card.host.hiddenItemsError.length > 0
    textFormat: Text.PlainText
    text: card.host.hiddenItemsError
    wrapMode: Text.Wrap
    color: card.theme.urgent
    font.family: card.theme.fontFamily
    font.pixelSize: card.theme.fontBody
    Accessible.role: Accessible.StaticText
    Accessible.name: text
  }

  Text {
    Layout.fillWidth: true
    visible: card.items.length === 0 && card.host.hiddenItemsError.length === 0
    text: hiddenSearch.text.length > 0
      ? "No hidden items match."
      : "Nothing is hidden. Choose \"Don't show this\" from a result's actions to hide it."
    wrapMode: Text.Wrap
    color: card.theme.textMuted
    font.family: card.theme.fontFamily
    font.pixelSize: card.theme.fontBody
  }

  Repeater {
    id: rowRepeater
    model: card.items
    onItemRemoved: (index, item) => {
      const row = item as HiddenRow
      if (row && row.restoreControl.activeFocus) card.pendingFocusId = row.modelData.id
    }

    delegate: HiddenRow {}
  }

  RowLayout {
    Layout.fillWidth: true
    visible: card.truncated
    spacing: Math.round(8 * card.theme.scale)
    Keys.priority: Keys.AfterItem
    Keys.forwardTo: [hiddenSearch]

    Text {
      Layout.fillWidth: true
      text: "Showing " + card.items.length + " of " + card.total
      color: card.theme.textFaint
      font.family: card.theme.fontFamily
      font.pixelSize: card.theme.fontSmall
    }

    DeckButton {
      id: loadMoreButton
      theme: card.theme
      compact: true
      text: "Show more"
      focusPolicy: Qt.TabFocus
      Accessible.name: "Show more hidden items"
      onClicked: card.refresh(card.items.length)
    }
  }
}
