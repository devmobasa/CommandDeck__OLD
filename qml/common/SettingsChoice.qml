pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Window
import "TabFence.js" as TabFence

// A drop-down whose options carry a title, a one-line description, and an
// optional badge, the way a model or a trust level should be presented.
// `options` is an array of { id, title, description, badge, warning }.
// The ComboBox contract (currentIndex, count, activated) is unchanged.
ComboBox {
  id: root

  required property var theme
  function tint(value, amount) { return Qt.rgba(value.r, value.g, value.b, amount) }
  property var options: []
  property string placeholder: "Nothing to choose"
  property bool showCurrentDescription: true
  property Item windowProbe: Item { visible: false }

  // Tab and Shift+Tab stay inside the settings dialog.
  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => TabFence.route(root.parent, event)

  readonly property var currentOption: root.currentIndex >= 0
    && root.currentIndex < (root.options || []).length
      ? root.options[root.currentIndex] : null
  readonly property string currentTitle: root.currentOption
    ? String(root.currentOption.title || root.currentOption.id || "") : ""
  readonly property string currentDescription: root.currentOption
    ? String(root.currentOption.description || "") : ""

  // Options feed the model as objects so the ComboBox value contract holds:
  // currentText is the title (falling back to the id) and currentValue the id.
  textRole: "title"
  valueRole: "id"
  model: (root.options || []).map(option => ({ id: option.id, title: String(option.title || option.id || "") }))
  implicitHeight: Math.round((root.showCurrentDescription && root.currentDescription.length > 0 ? 52 : 40) * root.theme.scale)
  implicitWidth: Math.round(260 * root.theme.scale)
  leftPadding: Math.round(12 * root.theme.scale)
  rightPadding: Math.round(36 * root.theme.scale)
  hoverEnabled: true
  focusPolicy: Qt.StrongFocus

  function optionIndex(id) {
    for (let index = 0; index < (root.options || []).length; index++) {
      if (String(root.options[index].id) === String(id)) return index
    }
    return -1
  }

  // The control stretches its content item to the full available height, so
  // the column sits inside a plain Item and centres itself in it. Otherwise a
  // tall row leaves the title hugging the top edge.
  contentItem: Item {
    Column {
      anchors.left: parent.left
      anchors.right: parent.right
      anchors.verticalCenter: parent.verticalCenter
      spacing: Math.round(1 * root.theme.scale)

      Row {
        width: parent.width
        spacing: Math.round(8 * root.theme.scale)

        Text {
          textFormat: Text.PlainText
          text: root.count > 0 ? root.currentTitle : root.placeholder
          color: root.count > 0 && root.enabled ? root.theme.text : root.theme.textFaint
          elide: Text.ElideRight
          width: Math.min(implicitWidth, parent.width - (badge.visible ? badge.width + parent.spacing : 0))
          font.family: root.theme.fontFamily
          font.pixelSize: Math.round(root.theme.fontBody * 1.15)
          font.weight: Font.Medium
        }

        Rectangle {
          id: badge
          visible: root.currentOption && root.currentOption.badge ? true : false
          anchors.verticalCenter: parent.verticalCenter
          width: badgeText.implicitWidth + Math.round(10 * root.theme.scale)
          height: badgeText.implicitHeight + Math.round(4 * root.theme.scale)
          radius: height / 2
          color: root.currentOption && root.currentOption.warning
            ? root.tint(root.theme.urgent, 0.16)
            : root.tint(root.theme.accent, 0.18)

          Text {
            id: badgeText
            anchors.centerIn: parent
            textFormat: Text.PlainText
            text: root.currentOption && root.currentOption.badge ? String(root.currentOption.badge) : ""
            color: root.currentOption && root.currentOption.warning ? root.theme.urgent : root.theme.text
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontCaption
            font.weight: Font.DemiBold
          }
        }
      }

      Text {
        width: parent.width
        visible: root.showCurrentDescription && root.currentDescription.length > 0
        textFormat: Text.PlainText
        text: root.currentDescription
        color: root.theme.textMuted
        elide: Text.ElideRight
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
      }
    }
  }

  indicator: Text {
    x: root.width - width - Math.round(12 * root.theme.scale)
    y: (root.height - height) / 2
    text: "⌄"
    color: root.enabled ? root.theme.textMuted : root.theme.textFaint
    font.family: root.theme.fontFamily
    font.pixelSize: Math.round(root.theme.fontBody * 1.3)
  }

  background: Rectangle {
    radius: Math.max(4, root.theme.radiusSmall)
    color: root.down || root.popup.visible
      ? root.tint(root.theme.text, 0.13)
      : root.hovered ? root.tint(root.theme.text, 0.11) : root.theme.surfaceStrong
    border.width: root.activeFocus ? 2 : 1
    border.color: root.activeFocus ? root.theme.focusRing : root.theme.separator
  }

  delegate: ItemDelegate {
    id: row
    required property int index
    readonly property var option: index < (root.options || []).length ? root.options[index] : null
    readonly property bool current: index === root.currentIndex
    width: ListView.view ? ListView.view.width : root.width
    height: Math.round((row.option && row.option.description ? 52 : 38) * root.theme.scale)
    hoverEnabled: true
    padding: 0

    contentItem: Item {
      Column {
        anchors.left: parent.left
        anchors.right: rowBadge.visible ? rowBadge.left : parent.right
        anchors.leftMargin: Math.round(12 * root.theme.scale)
        anchors.rightMargin: Math.round(10 * root.theme.scale)
        anchors.verticalCenter: parent.verticalCenter
        spacing: Math.round(1 * root.theme.scale)

        Text {
          width: parent.width
          textFormat: Text.PlainText
          text: row.option ? String(row.option.title || row.option.id || "") : ""
          color: row.current ? root.theme.selectedText : root.theme.text
          elide: Text.ElideRight
          font.family: root.theme.fontFamily
          font.pixelSize: Math.round(root.theme.fontBody * 1.1)
          font.weight: row.current ? Font.DemiBold : Font.Medium
        }

        Text {
          width: parent.width
          visible: row.option && row.option.description ? true : false
          textFormat: Text.PlainText
          text: row.option ? String(row.option.description || "") : ""
          color: root.theme.textMuted
          elide: Text.ElideRight
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontSmall
        }
      }

      Rectangle {
        id: rowBadge
        visible: row.option && row.option.badge ? true : false
        anchors.right: parent.right
        anchors.rightMargin: Math.round(12 * root.theme.scale)
        anchors.verticalCenter: parent.verticalCenter
        width: rowBadgeText.implicitWidth + Math.round(10 * root.theme.scale)
        height: rowBadgeText.implicitHeight + Math.round(4 * root.theme.scale)
        radius: height / 2
        color: row.option && row.option.warning
          ? root.tint(root.theme.urgent, 0.16)
          : root.tint(root.theme.accent, 0.18)

        Text {
          id: rowBadgeText
          anchors.centerIn: parent
          textFormat: Text.PlainText
          text: row.option && row.option.badge ? String(row.option.badge) : ""
          color: row.option && row.option.warning ? root.theme.urgent : root.theme.text
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontCaption
          font.weight: Font.DemiBold
        }
      }
    }

    background: Rectangle {
      color: row.current
        ? root.theme.selectedBackground
        : row.hovered || row.highlighted ? root.tint(root.theme.text, 0.08) : "transparent"
      radius: Math.max(3, root.theme.radiusSmall - 2)
    }
  }

  popup: Popup {
    // Right-aligned controls open their list leftwards so it never leaves
    // the window.
    readonly property real windowWidth: root.windowProbe.Window.window
      ? root.windowProbe.Window.window.width
      : 1e9
    readonly property real anchorX: root.visible ? root.mapToItem(null, 0, 0).x : 0
    x: anchorX + width > windowWidth - Math.round(8 * root.theme.scale) ? root.width - width : 0
    y: root.height + Math.round(4 * root.theme.scale)
    width: Math.max(root.width, Math.round(340 * root.theme.scale))
    implicitHeight: Math.min(contentItem.implicitHeight + topPadding + bottomPadding, Math.round(420 * root.theme.scale))
    padding: Math.round(6 * root.theme.scale)

    contentItem: ListView {
      clip: true
      implicitHeight: contentHeight
      model: root.popup.visible ? root.delegateModel : null
      currentIndex: root.highlightedIndex
      boundsBehavior: Flickable.StopAtBounds
      ScrollIndicator.vertical: ScrollIndicator {}
    }

    background: Rectangle {
      radius: Math.max(6, root.theme.radiusSmall)
      color: root.theme.popupBackground
      border.width: 1
      border.color: root.theme.border
    }
  }
}
