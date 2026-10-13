pragma ComponentBehavior: Bound
import QtQuick
import "Protocol.js" as Protocol
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts

FocusScope {
  id: root
  required property var theme
  required property var controller
  property bool opened: false
  property Item returnFocus: null
  property string page: Ui.ExtensionManagementPage.List
  property var selected: null
  property var manifest: ({})
  property var savedSettings: ({})
  // This revision belongs to savedSettings, not to later inventory refreshes.
  property var inspectedRevision: 0
  property var grants: []
  property string archiveDigest: ""
  property string inspectedPath: ""
  property var confirmation: null
  property string confirmationText: ""
  property string previousPage: Ui.ExtensionManagementPage.Detail
  property Item invoker: null
  property int settingsRevision: 0
  property int diagnosticsRequestId: 0
  property int connectionRequestId: 0
  onPageChanged: { diagnosticsRequestId = 0; connectionRequestId = 0 }
  // Stock controls on these pages (rows, checkboxes, scroll bars) draw from the
  // palette, which every nested page inherits; map it to the launcher theme.
  palette.window: root.theme.popupBackground
  palette.windowText: root.theme.text
  palette.base: root.theme.surfaceStrong
  palette.alternateBase: root.theme.surface
  palette.text: root.theme.text
  palette.placeholderText: root.theme.textFaint
  palette.button: root.theme.surfaceStrong
  palette.buttonText: root.theme.text
  palette.highlight: root.theme.withAlpha(root.theme.accent, 0.28)
  palette.highlightedText: root.theme.selectedText
  palette.light: root.theme.surfaceStrong
  palette.midlight: root.theme.surfaceStrong
  palette.mid: root.theme.separator
  palette.dark: root.theme.border
  palette.shadow: root.theme.popupBackground
  palette.link: root.theme.accent
  readonly property var preferenceTypes: Object.freeze({ Text: "text", Number: "number", Boolean: "boolean", Choice: "choice", Secret: "secret" })
  readonly property alias searchControl: search
  readonly property alias backControl: backButton
  readonly property alias archiveControl: archive
  readonly property alias settingsControl: settings
  readonly property alias packagesControl: packages
  readonly property alias cancelControl: cancel
  readonly property alias removeControl: remove
  readonly property alias reloadControl: reload
  readonly property alias logsControl: logs
  readonly property alias diagnosticsControl: diagnostics
  readonly property alias connectionsControl: connectionsView
  readonly property alias connectionsButtonControl: connectionsButton
  readonly property alias globalSearchControl: globalSearch
  readonly property alias scopedSearchControl: scopedSearch
  signal dismissed()
  signal browserLoginRequested()
  signal scopedSearchRequested(string filterId, string label)
  visible: opened
  function focusLater(control) { Qt.callLater(function() { if (root.opened && control && control.visible) control.forceActiveFocus() }) }
  function isConnectionAction(action) {
    return [Protocol.ExtensionManagementActions.Connections,
      Protocol.ExtensionManagementActions.ConnectionSet,
      Protocol.ExtensionManagementActions.ConnectionRemove,
      Protocol.ExtensionManagementActions.ConnectionLogin,
      Protocol.ExtensionManagementActions.EffectsReset].indexOf(action) >= 0
  }
  function showList() { page = Ui.ExtensionManagementPage.List; root.controller.load(search.text, 0); focusLater(search) }
  function inspect(item) { if (root.controller.busy) return; selected = item; root.controller.change({ action: Protocol.ExtensionManagementActions.Inspect, key: item.key }) }
  function back() {
    if (page === Ui.ExtensionManagementPage.Confirm) { confirmation = null; page = previousPage; focusLater(invoker); return }
    if (page === Ui.ExtensionManagementPage.Logs) { page = Ui.ExtensionManagementPage.Detail; focusLater(logs); return }
    if (page === Ui.ExtensionManagementPage.Connections) { page = Ui.ExtensionManagementPage.Detail; focusLater(connectionsButton); return }
    if (page === Ui.ExtensionManagementPage.Settings || page === Ui.ExtensionManagementPage.Grants) { page = Ui.ExtensionManagementPage.Detail; focusLater(configure); return }
    if (page !== Ui.ExtensionManagementPage.List) { showList(); return }
    dismissed()
  }
  function changeDiagnostics(input) {
    if (root.controller.busy) return
    root.controller.change(input)
    diagnosticsRequestId = root.controller.activeRequestId
  }
  function loadLogs(includeStderr) { changeDiagnostics({ action: Protocol.ExtensionManagementActions.Logs, key: selected.key, includeStderr: includeStderr }) }
  function changeConnection(input) {
    if (root.controller.busy) return
    root.controller.change(input)
    connectionRequestId = root.controller.activeRequestId
  }
  function approveChange() {
    const input = Object.assign({}, confirmation, { confirmed: true, eraseSettings: erase.checked })
    if (input.action === Protocol.ExtensionManagementActions.Debug) changeDiagnostics(input)
    else if (root.isConnectionAction(input.action)) {
      if (input.action === Protocol.ExtensionManagementActions.ConnectionLogin) browserLoginRequested()
      changeConnection(input)
    }
    else root.controller.change(input)
    confirmation = null
  }
  function confirm(input, text, control) {
    previousPage = page; invoker = control
    confirmation = input; confirmationText = text; page = Ui.ExtensionManagementPage.Confirm; focusLater(cancel)
  }
  function configureSettings() {
    if ((manifest.preferences || []).some(p => p.type === root.preferenceTypes.Secret)) { root.controller.error = "This package requires secret settings, which this preview does not support."; return }
    const fields = (manifest.preferences || []).map(p => ({
      id: p.id, label: p.title, kind: p.type === root.preferenceTypes.Boolean ? "checkbox" : p.type === root.preferenceTypes.Choice ? "choice" : "text",
      required: p.type !== root.preferenceTypes.Boolean && p.required, maxLength: p.maximumLength || 8192,
      choices: p.choices || [], defaultValue: p.type === root.preferenceTypes.Number
        ? String(savedSettings[p.id] !== undefined ? savedSettings[p.id] : p.default !== undefined ? p.default : "")
        : savedSettings[p.id] !== undefined ? savedSettings[p.id] : p.default
    }))
    if (fields.length === 0) { root.controller.error = "This package has no supported non-secret settings."; return }
    page = Ui.ExtensionManagementPage.Settings
    settings.snapshot = { revision: ++settingsRevision, content: { id: "settings", title: "Configure " + manifest.name,
      fields: fields, submit: { title: "Save settings", handle: "settings" } } }
    settings.resetState()
  }
  onOpenedChanged: {
    diagnosticsRequestId = 0
    connectionRequestId = 0
    if (!opened) confirmation = null
    if (opened) showList()
    else if (returnFocus) Qt.callLater(function() { if (!root.opened && root.returnFocus) root.returnFocus.forceActiveFocus() })
  }
  Keys.onEscapePressed: back()
  function contains(item) {
    for (let current = item; current; current = current.parent) if (current === root) return true
    return false
  }
  Keys.onPressed: event => {
    if (event.key !== Qt.Key_Tab && event.key !== Qt.Key_Backtab) return
    const forward = event.key === Qt.Key_Tab && !(event.modifiers & Qt.ShiftModifier)
    const current = root.Window.window.activeFocusItem
    const next = current ? current.nextItemInFocusChain(forward) : null
    if (next && root.contains(next)) next.forceActiveFocus()
    else root.focusLater(root.page === Ui.ExtensionManagementPage.List ? search : backButton)
    event.accepted = true
  }
  Connections {
    target: root.controller
    function onReceived(action, inventory) {
      if (!root.opened) return
      if (root.isConnectionAction(action)) {
        if (root.connectionRequestId !== root.controller.activeRequestId) return
        root.connectionRequestId = 0
        connectionsView.connections = inventory.connections || []
        connectionsView.message = inventory.connectionMessage || ""
        connectionsView.effectAttempts = inventory.effectAttempts || 0
        if (action === Protocol.ExtensionManagementActions.ConnectionSet) connectionsView.editing = false
        root.page = Ui.ExtensionManagementPage.Connections
        if (action === Protocol.ExtensionManagementActions.EffectsReset) root.focusLater(connectionsView.resetEffectsControl)
        else if (action !== Protocol.ExtensionManagementActions.Connections) root.focusLater(connectionsView.searchControl)
      } else if (action === Protocol.ExtensionManagementActions.Logs || action === Protocol.ExtensionManagementActions.Debug || action === Protocol.ExtensionManagementActions.DebugStop) {
        if (root.diagnosticsRequestId !== root.controller.activeRequestId) return
        root.diagnosticsRequestId = 0
        const restoreSearch = diagnostics.stopControl.activeFocus && !inventory.debug
        diagnostics.entries = inventory.diagnostics || []
        diagnostics.debug = inventory.debug || null
        root.page = Ui.ExtensionManagementPage.Logs
        if (restoreSearch) root.focusLater(diagnostics.searchControl)
      } else if (action === Protocol.ExtensionManagementActions.Inspect) {
        root.manifest = inventory.manifest || ({})
        root.savedSettings = inventory.settings || ({})
        root.inspectedRevision = inventory.revision
        root.archiveDigest = inventory.archiveDigest || ""
        root.grants = root.archiveDigest ? [] : (root.selected.grants || []).slice()
        root.page = root.archiveDigest ? "archive-review" : "detail"
        root.focusLater(root.archiveDigest ? install : configure)
      } else if (action !== Protocol.ExtensionManagementActions.List) root.showList()
    }
  }
  Rectangle { anchors.fill: parent; color: root.theme.popupBackground; radius: root.theme.radius }
  ColumnLayout {
    anchors.fill: parent; anchors.margins: 16; spacing: 10
    RowLayout {
      Layout.fillWidth: true
      Text { text: "Extensions"; color: root.theme.text; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontHeading; Layout.fillWidth: true }
      DeckButton { id: reload; theme: root.theme; text: "Reload"; focusPolicy: Qt.TabFocus; visible: root.page !== Ui.ExtensionManagementPage.Confirm; enabled: !root.controller.busy; onClicked: { if (root.page === Ui.ExtensionManagementPage.Logs) root.loadLogs(diagnostics.stderrControl.checked); else if (root.page === Ui.ExtensionManagementPage.Connections) root.changeConnection({ action: Protocol.ExtensionManagementActions.Connections, key: root.selected.key }); else root.controller.load(search.text, 0) } }
      DeckButton { id: backButton; theme: root.theme; enabled: root.page !== Ui.ExtensionManagementPage.Confirm; text: root.page === Ui.ExtensionManagementPage.List ? "Close · Esc" : "Back · Esc"; onClicked: root.back() }
    }
    Text { Layout.fillWidth: true; visible: text.length > 0; text: root.controller.error || root.controller.inventory.error || ""; color: root.theme.urgent; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontSmall; textFormat: Text.PlainText; wrapMode: Text.Wrap }
    ColumnLayout {
      visible: root.page === Ui.ExtensionManagementPage.List
      Layout.fillWidth: true; Layout.fillHeight: true
      SettingsField {
        id: search
        theme: root.theme
        Layout.fillWidth: true; placeholderText: "Search installed extensions"
        Accessible.name: "Search installed extensions"
        onTextEdited: root.controller.load(text, 0)
        Keys.onDownPressed: { packages.currentIndex = 0; packages.forceActiveFocus() }
        Keys.onReturnPressed: if (packages.count) root.inspect(packages.model[Math.max(0, packages.currentIndex)])
        Keys.onEscapePressed: { if (text) { text = ""; root.controller.load("", 0) } else root.back() }
        KeyNavigation.backtab: backButton
      }
      ListView {
        id: packages
        Layout.fillWidth: true; Layout.fillHeight: true; clip: true; spacing: 4
        model: root.controller.inventory.packages || []
        keyNavigationEnabled: true
        Keys.forwardTo: [search]
        Keys.onReturnPressed: if (currentIndex >= 0) root.inspect(model[currentIndex])
        Keys.onEnterPressed: if (currentIndex >= 0) root.inspect(model[currentIndex])
        delegate: ItemDelegate {
          id: packageRow
          required property var modelData
          required property int index
          width: packages.width
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontBody
          text: modelData.name + " · " + modelData.version + (modelData.removed ? " · Removed (data retained)" : modelData.enabled ? " · Enabled" : " · Disabled")
          highlighted: packages.currentIndex === index
          onClicked: root.inspect(modelData)
          // Package names come from their manifests; show them as written.
          contentItem: Text {
            text: packageRow.text
            textFormat: Text.PlainText
            font: packageRow.font
            color: packageRow.highlighted ? packageRow.palette.highlightedText : packageRow.palette.text
            verticalAlignment: Text.AlignVCenter
            elide: Text.ElideRight
          }
        }
      }
      RowLayout {
        DeckButton { theme: root.theme; text: "Install / update archive"; onClicked: { root.page = Ui.ExtensionManagementPage.Archive; root.focusLater(archive) } }
        DeckButton { theme: root.theme; text: root.controller.inventory.enabled ? "Turn extensions off" : "Turn extensions on"; enabled: !root.controller.busy; onClicked: root.controller.change({ action: root.controller.inventory.enabled ? "disable" : "enable" }) }
      }
      RowLayout {
        DeckButton { theme: root.theme; text: "Previous"; focusPolicy: Qt.TabFocus; enabled: root.controller.inventory.offset > 0; onClicked: root.controller.load(search.text, Math.max(0, root.controller.inventory.offset - 32)) }
        DeckButton { theme: root.theme; text: "Next"; focusPolicy: Qt.TabFocus; enabled: root.controller.inventory.offset + 32 < root.controller.inventory.total; onClicked: root.controller.load(search.text, root.controller.inventory.offset + 32) }
        Text { text: root.controller.inventory.total + " packages"; color: root.theme.textMuted; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontSmall }
      }
    }
    ColumnLayout {
      visible: root.page === Ui.ExtensionManagementPage.Archive
      Layout.fillWidth: true; Layout.fillHeight: true
      SettingsField { id: archive; theme: root.theme; Layout.fillWidth: true; placeholderText: "Absolute path to a .cdext archive"; Accessible.name: "Extension archive path"; onAccepted: inspectArchive.clicked() }
      DeckButton { id: inspectArchive; theme: root.theme; text: "Inspect archive"; enabled: !root.controller.busy && archive.text.startsWith("/"); onClicked: { root.inspectedPath = archive.text; root.controller.change({ action: Protocol.ExtensionManagementActions.Inspect, archivePath: root.inspectedPath }) } }
      Item { Layout.fillHeight: true }
    }
    ColumnLayout {
      visible: root.page === Ui.ExtensionManagementPage.Detail || root.page === Ui.ExtensionManagementPage.ArchiveReview || root.page === Ui.ExtensionManagementPage.Grants
      Layout.fillWidth: true; Layout.fillHeight: true
      Text { text: (root.manifest.name || "") + " · " + (root.manifest.version || ""); textFormat: Text.PlainText; color: root.theme.text; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontHeading }
      Text { Layout.fillWidth: true; text: root.manifest.description || ""; textFormat: Text.PlainText; wrapMode: Text.Wrap; color: root.theme.text; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody }
      Text { Layout.fillWidth: true; text: root.manifest.id || ""; textFormat: Text.PlainText; color: root.theme.textMuted; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontSmall }
      Text { visible: root.page === Ui.ExtensionManagementPage.ArchiveReview; Layout.fillWidth: true; text: "Review the requested permissions below. Installation does not run the extension. Enable it afterward."; wrapMode: Text.Wrap; color: root.theme.text; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody }
      ScrollView {
        Layout.fillWidth: true; Layout.fillHeight: true; contentWidth: availableWidth
        ColumnLayout {
          width: parent.width
          Repeater {
            model: Object.keys(root.manifest.capabilities || {})
            CheckBox {
              id: capabilityGrant

              required property string modelData
              readonly property var capability: root.manifest.capabilities
                && root.manifest.capabilities[capabilityGrant.modelData]
                  ? root.manifest.capabilities[capabilityGrant.modelData]
                  : ({})
              Layout.fillWidth: true
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontBody
              text: capabilityGrant.modelData + " — " + (capabilityGrant.capability.reason || "")
              // The reason comes from the manifest: markup must not restyle or
              // hide what is being granted.
              contentItem: Text {
                leftPadding: capabilityGrant.indicator && !capabilityGrant.mirrored ? capabilityGrant.indicator.width + capabilityGrant.spacing : 0
                rightPadding: capabilityGrant.indicator && capabilityGrant.mirrored ? capabilityGrant.indicator.width + capabilityGrant.spacing : 0
                text: capabilityGrant.text
                textFormat: Text.PlainText
                font: capabilityGrant.font
                color: capabilityGrant.palette.windowText
                verticalAlignment: Text.AlignVCenter
                wrapMode: Text.Wrap
              }
              checked: root.grants.indexOf(capabilityGrant.modelData) >= 0
              enabled: root.page === Ui.ExtensionManagementPage.ArchiveReview || root.page === Ui.ExtensionManagementPage.Grants
              onToggled: root.grants = checked
                ? root.grants.concat([capabilityGrant.modelData])
                : root.grants.filter(value => value !== capabilityGrant.modelData)
            }
          }
        }
      }
      DeckButton { id: install; theme: root.theme; kind: Ui.ButtonVariant.Primary; visible: root.page === Ui.ExtensionManagementPage.ArchiveReview; text: "Install reviewed archive"; enabled: !root.controller.busy; onClicked: root.confirm({ action: Protocol.ExtensionManagementActions.Install, archivePath: root.inspectedPath, digest: root.archiveDigest, grants: root.grants }, "Install " + root.manifest.name + " with the selected permissions?", this) }
      DeckButton { theme: root.theme; kind: Ui.ButtonVariant.Primary; visible: root.page === Ui.ExtensionManagementPage.Grants; text: "Save reviewed permissions"; enabled: !root.controller.busy; onClicked: root.confirm({ action: Protocol.ExtensionManagementActions.Grants, key: root.selected.key, grants: root.grants }, "Apply these permissions? Active commands will close.", this) }
      RowLayout {
        visible: root.page === Ui.ExtensionManagementPage.Detail
        DeckButton { id: configure; theme: root.theme; text: "Configure"; enabled: !root.controller.busy && root.selected && !root.selected.removed; onClicked: root.configureSettings() }
        DeckButton { id: logs; theme: root.theme; text: "Logs"; enabled: !root.controller.busy; onClicked: root.loadLogs(false) }
        DeckButton { id: connectionsButton; theme: root.theme; text: "Connections"; enabled: !root.controller.busy && root.selected && root.selected.enabled; onClicked: root.changeConnection({ action: Protocol.ExtensionManagementActions.Connections, key: root.selected.key }) }
        DeckButton {
          id: globalSearch
          theme: root.theme
          visible: (root.manifest.searchProviders || []).length > 0
          enabled: !root.controller.busy && root.selected && !root.selected.removed && root.selected.origin !== "development"
          text: root.selected && root.selected.globalSearchEnabled ? "Stop sharing launcher queries…" : "Allow launcher queries…"
          onClicked: root.confirm({ action: Protocol.ExtensionManagementActions.GlobalSearch, key: root.selected.key, globalSearchEnabled: !root.selected.globalSearchEnabled },
            root.selected.globalSearchEnabled ? "Stop sending ordinary launcher queries to this extension?" : "Allow this extension to receive ordinary launcher queries? Search runs locally with a read-only cache and cannot use connections, credentials, storage writes or desktop effects. Scoped search works without this permission.", this)
        }
        DeckButton { theme: root.theme; text: "Permissions"; enabled: !root.controller.busy && root.selected && !root.selected.removed; onClicked: { root.page = Ui.ExtensionManagementPage.Grants; root.focusLater(backButton) } }
        DeckButton { theme: root.theme; text: root.selected && root.selected.enabled ? "Disable" : "Enable"; enabled: !root.controller.busy && root.selected && !root.selected.removed; onClicked: root.controller.change({ action: root.selected.enabled ? "disable" : "enable", key: root.selected.key }) }
        DeckButton { id: remove; theme: root.theme; kind: Ui.ButtonVariant.Danger; text: "Remove…"; enabled: !root.controller.busy && root.selected; onClicked: root.confirm({ action: Protocol.ExtensionManagementActions.Remove, key: root.selected.key }, "Remove this extension? Settings and drafts are retained unless you choose to erase them.", this) }
      }
      RowLayout {
        visible: root.page === Ui.ExtensionManagementPage.Detail && root.selected && root.selected.origin === "development" && !root.selected.removed
        SettingsChoice { id: debugCommand; theme: root.theme; options: (root.manifest.commands || []).map(command => ({ id: command.id, title: command.title })); Accessible.name: "Command to debug"; Layout.fillWidth: true }
        DeckButton { theme: root.theme; text: "Debug for 5 minutes…"; enabled: !root.controller.busy && root.selected && root.selected.enabled; onClicked: root.confirm({ action: Protocol.ExtensionManagementActions.Debug, key: root.selected.key, commandId: debugCommand.currentValue, digest: root.selected.digest, debugSeconds: 300 }, "Allow native debugging of this development build for 5 minutes? Foreground deadlines pause; permissions and resource limits stay enforced. Open the selected command, then connect GDB. Closing the session ends the lease.", this) }
      }
      RowLayout {
        visible: root.page === Ui.ExtensionManagementPage.Detail && (root.manifest.searchProviders || []).length > 0
        SettingsChoice { id: searchProvider; theme: root.theme; options: (root.manifest.searchProviders || []).map(provider => ({ id: provider.id, title: provider.title })); Accessible.name: "Extension search provider"; Layout.fillWidth: true }
        DeckButton { id: scopedSearch; theme: root.theme; text: "Search this extension"; enabled: !root.controller.busy && root.selected && root.selected.enabled; onClicked: root.scopedSearchRequested("extension-search:" + root.selected.key + ":" + searchProvider.currentValue, root.selected.name + " · " + searchProvider.currentText) }
      }
      RowLayout {
        visible: root.page === Ui.ExtensionManagementPage.Detail && root.selected && !root.selected.removed
        SettingsChoice { id: versions; theme: root.theme; options: root.selected ? (root.selected.retainedVersions || []).filter(value => value.digest !== root.selected.digest).map(value => ({ id: value.digest, title: value.version })) : []; Accessible.name: "Previous version"; Layout.fillWidth: true }
        DeckButton { theme: root.theme; text: "Roll back…"; enabled: !root.controller.busy && versions.count > 0; onClicked: root.confirm({ action: Protocol.ExtensionManagementActions.Rollback, key: root.selected.key, digest: versions.currentValue }, "Restore version " + versions.currentText + "? Settings and drafts stay unchanged; active commands will close.", this) }
      }
    }
    ExtensionConnectionsView {
      id: connectionsView
      visible: root.page === Ui.ExtensionManagementPage.Connections
      opened: root.opened && (visible || root.page === Ui.ExtensionManagementPage.Confirm && root.previousPage === Ui.ExtensionManagementPage.Connections)
      Layout.fillWidth: true; Layout.fillHeight: true
      theme: root.theme; busy: root.controller.busy
      onBackRequested: root.back()
      onConfigureRequested: setup => root.confirm({ action: Protocol.ExtensionManagementActions.ConnectionSet, key: root.selected.key, connection: setup }, "Allow " + setup.name + " to use " + (setup.kind === Protocol.ExtensionConnectionKinds.Files ? setup.path : setup.origin + setup.route + " · " + setup.methods.join(", ") + (setup.privateNetwork ? " · private network" : " · public HTTPS") + " · " + setup.credentialKind) + "? Existing access under this name will be revoked.", connectionsView.saveControl)
      onDisconnectRequested: (name, remote) => root.confirm({ action: Protocol.ExtensionManagementActions.ConnectionRemove, key: root.selected.key, connectionName: name, remoteRevoke: remote }, "Disconnect " + name + (remote ? " and attempt remote OAuth revocation?" : " locally? Remote credentials may remain valid."), connectionsView.searchControl)
      onLoginRequested: name => root.confirm({ action: Protocol.ExtensionManagementActions.ConnectionLogin, key: root.selected.key, connectionName: name }, "Open the configured service login for " + name + " in your browser?", connectionsView.searchControl)
      onResetEffectsRequested: root.confirm({ action: Protocol.ExtensionManagementActions.EffectsReset, key: root.selected.key }, "Clear this package's remote-write history? Previously used request keys could be sent again. Review earlier writes before confirming.", connectionsView.resetEffectsControl)
    }
    ExtensionDiagnosticsView {
      id: diagnostics
      visible: root.page === Ui.ExtensionManagementPage.Logs
      opened: visible && root.opened
      Layout.fillWidth: true; Layout.fillHeight: true
      theme: root.theme; busy: root.controller.busy
      onRefreshRequested: includeStderr => root.loadLogs(includeStderr)
      onStopRequested: root.changeDiagnostics({ action: Protocol.ExtensionManagementActions.DebugStop, key: root.selected.key })
      onBackRequested: root.back()
    }
    ExtensionFormView {
      id: settings
      Layout.fillWidth: true; Layout.fillHeight: true
      theme: root.theme; opened: root.page === Ui.ExtensionManagementPage.Settings && root.opened; busy: root.controller.busy; externalError: root.controller.error
      onBackRequested: root.back()
      onSubmitted: (revision, handle, formRevision, values) => {
        const converted = Object.assign({}, values)
        for (const field of root.manifest.preferences || []) {
          if (field.type === root.preferenceTypes.Number) {
            if (!String(values[field.id]).trim() && !field.required) {
              delete converted[field.id]
              continue
            }
            const number = Number(values[field.id])
            if (!String(values[field.id]).trim() || !isFinite(number)) { root.controller.error = field.title + " must be a number."; return }
            converted[field.id] = number
          }
        }
        root.controller.change({ action: Protocol.ExtensionManagementActions.Configure, key: root.selected.key, settings: converted, expectedRevision: root.inspectedRevision })
      }
    }
    ColumnLayout {
      visible: root.page === Ui.ExtensionManagementPage.Confirm; Layout.fillWidth: true; Layout.fillHeight: true
      Text { Layout.fillWidth: true; text: root.confirmationText; textFormat: Text.PlainText; wrapMode: Text.Wrap; color: root.theme.text; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody }
      CheckBox { id: erase; font.family: root.theme.fontFamily; font.pixelSize: root.theme.fontBody; visible: root.confirmation && root.confirmation.action === Protocol.ExtensionManagementActions.Remove; text: "Permanently erase settings and drafts"; onVisibleChanged: checked = false; Keys.onTabPressed: approve.forceActiveFocus(); Keys.onBacktabPressed: cancel.forceActiveFocus() }
      RowLayout {
        DeckButton { id: cancel; theme: root.theme; text: "Cancel · Esc"; onClicked: root.back(); Keys.onTabPressed: if (erase.visible) erase.forceActiveFocus(); else approve.forceActiveFocus(); Keys.onBacktabPressed: approve.forceActiveFocus() }
        DeckButton { id: approve; theme: root.theme; kind: Ui.ButtonVariant.Primary; text: "Confirm"; enabled: !root.controller.busy && root.confirmation !== null; onClicked: root.approveChange(); Keys.onTabPressed: cancel.forceActiveFocus(); Keys.onBacktabPressed: if (erase.visible) erase.forceActiveFocus(); else cancel.forceActiveFocus() }
      }
      Item { Layout.fillHeight: true }
    }
  }
}
