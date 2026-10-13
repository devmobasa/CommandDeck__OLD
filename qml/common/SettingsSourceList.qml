pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "Protocol.js" as Protocol
import "UiConstants.js" as Ui

// Settings → Search sources: a heading row, then one row per source with a
// Search and a Home checkbox and reorder arrows. The checkboxes and arrows form
// one keyboard grid with a single Tab stop: arrow keys move between cells,
// Space or Enter toggles, and Alt+Up or Alt+Down moves the focused source.
// Dragging a row's grip moves it anywhere, with a line marking where it will
// land and the page scrolling near its edges. Either way the new order is saved
// as one change.
Item {
  id: list

  required property var host
  readonly property alias repeater: sourceRepeater
  readonly property alias headingControl: heading
  readonly property var sources: list.host.snapshot && list.host.snapshot.preferences
    ? list.host.snapshot.preferences.sources
    : []
  property int dragIndex: -1
  // The gap the dragged source would land in: 0 is above the first row,
  // count is below the last.
  property int dropGap: -1
  property real pointerY: 0
  // Where the press landed; the page only auto-scrolls once the pointer has
  // moved past the platform drag distance from it, so a click or a held
  // press never scrolls.
  property real pressY: 0
  property real dragThreshold: 0
  property bool dragMovedPastThreshold: false
  readonly property bool dropChangesOrder: list.dragIndex >= 0 && list.dropGap >= 0
    && list.dropGap !== list.dragIndex && list.dropGap !== list.dragIndex + 1
  readonly property int autoScrollStep: Math.round(18 * list.host.theme.scale)
  readonly property int autoScrollZone: Math.round(48 * list.host.theme.scale)

  // The grid's Tab stop follows the source, not its row, so it survives a
  // reorder. An unknown source (or none yet) falls back to the first row.
  property string currentSourceId: ""
  property int currentColumn: Ui.SourceGridColumn.Search
  property int currentRow: 0
  property bool focusPending: false
  readonly property string tabStopSourceId: list.indexOfSource(list.currentSourceId) >= 0
    ? list.currentSourceId
    : list.sources.length > 0 ? list.sources[0].sourceId : ""

  // Column widths shared by the heading and every row. A narrow Settings (a
  // small screen or a large display size) cannot hold the full grid: the
  // icons go and the columns tighten, so the arrows stay inside the list.
  readonly property int fullGridWidth: Math.round((14 + 26 + 110 + 64 * 2 + 66 + 12 * 6) * list.host.theme.scale)
  readonly property bool compact: list.width > 0 && list.width < list.fullGridWidth
  readonly property int gripWidth: Math.round(14 * list.host.theme.scale)
  readonly property int iconBoxSize: list.compact ? 0 : Math.round(26 * list.host.theme.scale)
  readonly property int nameMinimumWidth: Math.round((list.compact ? 48 : 110) * list.host.theme.scale)
  readonly property int columnWidth: Math.round((list.compact ? 44 : 64) * list.host.theme.scale)
  readonly property int orderWidth: Math.round(66 * list.host.theme.scale)
  readonly property int cellSpacing: Math.round((list.compact ? 4 : 12) * list.host.theme.scale)

  // What the last reorder told assistive technology, such as "Files moved to
  // position 4 of 4". The list's description keeps it for readers that do
  // not take announcements.
  property string moveAnnouncement: ""

  Layout.fillWidth: true
  implicitHeight: rows.implicitHeight
  Accessible.role: Accessible.List
  Accessible.name: "Search sources"
  Accessible.description: list.moveAnnouncement

  function rowAt(index) {
    return sourceRepeater.itemAt(index)
  }

  function indexOfSource(sourceId) {
    for (let index = 0; index < list.sources.length; index++) {
      if (list.sources[index].sourceId === sourceId) return index
    }
    return -1
  }

  function cellEnabled(index, column) {
    const row = list.rowAt(index)
    const cell = row ? row.cellAt(column) : null
    return cell !== null && cell.enabled
  }

  // The column itself if it can take focus, otherwise its partner (the other
  // checkbox, or the other arrow), then the other pair.
  function nearestEnabledColumn(index, column) {
    const checks = [Ui.SourceGridColumn.Search, Ui.SourceGridColumn.Home]
    const arrows = [Ui.SourceGridColumn.MoveUp, Ui.SourceGridColumn.MoveDown]
    const own = checks.indexOf(column) >= 0 ? checks : arrows
    const order = [column].concat(own.filter(value => value !== column),
      own === checks ? arrows : [Ui.SourceGridColumn.Home, Ui.SourceGridColumn.Search])
    for (const candidate of order) {
      if (list.cellEnabled(index, candidate)) return candidate
    }
    return -1
  }

  function focusCell(index, column) {
    const target = list.nearestEnabledColumn(index, column)
    if (target < 0) return false
    list.rowAt(index).cellAt(target).forceActiveFocus()
    return true
  }

  function noteFocus(index, column) {
    list.currentSourceId = list.sources[index].sourceId
    list.currentColumn = column
    list.currentRow = index
  }

  // Left and Right skip cells that cannot act (the top row's up arrow) and stop
  // at the edges; Up and Down keep the column where they can.
  function navigate(index, column, rowDelta, columnDelta) {
    if (rowDelta !== 0) {
      const target = index + rowDelta
      if (target >= 0 && target < sourceRepeater.count) list.focusCell(target, column)
      return
    }
    for (let next = column + columnDelta; next >= 0 && next <= Ui.SourceGridColumn.MoveDown; next += columnDelta) {
      if (list.cellEnabled(index, next)) {
        list.rowAt(index).cellAt(next).forceActiveFocus()
        return
      }
    }
  }

  // Focus follows a moved source, so a screen reader would hear only the
  // focused cell; say where the source landed as well.
  function announceMove(from, to) {
    list.moveAnnouncement = list.host.sourceLabel(list.sources[from].sourceId)
      + " moved to position " + (to + 1) + " of " + list.sources.length
    if (typeof list.Accessible.announce === "function") list.Accessible.announce(list.moveAnnouncement)
  }

  // The arrows, Alt+Up, and Alt+Down move a source one place.
  function moveBy(index, delta) {
    const target = index + delta
    if (target < 0 || target >= list.sources.length) return

    list.announceMove(index, target)
    list.host.moveSource(index, delta)
  }

  // A saved change rebuilds every row. If a cell had focus, give it back to the
  // same source and column wherever that source now sits.
  function restoreFocus() {
    if (!list.focusPending) return
    list.focusPending = false
    if (!list.visible || sourceRepeater.count === 0) return
    let index = list.indexOfSource(list.currentSourceId)
    if (index < 0) index = Math.min(list.currentRow, sourceRepeater.count - 1)
    list.focusCell(index, list.currentColumn)
  }

  function gapAt(y) {
    for (let index = 0; index < sourceRepeater.count; index++) {
      const row = list.rowAt(index)
      if (row && y < row.y + row.height / 2) return index
    }
    return sourceRepeater.count
  }

  function gapY(gap) {
    if (sourceRepeater.count === 0) return 0
    if (gap < sourceRepeater.count) return list.rowAt(gap).y
    const last = list.rowAt(sourceRepeater.count - 1)
    return last.y + last.height
  }

  function beginDrag(index, y, threshold) {
    list.pressY = y
    list.dragThreshold = threshold
    list.pointerY = y
    list.dragMovedPastThreshold = false
    list.dragIndex = index
    list.dropGap = index
  }

  function updateDrag(y) {
    if (list.dragIndex < 0) return

    list.pointerY = y
    if (Math.abs(y - list.pressY) >= list.dragThreshold) list.dragMovedPastThreshold = true
    list.dropGap = list.gapAt(y)
  }

  function finishDrag() {
    const from = list.dragIndex
    const gap = list.dropGap
    const changes = list.dropChangesOrder
    list.cancelDrag()
    if (!changes) return

    const to = gap > from ? gap - 1 : gap
    list.announceMove(from, to)
    list.host.moveSourceTo(from, to)
  }

  function cancelDrag() {
    list.dragIndex = -1
    list.dropGap = -1
    list.dragMovedPastThreshold = false
  }

  // Escape during a drag drops it and leaves the order alone; releasing the
  // pointer afterwards saves nothing. False when no drag is in progress.
  function cancelDragByKeyboard() {
    if (list.dragIndex < 0) return false

    list.cancelDrag()
    return true
  }

  // The Settings page that scrolls this list, if any.
  function scrollingPage(): Flickable {
    let item = list.parent
    while (item && !(item instanceof Flickable)) item = item.parent
    return item as Flickable
  }

  component HeadingText: Text {
    id: headingText
    required property var theme
    color: headingText.theme.textFaint
    font.family: headingText.theme.fontFamily
    font.pixelSize: headingText.theme.fontCaption
    font.weight: Font.DemiBold
    font.letterSpacing: 0.6
    font.capitalization: Font.AllUppercase
    verticalAlignment: Text.AlignVCenter
    elide: Text.ElideRight
  }

  ColumnLayout {
    id: rows
    anchors.left: parent.left
    anchors.right: parent.right
    spacing: 0

    // Mirrors the row layout below, cell for cell, so each heading sits over
    // its column.
    RowLayout {
      id: heading
      Layout.fillWidth: true
      Layout.preferredHeight: Math.round(26 * list.host.theme.scale)
      spacing: list.cellSpacing
      Accessible.ignored: true

      Item { Layout.preferredWidth: list.gripWidth }
      Item {
        visible: list.iconBoxSize > 0
        Layout.preferredWidth: list.iconBoxSize
      }
      HeadingText {
        theme: list.host.theme
        Layout.fillWidth: true
        text: "Source"
      }
      Item { Layout.preferredWidth: 0 }
      HeadingText {
        theme: list.host.theme
        objectName: "sourceSearchHeading"
        Layout.preferredWidth: list.columnWidth
        horizontalAlignment: Text.AlignHCenter
        text: "Search"
      }
      HeadingText {
        theme: list.host.theme
        objectName: "sourceHomeHeading"
        Layout.preferredWidth: list.columnWidth
        horizontalAlignment: Text.AlignHCenter
        text: "Home"
      }
      HeadingText {
        theme: list.host.theme
        Layout.preferredWidth: list.orderWidth
        horizontalAlignment: Text.AlignHCenter
        text: "Order"
      }
    }

    Rectangle {
      Layout.fillWidth: true
      Layout.preferredHeight: 1
      color: list.host.theme.separator
    }

    Repeater {
      id: sourceRepeater
      model: list.sources
      onItemRemoved: (index, item) => {
        const row = item as SourcePreferenceRow
        if (!row || !row.hasCellFocus()) return
        list.focusPending = true
        Qt.callLater(list.restoreFocus)
      }

      SourcePreferenceRow {
        id: row
        required property int index
        required property var modelData
        Layout.fillWidth: true
        theme: list.host.theme
        enabled: !list.host.loading
        preference: row.modelData
        label: list.host.sourceLabel(row.modelData.sourceId)
        iconSource: list.host.iconResolver(list.host.sourceIcon(row.modelData.sourceId))
        first: row.index === 0
        last: row.index === list.sources.length - 1
        dragging: list.dragIndex === row.index
        homeAvailable: list.host.sourceHomeAvailable(row.modelData.sourceId)
        gripWidth: list.gripWidth
        iconBoxSize: list.iconBoxSize
        nameMinimumWidth: list.nameMinimumWidth
        columnWidth: list.columnWidth
        orderWidth: list.orderWidth
        cellSpacing: list.cellSpacing
        rovingTabStop: true
        tabStopColumn: list.tabStopSourceId === row.modelData.sourceId ? list.currentColumn : -1
        onSearchToggled: enabled => list.host.mutateSource(row.index, Protocol.SourcePreferenceFields.EnabledInSearch, enabled)
        onHomeToggled: enabled => list.host.mutateSource(row.index, Protocol.SourcePreferenceFields.EnabledOnHome, enabled)
        onMoveRequested: delta => list.moveBy(row.index, delta)
        onCellFocused: column => list.noteFocus(row.index, column)
        onCellNavigationRequested: (column, rowDelta, columnDelta) => list.navigate(row.index, column, rowDelta, columnDelta)
        onCellMoveRequested: (column, delta) => list.moveBy(row.index, delta)
        onDragStarted: y => list.beginDrag(row.index, row.mapToItem(list, 0, y).y, row.dragThreshold)
        onDragMoved: y => list.updateDrag(row.mapToItem(list, 0, y).y)
        onDragFinished: list.finishDrag()
        onDragCanceled: list.cancelDrag()
      }
    }
  }

  Rectangle {
    id: dropLine
    visible: list.dropChangesOrder
    x: 0
    width: list.width
    height: Math.max(2, Math.round(3 * list.host.theme.scale))
    y: list.gapY(Math.max(0, list.dropGap)) - height / 2
    radius: height / 2
    color: list.host.theme.accent
  }

  Timer {
    interval: 16
    repeat: true
    running: list.dragIndex >= 0 && list.dragMovedPastThreshold
    onTriggered: {
      const page = list.scrollingPage()
      if (!page) return

      const pointer = list.mapToItem(page, 0, list.pointerY).y
      const maximum = Math.max(0, page.contentHeight - page.height)
      let delta = 0
      if (pointer < list.autoScrollZone) delta = -list.autoScrollStep
      else if (pointer > page.height - list.autoScrollZone) delta = list.autoScrollStep
      const next = Math.max(0, Math.min(maximum, page.contentY + delta))
      if (next === page.contentY) return

      const shift = next - page.contentY
      page.contentY = next
      list.updateDrag(list.pointerY + shift)
    }
  }
}
