import QtQuick
import QtQuick.Layouts

SettingsRow {
  id: root

  required property var searchControl
  property int percent: 100
  readonly property alias sliderControl: slider
  readonly property alias resetControl: reset
  signal selected(int percent)

  onPercentChanged: { if (!slider.pressed) slider.value = percent }

  SettingsSlider {
    id: slider
    theme: root.theme
    Layout.fillWidth: true
    from: 80
    to: 140
    stepSize: 5
    Component.onCompleted: value = root.percent
    Accessible.name: "Launcher " + root.label.toLowerCase()
    Accessible.description: "80 to 140 percent in steps of 5; 100 is the default. "
      + root.help + " Text and control size stay unchanged."
    onMoved: root.selected(Math.round(value))
  }

  DeckButton {
    id: reset
    theme: root.theme
    text: "Reset"
    compact: true
    enabled: root.percent !== 100
    Accessible.name: "Reset launcher " + root.label.toLowerCase()
    Accessible.description: "Restore 100 percent"
    focusPolicy: Qt.TabFocus
    down: pressed || pointer.pressed

    function resetSize(fromPointer) {
      root.selected(100)
      if (fromPointer) root.searchControl.forceActiveFocus()
      else slider.sliderControl.forceActiveFocus()
    }

    onClicked: resetSize(false)
    MouseArea {
      id: pointer
      anchors.fill: parent
      acceptedButtons: Qt.LeftButton
      onClicked: reset.resetSize(true)
    }
  }
}
