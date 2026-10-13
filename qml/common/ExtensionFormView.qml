pragma ComponentBehavior: Bound
import QtQuick
import "Protocol.js" as Protocol
import QtQuick.Controls
import QtQuick.Layouts
import "UiConstants.js" as Ui

Rectangle {
  id: root
  required property var theme
  property var snapshot: ({ revision: 0, content: {} })
  property bool opened: false
  property bool busy: false
  property string externalError: ""
  readonly property var content: snapshot.content || ({})
  property var values: ({})
  property var errors: ({})
  property int formRevision: 1
  property string focusKey: ""
  property int presentedRevision: 0
  readonly property alias fieldsControl: fields
  readonly property alias submitControl: submit
  readonly property alias cancelControl: cancel
  signal presented(int revision)
  signal submitted(int revision, string handle, int formRevision, var values)
  signal backRequested()
  visible: opened
  color: theme.popupBackground
  border.width: 1
  border.color: theme.border
  radius: theme.radius
  function initial(field) { return field.defaultValue !== undefined ? field.defaultValue : field.kind === Protocol.ExtensionFormFieldKinds.Checkbox ? false : field.kind === Protocol.ExtensionFormFieldKinds.Choice ? (field.choices[0]?.value || "") : "" }
  function acknowledge() {
    if (opened && snapshot.revision > 0 && snapshot.revision !== presentedRevision) { presentedRevision = snapshot.revision; presented(presentedRevision) }
  }
  function fieldItemAt(index) { return fields.itemAt(index) }
  function restoreFocus() {
    if (!opened) return
    if (focusKey === "$submit") { submit.forceActiveFocus(); return }
    if (focusKey === "$cancel") { cancel.forceActiveFocus(); return }
    for (let i = 0; i < fields.count; i++) if (!focusKey || root.fieldItemAt(i).field.id === focusKey) { root.fieldItemAt(i).editor.forceActiveFocus(); return }
    if (fields.count > 0) root.fieldItemAt(0).editor.forceActiveFocus()
    else cancel.forceActiveFocus()
  }
  function resetState() {
    const next = {}
    for (const field of content.fields || []) next[field.id] = initial(field)
    values = next; errors = ({}); formRevision = 1; focusKey = ""; scroll.contentY = 0
    Qt.callLater(restoreFocus)
  }
  function captureState() { return { values: values, errors: errors, formRevision: formRevision, focusKey: focusKey, scroll: scroll.contentY } }
  function restoreState(state) {
    values = state.values; errors = state.errors; formRevision = state.formRevision; focusKey = state.focusKey
    scroll.contentY = state.scroll || 0; Qt.callLater(restoreFocus)
  }
  function edited(field, value) {
    values = Object.assign({}, values, { [field.id]: value }); formRevision++
    const next = Object.assign({}, errors); delete next[field.id]; errors = next
  }
  function traverse(index, backwards) {
    const next = index + (backwards ? -1 : 1)
    if (next < 0) cancel.forceActiveFocus()
    else if (next >= fields.count) submit.forceActiveFocus()
    else root.fieldItemAt(next).editor.forceActiveFocus()
  }
  function send() {
    if (busy) return
    const next = {}
    for (const field of content.fields || []) {
      const value = values[field.id]
      if (field.required && (field.kind === Protocol.ExtensionFormFieldKinds.Checkbox ? value !== true : !String(value || "").trim())) next[field.id] = "This field is required."
      else if (field.kind !== Protocol.ExtensionFormFieldKinds.Checkbox && unescape(encodeURIComponent(String(value || ""))).length > (field.maxLength || 8192)) next[field.id] = "This value is too long."
    }
    errors = next
    if (Object.keys(next).length) { focusKey = Object.keys(next)[0]; restoreFocus(); return }
    acknowledge()
    submitted(snapshot.revision, content.submit.handle, formRevision, Object.assign({}, values))
  }
  onOpenedChanged: if (opened) { Qt.callLater(restoreFocus); Qt.callLater(acknowledge) } else presentedRevision = 0
  onSnapshotChanged: {
    const content = snapshot.content || {}
    const next = {}
    for (const field of content.fields || []) next[field.id] = values[field.id] !== undefined ? values[field.id] : initial(field)
    values = next
    if (content.validatedFormRevision === formRevision) errors = content.fieldErrors || ({})
    Qt.callLater(acknowledge); Qt.callLater(restoreFocus)
  }
  onBusyChanged: if (!busy) Qt.callLater(restoreFocus)
  ColumnLayout {
    anchors.fill: parent; anchors.margins: 16; spacing: 12
    Text { text: root.content.title || "Form"; textFormat: Text.PlainText; color: root.theme.text; font.pixelSize: root.theme.fontHeading }
    Text { visible: text.length > 0; text: root.externalError || root.content.error || ""; textFormat: Text.PlainText; color: root.theme.text; Layout.fillWidth: true; wrapMode: Text.Wrap }
    ScrollView {
      id: scrollView
      Layout.fillWidth: true; Layout.fillHeight: true
      contentWidth: availableWidth
      Flickable {
        id: scroll
        contentHeight: column.implicitHeight; clip: true
        ColumnLayout {
          id: column
          width: scroll.width; spacing: 14
          Repeater {
            id: fields
            model: root.content.fields || []
            ExtensionFormField {
              required property var modelData
              required property int index
              Layout.fillWidth: true
              theme: root.theme; field: modelData; value: root.values[field.id]; error: root.errors[field.id] || ""
              onEdited: value => root.edited(field, value)
              onFocused: { root.focusKey = field.id; scroll.contentY = Math.max(0, Math.min(y, scroll.contentHeight - scroll.height)) }
              onTraverse: backwards => root.traverse(index, backwards)
              onCancel: root.backRequested()
            }
          }
        }
      }
    }
    RowLayout {
      spacing: Math.round(12 * root.theme.scale)

      DeckButton {
        id: submit
        theme: root.theme
        kind: Ui.ButtonVariant.Primary
        text: root.busy ? "Saving…" : root.content.submit?.title || "Save"
        onClicked: root.send()
        onActiveFocusChanged: if (activeFocus) root.focusKey = "$submit"
        Keys.onEscapePressed: root.backRequested()
        Keys.onTabPressed: cancel.forceActiveFocus()
        Keys.onBacktabPressed: root.traverse(fields.count, true)
        Keys.onRightPressed: cancel.forceActiveFocus()
      }
      DeckButton {
        id: cancel
        theme: root.theme
        kind: Ui.ButtonVariant.Secondary
        text: "Cancel · Esc"
        onClicked: root.backRequested()
        onActiveFocusChanged: if (activeFocus) root.focusKey = "$cancel"
        Keys.onEscapePressed: root.backRequested()
        Keys.onTabPressed: root.traverse(-1, false)
        Keys.onBacktabPressed: submit.forceActiveFocus()
        Keys.onLeftPressed: submit.forceActiveFocus()
      }
    }
  }
}
