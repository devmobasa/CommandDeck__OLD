pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

// Inline fields in the search bar for the selected result's primary action
// when it asks for one to three inputs. Tab and Shift+Tab move between them,
// Enter runs the action with their values, and Escape returns to the search
// field. The owner replaces `inputs` only when the action changes, so typed
// values survive a refresh of the same result.
RowLayout {
  id: root

  required property var theme
  // The inputs, as [{ name, defaultValue }].
  property var inputs: []
  readonly property int count: repeater.count

  signal submitted(var values)
  signal exitRequested(bool forward)
  signal missingValue(string name)

  spacing: Math.round(6 * root.theme.scale)
  visible: root.inputs.length > 0

  function fieldAt(index) {
    const pill = repeater.itemAt(index) as Pill
    return pill ? pill.field : null
  }

  function focusFirst() {
    const field = fieldAt(0)
    if (field) field.forceActiveFocus()
    return field !== null
  }

  function submit() {
    const values = {}
    for (let index = 0; index < repeater.count; index++) {
      const field = fieldAt(index)
      if (field.text.length === 0) {
        field.forceActiveFocus()
        root.missingValue(root.inputs[index].name)
        return
      }
      values[root.inputs[index].name] = field.text
    }
    root.submitted(values)
  }

  component Pill: Rectangle {
    id: pill
    required property var owner
    required property var modelData
    required property int index
    readonly property alias field: input

    implicitWidth: Math.max(Math.round(90 * pill.owner.theme.scale), input.implicitWidth + Math.round(16 * pill.owner.theme.scale))
    implicitHeight: input.implicitHeight + Math.round(6 * pill.owner.theme.scale)
    radius: pill.owner.theme.radiusSmall
    color: pill.owner.theme.withAlpha(pill.owner.theme.accent, input.activeFocus ? 0.22 : 0.12)
    border.width: input.activeFocus ? 1 : 0
    border.color: pill.owner.theme.focusRing

    TextField {
      id: input
      anchors.fill: parent
      anchors.leftMargin: Math.round(8 * pill.owner.theme.scale)
      anchors.rightMargin: Math.round(8 * pill.owner.theme.scale)
      text: pill.modelData.defaultValue || ""
      placeholderText: pill.modelData.name
      placeholderTextColor: pill.owner.theme.textFaint
      color: pill.owner.theme.text
      background: null
      font.family: pill.owner.theme.fontFamily
      font.pixelSize: pill.owner.theme.fontBody
      Accessible.role: Accessible.EditableText
      Accessible.name: "Input " + pill.modelData.name

      Keys.onPressed: event => {
        const plain = (event.modifiers & ~Qt.KeypadModifier) === Qt.NoModifier
        if ((event.key === Qt.Key_Return || event.key === Qt.Key_Enter) && plain) {
          pill.owner.submit()
          event.accepted = true
        } else if (event.key === Qt.Key_Tab && plain) {
          const next = pill.owner.fieldAt(pill.index + 1)
          if (next) next.forceActiveFocus()
          else pill.owner.exitRequested(true)
          event.accepted = true
        } else if (event.key === Qt.Key_Backtab) {
          const previous = pill.owner.fieldAt(pill.index - 1)
          if (previous) previous.forceActiveFocus()
          else pill.owner.exitRequested(false)
          event.accepted = true
        } else if (event.key === Qt.Key_Escape) {
          pill.owner.exitRequested(false)
          event.accepted = true
        }
      }
    }
  }

  Repeater {
    id: repeater
    model: root.inputs

    delegate: Pill { owner: root }
  }
}
