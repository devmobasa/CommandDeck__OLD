pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import "ColorThemeFormat.js" as Format

// One palette role in Settings → Customization → Colors: preset swatches and a
// hex field. The swatches are a single Tab stop at the current colour; the
// arrow keys move between them and Enter or Space applies the focused one.
SettingsRow {
  id: row

  property string roleName: ""
  property string value: ""
  property var presets: []
  property string hexError: ""
  readonly property alias hexControl: hexField
  readonly property int swatchCount: swatches.count
  readonly property int tabStopIndex: Math.max(0, row.currentPresetIndex)
  // The swatches carry no text, so the caption names the one under the pointer,
  // else the one with keyboard focus, else the current colour.
  property int hoverIndex: -1
  property int focusIndex: -1
  readonly property int currentPresetIndex: row.presets.findIndex(preset => preset.value === row.value)
  readonly property int captionIndex: row.hoverIndex >= 0 ? row.hoverIndex : row.focusIndex
  readonly property string customCaption: "Custom"
  readonly property string captionText: row.captionIndex >= 0 && row.captionIndex < row.presets.length
    ? row.describe(row.presets[row.captionIndex])
    : (row.currentPresetIndex >= 0 ? row.presets[row.currentPresetIndex].name : row.customCaption)

  signal picked(string value)

  function swatchAt(index) {
    return swatches.itemAt(index)
  }

  function describe(preset) {
    return preset.name + " · " + preset.value
  }

  function noteHover(index, hovered) {
    if (hovered) row.hoverIndex = index
    else if (row.hoverIndex === index) row.hoverIndex = -1
  }

  function noteFocus(index, focused) {
    if (focused) row.focusIndex = index
    else if (row.focusIndex === index) row.focusIndex = -1
  }

  function focusSwatch(index) {
    const swatch = row.swatchAt(Math.max(0, Math.min(swatches.count - 1, index)))
    if (swatch) swatch.forceActiveFocus()
  }

  // Enter applies a valid hex colour; leaving the field discards an invalid one.
  function applyHex(keepEditing) {
    const color = Format.normalizeColor(hexField.text, "")
    if (color.length === 0) {
      if (keepEditing) {
        row.hexError = "Use a 6-digit hex color such as " + row.value + "."
      } else {
        row.hexError = ""
        hexField.text = row.value
      }
      return
    }

    row.hexError = ""
    hexField.text = color
    if (color !== row.value) row.picked(color)
  }

  // Escape puts the current colour back. False when there was no edit to
  // cancel, so the Escape can close Settings instead.
  function revertHex() {
    if (hexField.text === row.value && row.hexError.length === 0) return false

    row.hexError = ""
    hexField.text = row.value
    return true
  }

  onValueChanged: if (!hexField.activeFocus) hexField.text = row.value
  Component.onCompleted: hexField.text = row.value

  ColumnLayout {
    Layout.fillWidth: true
    spacing: Math.round(6 * row.theme.scale)

    RowLayout {
      Layout.fillWidth: true
      spacing: Math.round(10 * row.theme.scale)

      Repeater {
        id: swatches
        model: row.presets

        delegate: AbstractButton {
          id: swatch

          required property var modelData
          required property int index
          readonly property bool current: swatch.modelData.value === row.value

          implicitWidth: Math.round(28 * row.theme.scale)
          implicitHeight: implicitWidth
          activeFocusOnTab: swatch.index === row.tabStopIndex
          focusPolicy: Qt.StrongFocus
          hoverEnabled: true
          Accessible.role: Accessible.RadioButton
          Accessible.name: swatch.modelData.name + " " + row.roleName.toLowerCase()
          Accessible.checkable: true
          Accessible.checked: swatch.current

          // A radio group, like the other button groups: Left and Up step
          // back, Right and Down step on, Home and End reach the ends.
          Keys.onPressed: event => {
            if (event.modifiers !== Qt.NoModifier) return
            if (event.key === Qt.Key_Left || event.key === Qt.Key_Up) row.focusSwatch(swatch.index - 1)
            else if (event.key === Qt.Key_Right || event.key === Qt.Key_Down) row.focusSwatch(swatch.index + 1)
            else if (event.key === Qt.Key_Home) row.focusSwatch(0)
            else if (event.key === Qt.Key_End) row.focusSwatch(swatches.count - 1)
            else return
            event.accepted = true
          }
          Keys.onReturnPressed: row.picked(swatch.modelData.value)
          Keys.onEnterPressed: row.picked(swatch.modelData.value)
          onClicked: row.picked(swatch.modelData.value)
          onHoveredChanged: row.noteHover(swatch.index, swatch.hovered)
          onActiveFocusChanged: row.noteFocus(swatch.index, swatch.activeFocus)

          background: Item {
            Rectangle {
              anchors.fill: parent
              radius: width / 2
              color: swatch.modelData.value
              border.width: swatch.current ? 2 : 1
              // Dark swatches on a dark card need a visible outline.
              border.color: swatch.current ? row.theme.text : row.theme.withAlpha(row.theme.text, 0.28)
            }

            Rectangle {
              anchors.fill: parent
              anchors.margins: -Math.round(5 * row.theme.scale)
              visible: swatch.activeFocus
              radius: width / 2
              color: "transparent"
              border.width: Math.max(3, Math.round(3 * row.theme.scale))
              border.color: row.theme.focusRing
            }
          }

          contentItem: Text {
            visible: swatch.current
            text: "✓"
            horizontalAlignment: Text.AlignHCenter
            verticalAlignment: Text.AlignVCenter
            color: Format.relativeLuminance(swatch.modelData.value) > 0.4
              ? Qt.rgba(0, 0, 0, 0.85) : Qt.rgba(1, 1, 1, 0.95)
            font.family: row.theme.fontFamily
            font.pixelSize: row.theme.fontBody
            font.weight: Font.DemiBold
          }

        }
      }

      Item { Layout.fillWidth: true }

      SettingsField {
        id: hexField
        theme: row.theme
        Layout.preferredWidth: Math.round(116 * row.theme.scale)
        // Room for a pasted colour with spaces around it: applyHex trims them,
        // and a longer value is rejected rather than cut to a different colour.
        maximumLength: 32
        placeholderText: "#rrggbb"
        Accessible.name: row.roleName + " hex color"
        Accessible.description: "Six hexadecimal digits. Enter applies it; Escape puts back the current color."
        onEditingFinished: row.applyHex(hexField.activeFocus)
        Keys.onEscapePressed: event => event.accepted = row.revertHex()
      }
    }

    // Its own line, taking no width from the swatches: a caption that resized the
    // row would move the swatches out from under the pointer and flicker.
    Text {
      id: caption
      Layout.fillWidth: true
      Layout.preferredWidth: 0
      text: row.captionText
      color: row.theme.textMuted
      elide: Text.ElideRight
      font.family: row.theme.fontFamily
      font.pixelSize: row.theme.fontSmall
    }

    Text {
      Layout.fillWidth: true
      visible: row.hexError.length > 0
      text: row.hexError
      color: row.theme.urgent
      wrapMode: Text.Wrap
      font.family: row.theme.fontFamily
      font.pixelSize: row.theme.fontSmall
      Accessible.role: Accessible.StaticText
      Accessible.name: text
    }
  }
}
