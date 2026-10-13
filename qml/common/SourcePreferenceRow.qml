pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "UiConstants.js" as Ui

// One search source: name, a Search and a Home checkbox under the list's column
// headings, and reorder arrows. The grip, icon, and name also drag the row;
// the owning list decides where it lands. Inside a list the cells form one
// keyboard grid with a single Tab stop; a standalone row keeps every cell in
// the Tab order. The control aliases are the contract the specs drive.
Rectangle {
  id: root

  required property var theme
  required property var preference
  property string label: preference ? preference.sourceId : ""
  property string iconSource: ""
  property bool first: false
  property bool last: false
  property bool dragging: false
  // Shared with the list's heading row so the columns line up. A list too
  // narrow for the full grid sets iconBoxSize to 0, hiding the icon.
  property int gripWidth: Math.round(14 * root.theme.scale)
  property int iconBoxSize: Math.round(26 * root.theme.scale)
  // Keeps the name readable if an ancestor ever fails to hand this row its
  // full width again.
  property int nameMinimumWidth: Math.round(110 * root.theme.scale)
  property int columnWidth: Math.round(64 * root.theme.scale)
  property int orderWidth: Math.round(66 * root.theme.scale)
  property int cellSpacing: Math.round(12 * root.theme.scale)
  // A list gives only one cell a Tab stop and moves it with the arrow keys.
  property bool rovingTabStop: false
  property int tabStopColumn: Ui.SourceGridColumn.Search
  // A cell that cannot act here (an end arrow, or Home for a source that never
  // appears there) hands its Tab stop to the nearest cell that can.
  readonly property int tabStopCell: {
    let column = root.tabStopColumn
    if (column === Ui.SourceGridColumn.MoveUp && root.first)
      column = root.last ? Ui.SourceGridColumn.Home : Ui.SourceGridColumn.MoveDown
    else if (column === Ui.SourceGridColumn.MoveDown && root.last)
      column = root.first ? Ui.SourceGridColumn.Home : Ui.SourceGridColumn.MoveUp
    if (column === Ui.SourceGridColumn.Home && !root.homeAvailable) column = Ui.SourceGridColumn.Search
    return column
  }
  // False for a source that never appears on Home, such as HEY mail.
  property bool homeAvailable: true
  readonly property bool hiddenEverywhere: !!root.preference
    && !root.preference.enabledInSearch && (!root.homeAvailable || !root.preference.enabledOnHome)
  readonly property bool pointerInside: rowHover.hovered
  readonly property alias searchToggleControl: searchCheck
  readonly property alias homeToggleControl: homeCheck
  readonly property alias moveUpControl: moveUp
  readonly property alias moveDownControl: moveDown
  readonly property alias dragControl: dragArea
  // The platform drag distance: how far a press must move to count as a drag.
  readonly property real dragThreshold: dragArea.drag.threshold
  readonly property alias nameControl: nameText
  signal searchToggled(bool enabled)
  signal homeToggled(bool enabled)
  signal moveRequested(int delta)
  // Pointer positions are in this row's coordinates.
  signal dragStarted(real y)
  signal dragMoved(real y)
  signal dragFinished()
  signal dragCanceled()
  // Keyboard grid: a cell took focus, asked to move focus, or asked to move
  // this source (Alt+Up or Alt+Down).
  signal cellFocused(int column)
  signal cellNavigationRequested(int column, int rowDelta, int columnDelta)
  signal cellMoveRequested(int column, int delta)

  function cellAt(column) {
    switch (column) {
      case Ui.SourceGridColumn.Search: return searchCheck
      case Ui.SourceGridColumn.Home: return homeCheck
      case Ui.SourceGridColumn.MoveUp: return moveUp
      case Ui.SourceGridColumn.MoveDown: return moveDown
      default: return null
    }
  }

  function hasCellFocus() {
    return searchCheck.activeFocus || homeCheck.activeFocus || moveUp.activeFocus || moveDown.activeFocus
  }

  function isTabStop(column) {
    return !root.rovingTabStop || root.tabStopCell === column
  }

  function handleArrow(event, column, rowDelta, columnDelta) {
    if (rowDelta !== 0 && (event.modifiers & Qt.AltModifier)) root.cellMoveRequested(column, rowDelta)
    else root.cellNavigationRequested(column, rowDelta, columnDelta)
  }

  implicitHeight: Math.round(40 * root.theme.scale)
  color: root.dragging ? root.theme.selectedBackground : root.pointerInside ? root.theme.surface : "transparent"
  opacity: root.dragging ? 0.6 : 1
  radius: root.theme.radiusSmall
  Accessible.role: Accessible.ListItem
  Accessible.name: root.label + " source settings" + (root.hiddenEverywhere ? ", hidden" : "")

  HoverHandler {
    id: rowHover
  }

  Rectangle {
    anchors.left: parent.left
    anchors.right: parent.right
    anchors.bottom: parent.bottom
    height: 1
    visible: !root.last
    color: root.theme.separator
  }

  RowLayout {
    anchors.fill: parent
    spacing: root.cellSpacing

    Text {
      Layout.preferredWidth: root.gripWidth
      Layout.alignment: Qt.AlignVCenter
      horizontalAlignment: Text.AlignHCenter
      text: "⠿"
      color: dragArea.containsMouse || root.dragging ? root.theme.text : root.theme.textFaint
      font.family: root.theme.fontFamily
      font.pixelSize: Math.round(root.theme.fontBody * 1.2)
      Accessible.ignored: true
    }

    Rectangle {
      visible: root.iconBoxSize > 0
      Layout.preferredWidth: root.iconBoxSize
      Layout.preferredHeight: root.iconBoxSize
      Layout.alignment: Qt.AlignVCenter
      radius: Math.max(4, root.theme.radiusSmall)
      color: root.theme.surfaceStrong
      opacity: root.hiddenEverywhere ? 0.45 : 1

      Image {
        anchors.centerIn: parent
        width: Math.round(root.iconBoxSize * 0.7)
        height: width
        source: root.iconSource
        sourceSize: Qt.size(width, height)
        fillMode: Image.PreserveAspectFit
        visible: root.iconSource.length > 0 && status === Image.Ready
        asynchronous: true
        smooth: true
        mipmap: true
      }

      Text {
        anchors.centerIn: parent
        visible: root.iconSource.length === 0
        text: root.label.length > 0 ? root.label.charAt(0).toUpperCase() : "?"
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
        font.weight: Font.DemiBold
      }
    }

    Text {
      id: nameText
      Layout.fillWidth: true
      Layout.minimumWidth: root.nameMinimumWidth
      textFormat: Text.PlainText
      text: root.label
      // A source off in both columns fades so the exceptions stand out.
      color: root.hiddenEverywhere ? root.theme.textMuted : root.theme.text
      font.family: root.theme.fontFamily
      font.pixelSize: Math.round(root.theme.fontBody * 1.15)
      font.weight: Font.Medium
      elide: Text.ElideRight
    }

    // Keeps the drag area off the checkboxes and arrows beside it.
    Item {
      id: dragEnd
      Layout.preferredWidth: 0
    }

    Item {
      Layout.preferredWidth: root.columnWidth
      Layout.fillHeight: true

      SettingsCheck {
        id: searchCheck
        anchors.centerIn: parent
        theme: root.theme
        checked: root.preference ? root.preference.enabledInSearch : false
        activeFocusOnTab: root.isTabStop(Ui.SourceGridColumn.Search)
        Accessible.name: "Include " + root.label + " in universal search"
        onClicked: root.searchToggled(checked)
        onActiveFocusChanged: if (activeFocus) root.cellFocused(Ui.SourceGridColumn.Search)
        Keys.onUpPressed: event => root.handleArrow(event, Ui.SourceGridColumn.Search, -1, 0)
        Keys.onDownPressed: event => root.handleArrow(event, Ui.SourceGridColumn.Search, 1, 0)
        Keys.onLeftPressed: event => root.handleArrow(event, Ui.SourceGridColumn.Search, 0, -1)
        Keys.onRightPressed: event => root.handleArrow(event, Ui.SourceGridColumn.Search, 0, 1)
      }
    }

    Item {
      Layout.preferredWidth: root.columnWidth
      Layout.fillHeight: true

      SettingsCheck {
        id: homeCheck
        anchors.centerIn: parent
        theme: root.theme
        visible: root.homeAvailable
        enabled: root.homeAvailable
        checked: root.preference ? root.preference.enabledOnHome : false
        activeFocusOnTab: root.isTabStop(Ui.SourceGridColumn.Home)
        Accessible.name: "Show " + root.label + " on Home"
        onClicked: root.homeToggled(checked)
        onActiveFocusChanged: if (activeFocus) root.cellFocused(Ui.SourceGridColumn.Home)
        Keys.onUpPressed: event => root.handleArrow(event, Ui.SourceGridColumn.Home, -1, 0)
        Keys.onDownPressed: event => root.handleArrow(event, Ui.SourceGridColumn.Home, 1, 0)
        Keys.onLeftPressed: event => root.handleArrow(event, Ui.SourceGridColumn.Home, 0, -1)
        Keys.onRightPressed: event => root.handleArrow(event, Ui.SourceGridColumn.Home, 0, 1)
      }

      Text {
        anchors.centerIn: parent
        visible: !root.homeAvailable
        text: "—"
        color: root.theme.textFaint
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
        Accessible.role: Accessible.StaticText
        Accessible.name: root.label + " never appears on Home"
      }
    }

    // Quiet until the row is under the pointer or an arrow has focus: the
    // grip handles most reordering now.
    Row {
      Layout.preferredWidth: root.orderWidth
      Layout.alignment: Qt.AlignVCenter
      layoutDirection: Qt.RightToLeft
      spacing: Math.round(2 * root.theme.scale)
      opacity: root.pointerInside || moveUp.activeFocus || moveDown.activeFocus ? 1 : 0.5

      DeckButton {
        id: moveDown
        theme: root.theme
        compact: true
        flat: true
        text: "↓"
        enabled: !root.last
        activeFocusOnTab: root.isTabStop(Ui.SourceGridColumn.MoveDown)
        Accessible.name: "Move " + root.label + " down"
        onClicked: root.moveRequested(1)
        onActiveFocusChanged: if (activeFocus) root.cellFocused(Ui.SourceGridColumn.MoveDown)
        Keys.onUpPressed: event => root.handleArrow(event, Ui.SourceGridColumn.MoveDown, -1, 0)
        Keys.onDownPressed: event => root.handleArrow(event, Ui.SourceGridColumn.MoveDown, 1, 0)
        Keys.onLeftPressed: event => root.handleArrow(event, Ui.SourceGridColumn.MoveDown, 0, -1)
        Keys.onRightPressed: event => root.handleArrow(event, Ui.SourceGridColumn.MoveDown, 0, 1)
      }

      DeckButton {
        id: moveUp
        theme: root.theme
        compact: true
        flat: true
        text: "↑"
        enabled: !root.first
        activeFocusOnTab: root.isTabStop(Ui.SourceGridColumn.MoveUp)
        Accessible.name: "Move " + root.label + " up"
        onClicked: root.moveRequested(-1)
        onActiveFocusChanged: if (activeFocus) root.cellFocused(Ui.SourceGridColumn.MoveUp)
        Keys.onUpPressed: event => root.handleArrow(event, Ui.SourceGridColumn.MoveUp, -1, 0)
        Keys.onDownPressed: event => root.handleArrow(event, Ui.SourceGridColumn.MoveUp, 1, 0)
        Keys.onLeftPressed: event => root.handleArrow(event, Ui.SourceGridColumn.MoveUp, 0, -1)
        Keys.onRightPressed: event => root.handleArrow(event, Ui.SourceGridColumn.MoveUp, 0, 1)
      }
    }
  }

  MouseArea {
    id: dragArea
    anchors.left: parent.left
    anchors.top: parent.top
    anchors.bottom: parent.bottom
    width: dragEnd.x
    enabled: root.enabled
    hoverEnabled: true
    preventStealing: true
    cursorShape: pressed ? Qt.ClosedHandCursor : Qt.OpenHandCursor
    onPressed: mouse => root.dragStarted(mouse.y)
    onPositionChanged: mouse => {
      if (dragArea.pressed) root.dragMoved(mouse.y)
    }
    onReleased: root.dragFinished()
    onCanceled: root.dragCanceled()
  }
}
