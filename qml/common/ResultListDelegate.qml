pragma ComponentBehavior: Bound
import QtQuick

// One row of the launcher result list: a section header or a result, never
// both. A refresh reuses the delegate by index; the loaded item is replaced
// only when the row changes between header and result.
Loader {
  id: root

  required property var value
  required property bool isHeader
  required property string sectionTitle
  required property int sectionCount
  required property int ordinal
  required property int index
  required property var theme
  property bool selected: false
  // B5 - the launcher's computed action count, wired only for the highlighted
  // row so the "N actions" affordance appears there.
  property int actionCount: 0

  // Set by the list: a hover over a row selects it only after a real pointer
  // move, never because the row moved under a resting pointer.
  property PointerMoveGate pointerGate: null

  signal activated()
  signal actionsRequested()
  signal hoverSelected()

  sourceComponent: root.isHeader ? headerComponent : resultComponent

  Component {
    id: headerComponent

    SectionHeader {
      theme: root.theme
      title: root.sectionTitle
      count: root.sectionCount
    }
  }

  Component {
    id: resultComponent

    ResultRow {
      theme: root.theme
      result: root.value
      ordinal: root.ordinal
      selected: root.selected
      actionCount: root.actionCount
      onActivated: root.activated()
      onActionsRequested: root.actionsRequested()
      onPointerMoved: (x, y) => {
        if (root.pointerGate && root.pointerGate.moved(x, y)) root.hoverSelected()
      }
    }
  }
}
