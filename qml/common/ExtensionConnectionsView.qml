pragma ComponentBehavior: Bound
import QtQuick
import "Protocol.js" as Protocol
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts
import QtQuick.Dialogs as NativeDialogs

FocusScope {
  id: root
  required property var theme
  property bool opened: false
  property bool busy: false
  property var connections: []
  property string message: ""
  property int effectAttempts: 0
  property bool editing: false
  readonly property alias nameControl: name
  readonly property alias pathControl: path
  readonly property alias addControl: add
  readonly property alias saveControl: save
  readonly property alias searchControl: search
  readonly property alias listControl: list
  readonly property alias loginControl: login
  readonly property alias resetEffectsControl: resetEffects
  signal configureRequested(var setup)
  signal disconnectRequested(string name, bool remote)
  signal loginRequested(string name)
  signal resetEffectsRequested()
  signal backRequested()
  function reset() { editing = false; search.text = ""; credential.text = ""; Qt.callLater(() => { if (root.opened) search.forceActiveFocus() }) }
  onOpenedChanged: if (opened) reset(); else credential.text = ""
  Keys.onEscapePressed: { if (editing) reset(); else if (search.text) search.clear(); else backRequested() }
  NativeDialogs.FolderDialog { id: folders; onAccepted: path.text = decodeURIComponent(selectedFolder.toString().replace(/^file:\/\//, "")) }
  NativeDialogs.FileDialog { id: files; onAccepted: path.text = decodeURIComponent(selectedFile.toString().replace(/^file:\/\//, "")) }
  ColumnLayout {
    anchors.fill: parent
    Text { font.family: root.theme.fontFamily; text: "Files and service connections"; color: root.theme.text; font.pixelSize: root.theme.fontHeading }
    Text { font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody; Layout.fillWidth: true; wrapMode: Text.Wrap; text: "Connections last for this daemon session. Replacing or disabling the package revokes them. Credentials stay in the host."; color: root.theme.textMuted }
    Text { font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody; Layout.fillWidth: true; wrapMode: Text.Wrap; text: root.message; visible: text.length > 0; color: root.theme.text; textFormat: Text.PlainText }
    ColumnLayout {
      visible: !root.editing
      Layout.fillWidth: true; Layout.fillHeight: true
      SettingsField { id: search; theme: root.theme; Layout.fillWidth: true; placeholderText: "Filter connections"; Accessible.name: "Filter connections"; onTextEdited: forceActiveFocus(); Keys.onDownPressed: if (list.count) { list.currentIndex = Math.max(0, list.currentIndex); list.forceActiveFocus() } }
      RowLayout {
        Text { text: root.effectAttempts + " recorded remote-write attempts"; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody; color: root.theme.textMuted }
        DeckButton { id: resetEffects; theme: root.theme; text: "Reset remote-write history…"; kind: Ui.ButtonVariant.Danger; enabled: !root.busy; onClicked: root.resetEffectsRequested() }
      }
      ListView {
        id: list
        Layout.fillWidth: true; Layout.fillHeight: true; clip: true; spacing: 8
        model: root.connections.filter(value => (value.name + " " + value.scope).toLowerCase().indexOf(search.text.toLowerCase()) >= 0)
        keyNavigationEnabled: true; Keys.priority: Keys.AfterItem; Keys.forwardTo: [search]
        delegate: ItemDelegate {
          id: connectionRow
          required property var modelData
          required property int index
          width: list.width; focusPolicy: Qt.NoFocus
          font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody
          text: modelData.name + " · " + modelData.kind + "\n" + modelData.scope + "\n" + modelData.credentialStatus
          highlighted: list.currentIndex === index
          onClicked: { list.currentIndex = index; search.forceActiveFocus() }
          // Connection names and scopes come from the extension; show them as written.
          contentItem: Text { text: connectionRow.text; textFormat: Text.PlainText; font: connectionRow.font; color: connectionRow.highlighted ? connectionRow.palette.highlightedText : connectionRow.palette.text; verticalAlignment: Text.AlignVCenter; elide: Text.ElideRight }
        }
      }
      RowLayout {
        DeckButton { id: add; theme: root.theme; text: "Add / replace connection"; enabled: !root.busy; onClicked: { name.text = ""; path.text = ""; credential.text = ""; root.editing = true; Qt.callLater(() => name.forceActiveFocus()) } }
        DeckButton { id: login; theme: root.theme; text: "Connect in browser"; enabled: !root.busy && list.currentIndex >= 0 && list.count > 0; onClicked: { root.loginRequested(list.model[list.currentIndex].name); search.forceActiveFocus() } }
        DeckButton { theme: root.theme; kind: Ui.ButtonVariant.Danger; text: "Disconnect…"; enabled: !root.busy && list.currentIndex >= 0 && list.count > 0; onClicked: { root.disconnectRequested(list.model[list.currentIndex].name, false); search.forceActiveFocus() } }
        DeckButton { theme: root.theme; kind: Ui.ButtonVariant.Danger; text: "Revoke remotely…"; enabled: !root.busy && list.currentIndex >= 0 && list.count > 0; onClicked: { root.disconnectRequested(list.model[list.currentIndex].name, true); search.forceActiveFocus() } }
      }
    }
    ScrollView {
      visible: root.editing
      Layout.fillWidth: true; Layout.fillHeight: true; contentWidth: availableWidth
      ColumnLayout {
        width: parent.width
        SettingsField { id: name; theme: root.theme; Layout.fillWidth: true; placeholderText: "Connection name used by the extension"; Accessible.name: "Connection name"; maximumLength: 128 }
        SettingsChoice { id: connectionKind; theme: root.theme; options: [{ id: Protocol.ExtensionConnectionKinds.Files, title: "files" }, { id: Protocol.ExtensionConnectionKinds.Http, title: "http" }]; Accessible.name: "Connection kind" }
        RowLayout {
          visible: connectionKind.currentText === Protocol.ExtensionConnectionKinds.Files; Layout.fillWidth: true
          SettingsField { id: path; theme: root.theme; Layout.fillWidth: true; placeholderText: "Absolute file or folder path"; Accessible.name: "Selected file or folder"; maximumLength: 4096 }
          DeckButton { theme: root.theme; text: "Choose folder"; onClicked: folders.open() }
          DeckButton { theme: root.theme; text: "Choose file"; onClicked: files.open() }
        }
        ColumnLayout {
          visible: connectionKind.currentText === Protocol.ExtensionConnectionKinds.Http; Layout.fillWidth: true
          SettingsField { id: origin; theme: root.theme; Layout.fillWidth: true; placeholderText: "https://api.example.com"; Accessible.name: "Service origin"; maximumLength: 2048 }
          SettingsField { id: route; theme: root.theme; Layout.fillWidth: true; text: "/"; Accessible.name: "Allowed route prefix"; maximumLength: 2048 }
          SettingsField { id: methods; theme: root.theme; Layout.fillWidth: true; text: "GET"; placeholderText: "GET,POST"; Accessible.name: "Allowed HTTP methods"; maximumLength: 64 }
          CheckBox { id: privateNetwork; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody; text: "Allow this explicit private IP (including HTTP)" }
          SettingsChoice { id: credentialKind; theme: root.theme; options: [Protocol.ExtensionCredentialKinds.None, Protocol.ExtensionCredentialKinds.SessionBearer, Protocol.ExtensionCredentialKinds.SystemdBearer, Protocol.ExtensionCredentialKinds.OAuth].map(value => ({ id: value, title: value })); Accessible.name: "Credential mechanism" }
          SettingsField { id: credential; theme: root.theme; visible: credentialKind.currentText === Protocol.ExtensionCredentialKinds.SessionBearer || credentialKind.currentText === Protocol.ExtensionCredentialKinds.SystemdBearer; Layout.fillWidth: true; echoMode: TextInput.Password; placeholderText: credentialKind.currentText === Protocol.ExtensionCredentialKinds.SystemdBearer ? "Systemd credential name" : "Bearer token (memory only)"; Accessible.name: "Credential value or systemd name"; maximumLength: 8192 }
          ColumnLayout {
            visible: credentialKind.currentText === Protocol.ExtensionCredentialKinds.OAuth; Layout.fillWidth: true
            SettingsField { id: issuer; theme: root.theme; Layout.fillWidth: true; placeholderText: "Issuer origin (must return iss in callback)"; Accessible.name: "OAuth issuer"; maximumLength: 2048 }
            SettingsField { id: authorization; theme: root.theme; Layout.fillWidth: true; placeholderText: "Authorization endpoint"; Accessible.name: "Authorization endpoint"; maximumLength: 2048 }
            SettingsField { id: token; theme: root.theme; Layout.fillWidth: true; placeholderText: "Token endpoint"; Accessible.name: "Token endpoint"; maximumLength: 2048 }
            SettingsField { id: revocation; theme: root.theme; Layout.fillWidth: true; placeholderText: "Revocation endpoint (optional)"; Accessible.name: "Revocation endpoint"; maximumLength: 2048 }
            SettingsField { id: client; theme: root.theme; Layout.fillWidth: true; placeholderText: "Public client ID"; Accessible.name: "OAuth client ID"; maximumLength: 256 }
            SettingsField { id: scopes; theme: root.theme; Layout.fillWidth: true; placeholderText: "Scopes"; Accessible.name: "OAuth scopes"; maximumLength: 1024 }
          }
        }
        RowLayout {
          DeckButton {
            theme: root.theme
            kind: Ui.ButtonVariant.Primary
            id: save; text: "Review connection…"; enabled: !root.busy && name.text.length > 0
            onClicked: {
              const setup = { name: name.text, kind: connectionKind.currentText }
              if (connectionKind.currentText === Protocol.ExtensionConnectionKinds.Files) setup.path = path.text
              else {
                Object.assign(setup, { origin: origin.text, route: route.text, methods: methods.text.split(",").map(value => value.trim()), privateNetwork: privateNetwork.checked, credentialKind: credentialKind.currentText })
                if (credentialKind.currentText === Protocol.ExtensionCredentialKinds.SessionBearer || credentialKind.currentText === Protocol.ExtensionCredentialKinds.SystemdBearer) setup.credential = credential.text
                if (credentialKind.currentText === Protocol.ExtensionCredentialKinds.OAuth) setup.oauth = { issuer: issuer.text, authorizationEndpoint: authorization.text, tokenEndpoint: token.text, revocationEndpoint: revocation.text || null, clientId: client.text, scope: scopes.text }
              }
              credential.text = ""
              root.configureRequested(setup)
            }
          }
          DeckButton { theme: root.theme; text: "Cancel"; onClicked: root.reset() }
        }
      }
    }
  }
}
