pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

FocusScope {
  id: root
  required property var theme
  property bool opened: false
  property var entries: []
  property var debug: null
  property bool busy: false
  readonly property alias searchControl: search
  readonly property alias stderrControl: stderr
  readonly property alias listControl: events
  readonly property alias stopControl: stop
  signal refreshRequested(bool includeStderr)
  signal stopRequested()
  signal backRequested()
  function description(value) { return value.at + " · " + value.commandId + " · " + value.status + "\nBuild " + value.digest + "\nSession " + value.sessionId + (value.stderr ? "\nUntrusted child stderr:\n" + value.stderr : "") }
  onOpenedChanged: if (opened) { search.text = ""; stderr.checked = false; Qt.callLater(() => { if (root.opened) search.forceActiveFocus() }) }
  Keys.onEscapePressed: { if (search.text.length) search.clear(); else backRequested() }
  ColumnLayout {
    anchors.fill: parent
    Text { font.family: root.theme.fontFamily; text: "Recent extension sessions"; color: root.theme.text; font.pixelSize: root.theme.fontHeading }
    SettingsField { id: search; theme: root.theme; Layout.fillWidth: true; placeholderText: "Filter diagnostics"; Accessible.name: "Filter extension diagnostics"; KeyNavigation.tab: stderr; onTextEdited: forceActiveFocus(); Keys.onDownPressed: if (events.count) { events.currentIndex = Math.max(0, events.currentIndex); events.forceActiveFocus() } }
    CheckBox { id: stderr; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody; text: "Include child stderr (may contain sensitive data)"; enabled: !root.busy; focusPolicy: Qt.TabFocus; onToggled: root.refreshRequested(checked) }
    Text { font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody; Layout.fillWidth: true; visible: root.debug !== null; text: root.debug ? "Debug lease · " + (root.debug.consumed ? "In use" : "Open " + root.debug.commandId) + " · Expires " + root.debug.expiresAt + "\n" + root.debug.socketPath : ""; textFormat: Text.PlainText; wrapMode: Text.Wrap; color: root.theme.accent }
    DeckButton { id: stop; theme: root.theme; text: "Stop debug lease"; visible: root.debug !== null; enabled: !root.busy; focusPolicy: Qt.TabFocus; onClicked: root.stopRequested() }
    ListView {
      id: events
      Layout.fillWidth: true; Layout.fillHeight: true; clip: true; spacing: 8
      model: root.entries.filter(value => root.description(value).toLowerCase().indexOf(search.text.toLowerCase()) >= 0)
      keyNavigationEnabled: true
      Keys.priority: Keys.AfterItem
      Keys.forwardTo: [search]
      delegate: ItemDelegate {
        id: diagnosticDelegate

        required property var modelData
        width: events.width
        focusPolicy: Qt.NoFocus
        highlighted: ListView.isCurrentItem && events.activeFocus
        contentItem: Text { font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody; text: root.description(diagnosticDelegate.modelData); textFormat: Text.PlainText; wrapMode: Text.Wrap; color: root.theme.text }
        onClicked: search.forceActiveFocus()
      }
      ScrollBar.vertical: ScrollBar { }
    }
    Text { font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody; visible: events.count === 0; text: "No matching completed sessions. Close a command, then reload."; wrapMode: Text.Wrap; color: root.theme.textMuted; Layout.fillWidth: true }
  }
}
