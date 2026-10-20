pragma ComponentBehavior: Bound
import QtQuick
import "ListKeys.js" as ListKeys
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import QtQuick.Controls
import QtQuick.Layouts
import "ClipboardFormat.js" as Format

// Clipboard history browser. Search keeps typing focus; the entry list and the
// preview sit side by side with the selection marked unmistakably; actions for
// the selected entry run along the bottom with their shortcuts, and
// history-wide settings sit in the header.
Rectangle {
  id: surface
  required property var theme
  required property var controller
  property Item returnFocus: null
  property Item openInvoker: null
  property double nowSeconds: Math.floor(Date.now() / 1000)
  readonly property alias searchControl: search
  readonly property alias listControl: rows
  readonly property alias moreControl: more
  readonly property alias refreshControl: refresh
  readonly property alias filterControl: pinnedFilter
  readonly property alias kindControl: kind
  readonly property alias backControl: back
  readonly property alias clearControl: clear
  readonly property alias limitsControl: limits
  readonly property alias limitsDialogControl: limitsDialog
  readonly property alias dialogControl: dialog
  readonly property alias previewPaneControl: preview
  readonly property alias emptyStateControl: emptyState
  readonly property alias toastControl: toast
  readonly property Item copyControl: actionBar.copyControl
  readonly property Item pinControl: actionBar.pinControl
  readonly property Item sequenceControl: actionBar.sequenceControl
  readonly property Item openControl: actionBar.openControl
  readonly property Item keywordsControl: actionBar.keywordsControl
  readonly property Item deleteControl: actionBar.deleteControl
  readonly property Item captureControl: captureStatus.captureControl
  readonly property Item captureLabelControl: captureStatus.labelControl
  readonly property Item previewControl: preview.textControl
  readonly property Item previewImageControl: preview.imageControl
  readonly property Item previewHighlightControl: preview.highlightControl
  readonly property bool settingsTabFence: true
  readonly property var policy: surface.controller.settings && surface.controller.settings.policy ? surface.controller.settings.policy : null
  readonly property bool hasEntries: surface.controller.entries.length > 0
  // Pinned entries sort first; label the two runs only when the page is split that way.
  readonly property bool showSections: {
    const list = surface.controller.entries
    if (surface.controller.pinnedOnly || list.length === 0 || !list[0].pinned) return false
    let recent = false
    for (let index = 0; index < list.length; index++) {
      if (!list[index].pinned) recent = true
      else if (recent) return false
    }
    return true
  }
  visible: controller.opened
  // Covers the rounded palette and the results beneath, so it repeats the
  // palette frame and stays opaque.
  color: theme.popupBackground
  radius: theme.radius
  border.width: 1
  border.color: theme.border
  Accessible.name: "Clipboard history"
  Accessible.role: Accessible.Pane

  function containsFocus(item) {
    while (item) {
      if (item === surface) return true
      item = item.parent
    }
    return false
  }
  function cycleFocus(forward) {
    const current = Window.window ? Window.window.activeFocusItem : null
    const next = current ? current.nextItemInFocusChain(forward) : null
    if (next && containsFocus(next)) next.forceActiveFocus()
    else search.forceActiveFocus()
  }
  // A control that hides or disables while focused (Load more, match steps,
  // empty-state actions) either keeps focus on an unusable item or drops it
  // outside the surface; hand it back to search. The browser covers the
  // launcher, so only its own dialogs may hold focus outside it.
  function recoverFocus() {
    const window = surface.Window.window
    if (!surface.visible || !window || dialog.visible || limitsDialog.visible) return

    const focused = window.activeFocusItem
    if (!surface.containsFocus(focused) || !focused.visible || !focused.enabled) search.forceActiveFocus()
  }
  // Those controls hide in response to controller state, so re-check after each change.
  Connections {
    target: surface.controller
    function onEntriesChanged() { Qt.callLater(surface.recoverFocus) }
    function onCursorChanged() { Qt.callLater(surface.recoverFocus) }
    function onPreviewChanged() { Qt.callLater(surface.recoverFocus) }
    function onSettingsChanged() { Qt.callLater(surface.recoverFocus) }
    function onRequestedQueryChanged() { Qt.callLater(surface.recoverFocus) }
    function onPageLoadedChanged() { Qt.callLater(surface.recoverFocus) }
  }
  Connections {
    target: surface.Window.window
    function onActiveFocusItemChanged() { if (surface.visible) Qt.callLater(surface.recoverFocus) }
  }
  function leave() {
    controller.close()
    if (returnFocus) returnFocus.forceActiveFocus()
  }
  function pageStep() {
    return Math.max(1, Math.floor(rows.height / Math.round(54 * theme.scale)) - 1)
  }
  // Moving down from the last loaded entry fetches the next page.
  function navigate(delta) {
    const count = controller.entries.length
    const current = controller.selectedIndex
    if (count === 0) return
    if (delta > 0 && current === count - 1) {
      if (controller.cursor.length > 0 && !controller.loading) controller.refresh(true)
      return
    }

    const next = Math.max(0, Math.min(count - 1, current + delta))
    if (next !== current) controller.select(next)
  }
  function selectEdge(last) {
    if (!hasEntries) return
    const next = last ? controller.entries.length - 1 : 0
    if (next !== controller.selectedIndex) controller.select(next)
  }
  function pinSelected() {
    if (!actionBar.pinControl.enabled) return

    controller.mutate(controller.selected.pinned ? Protocol.ClipboardActionIds.Unpin : Protocol.ClipboardActionIds.Pin)
  }
  function stepKind(delta) {
    if (kind.activeFocus) kind.choose(kind.selectedIndex + delta)
    else controller.cycleKind(delta)
  }
  function toggleCapture(invoker) {
    if (!policy) return
    if (policy.consent === "None") dialog.show(Ui.ClipboardDialogPurpose.Enable, invoker)
    else controller.capture(policy.consent, !policy.paused, false)
  }

  onVisibleChanged: {
    if (visible) {
      nowSeconds = Math.floor(Date.now() / 1000)
      search.text = controller.query
      search.forceActiveFocus()
      Qt.callLater(function() {
        const focused = surface.Window.window ? surface.Window.window.activeFocusItem : null
        if (surface.visible && !dialog.opened && !limitsDialog.opened && !surface.containsFocus(focused)) search.forceActiveFocus()
      })
    } else { dialog.close(); limitsDialog.close() }
  }
  Keys.priority: Keys.BeforeItem
  function handleKey(event) {
    if (dialog.opened || limitsDialog.opened) return
    const browsing = search.activeFocus || rows.activeFocus
    const plain = event.modifiers === Qt.NoModifier
    const control = event.modifiers === Qt.ControlModifier
    if (rows.activeFocus && event.text.length > 0 && event.text.charCodeAt(0) >= 32 && event.text.charCodeAt(0) !== 127
        && !(event.modifiers & (Qt.ControlModifier | Qt.AltModifier | Qt.MetaModifier))) search.forceActiveFocus()

    let handled = true
    if ((event.key === Qt.Key_Return || event.key === Qt.Key_Enter) && browsing && plain) {
      controller.copySelected()
    } else if (event.key === Qt.Key_Escape) {
      if (search.text.length) { search.clear(); controller.query = ""; controller.refresh(false) }
      else if (controller.pinnedOnly || controller.kindFilter) { controller.pinnedOnly = false; controller.kindFilter = ""; controller.refresh(false) }
      else surface.leave()
    } else if ((event.key === Qt.Key_Tab || event.key === Qt.Key_Backtab) && (event.modifiers & Qt.ControlModifier)) {
      surface.stepKind(event.key === Qt.Key_Tab && !(event.modifiers & Qt.ShiftModifier) ? 1 : -1)
    } else if (event.key === Qt.Key_Tab || event.key === Qt.Key_Backtab) {
      surface.cycleFocus(event.key === Qt.Key_Tab && !(event.modifiers & Qt.ShiftModifier))
    } else if (control && event.key === Qt.Key_P) {
      surface.pinSelected()
    } else if (control && event.key === Qt.Key_S && actionBar.sequenceControl.enabled) {
      controller.startSequence()
    } else if (control && event.key === Qt.Key_E && actionBar.keywordsControl.enabled) {
      dialog.show(Ui.ClipboardDialogPurpose.Keywords, search)
    } else if (control && event.key === Qt.Key_K && actionBar.openControl.enabled) {
      surface.openInvoker = search; controller.prepareOpen()
    } else if (event.modifiers === Qt.ShiftModifier && event.key === Qt.Key_Delete
        && actionBar.deleteControl.enabled && (!search.activeFocus || search.selectedText.length === 0)) {
      controller.mutate(Protocol.ClipboardActionIds.Delete)
    } else if (event.key === Qt.Key_G && (control || event.modifiers === (Qt.ControlModifier | Qt.ShiftModifier))) {
      preview.stepMatch(control ? 1 : -1)
    } else handled = browsing && ListKeys.handle(event, surface.entryKeys, surface.pageStep(), search.activeFocus)
    if (handled) event.accepted = true
  }
  // Ctrl+P pins, so the entry list answers the arrow and page keys only.
  readonly property var entryKeys: ({
    step: delta => surface.navigate(delta),
    first: () => surface.selectEdge(false),
    last: () => surface.selectEdge(true),
    controlLetters: false
  })
  Keys.onPressed: event => surface.handleKey(event)

  MouseArea { anchors.fill: parent; acceptedButtons: Qt.AllButtons }

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: surface.theme.panelPadding
    spacing: surface.theme.gap

    RowLayout {
      Layout.fillWidth: true
      spacing: Math.round(4 * surface.theme.scale)

      Text {
        text: "Clipboard history"
        color: surface.theme.text
        font.family: surface.theme.fontFamily
        font.pixelSize: surface.theme.fontHeading
        font.weight: Font.DemiBold
        Accessible.role: Accessible.Heading
        Accessible.name: text
      }
      Item { Layout.fillWidth: true }
      ClipboardCaptureStatus {
        id: captureStatus
        Layout.fillWidth: true
        Layout.maximumWidth: implicitWidth
        theme: surface.theme
        controller: surface.controller
        onCaptureRequested: invoker => surface.toggleCapture(invoker || search)
      }
      ClipboardActionButton {
        id: migrationReview
        theme: surface.theme
        tone: "primary"
        visible: Boolean(surface.controller.settings && surface.controller.settings.migration && surface.controller.settings.migration.required)
        text: "Review migration…"
        onClicked: dialog.show(Ui.ClipboardDialogPurpose.Migration, migrationReview.activeFocus ? migrationReview : search)
      }
      ClipboardActionButton {
        id: limits
        theme: surface.theme
        text: "Limits…"
        enabled: surface.controller.settings !== null
        toolTipText: "Entry count, retention, and storage limits"
        onClicked: limitsDialog.show(limits.activeFocus ? limits : search)
      }
      ClipboardActionButton {
        id: clear
        theme: surface.theme
        text: "Clear history…"
        enabled: surface.controller.settings !== null
        onClicked: dialog.show(Ui.ClipboardDialogPurpose.Clear, clear.activeFocus ? clear : search)
      }
      ClipboardActionButton {
        id: back
        theme: surface.theme
        text: "Back"
        keyHint: "Esc"
        onClicked: surface.leave()
      }
    }

    Rectangle {
      Layout.fillWidth: true
      implicitHeight: surface.theme.inputHeight
      radius: surface.theme.radiusSmall
      color: surface.theme.surface
      border.width: search.activeFocus ? 2 : 1
      border.color: search.activeFocus ? surface.theme.focusRing : surface.theme.separator

      RowLayout {
        anchors.fill: parent
        anchors.leftMargin: Math.round(14 * surface.theme.scale)
        anchors.rightMargin: Math.round(14 * surface.theme.scale)
        spacing: Math.round(10 * surface.theme.scale)

        Text {
          text: "⌕"
          color: search.activeFocus ? surface.theme.accent : surface.theme.textMuted
          font.family: surface.theme.fontFamily
          font.pixelSize: Math.round(surface.theme.fontHeading * 1.25)
        }
        TextField {
          id: search
          Layout.fillWidth: true
          placeholderText: "Search clipboard history"
          placeholderTextColor: surface.theme.textFaint
          Accessible.name: "Search clipboard history"
          selectByMouse: true
          color: surface.theme.text
          selectionColor: surface.theme.withAlpha(surface.theme.accent, 0.4)
          selectedTextColor: surface.theme.text
          font.family: surface.theme.fontFamily
          font.pixelSize: surface.theme.fontTitle
          background: null
          onTextEdited: { surface.controller.query = text; debounce.restart() }
          Keys.onPressed: event => surface.handleKey(event)
        }
        Text {
          text: surface.controller.pageLoaded
            ? Format.entryCountLabel(surface.controller.entries.length, surface.controller.cursor.length > 0) : ""
          color: surface.theme.textFaint
          font.family: surface.theme.fontFamily
          font.pixelSize: surface.theme.fontSmall
        }
      }

      SearchLoadingIndicator {
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        anchors.leftMargin: surface.theme.radiusSmall
        anchors.rightMargin: surface.theme.radiusSmall
        anchors.bottomMargin: 2
        theme: surface.theme
        loading: surface.controller.loading
      }
    }

    RowLayout {
      Layout.fillWidth: true
      spacing: surface.theme.gap

      ClipboardKindFilter {
        id: kind
        theme: surface.theme
        kinds: surface.controller.kindFilters
        selectedKind: surface.controller.kindFilter
        labelFor: value => surface.controller.kindLabel(value)
        onKindRequested: value => surface.controller.setKind(value)
      }
      ClipboardActionButton {
        id: pinnedFilter
        theme: surface.theme
        text: surface.controller.pinnedOnly ? "✓ Pinned only" : "Pinned only"
        latched: surface.controller.pinnedOnly
        outlined: true
        toolTipText: "Show only pinned entries"
        Accessible.name: "Pinned only"
        Accessible.checkable: true
        Accessible.checked: surface.controller.pinnedOnly
        onClicked: surface.controller.togglePinnedOnly()
      }
      Item { Layout.fillWidth: true }
      Text {
        visible: surface.controller.newerAvailable
        text: "● New copies"
        color: surface.theme.accent
        font.family: surface.theme.fontFamily
        font.pixelSize: surface.theme.fontSmall
      }
      ClipboardActionButton {
        id: refresh
        theme: surface.theme
        text: "Refresh"
        outlined: true
        latched: surface.controller.newerAvailable
        onClicked: surface.controller.refresh(false)
      }
    }

    Item {
      Layout.fillWidth: true
      Layout.fillHeight: true

      Item {
        id: listPane
        visible: surface.hasEntries
        anchors.left: parent.left
        anchors.top: parent.top
        anchors.bottom: parent.bottom
        width: Math.round(parent.width * 0.42)

        Rectangle {
          anchors.fill: parent
          visible: rows.activeFocus
          radius: surface.theme.radiusSmall + 3
          color: "transparent"
          border.width: 2
          border.color: surface.theme.focusRing
        }

        ColumnLayout {
          anchors.fill: parent
          anchors.margins: Math.round(4 * surface.theme.scale)
          spacing: Math.round(4 * surface.theme.scale)

          ListView {
            id: rows
            Layout.fillWidth: true
            Layout.fillHeight: true
            clip: true
            spacing: Math.max(1, Math.round(2 * surface.theme.scale))
            model: surface.controller.entries
            currentIndex: surface.controller.selectedIndex
            activeFocusOnTab: true
            keyNavigationEnabled: false
            highlightFollowsCurrentItem: false
            boundsBehavior: Flickable.StopAtBounds
            Accessible.role: Accessible.List
            Accessible.name: "Clipboard entries"
            Keys.forwardTo: [search]
            ScrollBar.vertical: ScrollBar { id: rowsScrollBar; policy: ScrollBar.AsNeeded }
            delegate: ClipboardEntryRow {
              width: rows.width - (rowsScrollBar.visible ? rowsScrollBar.width + 2 : 0)
              theme: surface.theme
              selected: index === surface.controller.selectedIndex
              listFocused: rows.activeFocus
              nowSeconds: surface.nowSeconds
              description: surface.controller.describe(modelData)
              // Delegates can briefly outlive a shrinking page, so the previous entry may be gone.
              sectionVisible: surface.showSections && (index === 0 || Boolean(surface.controller.entries[index - 1])
                && surface.controller.entries[index - 1].pinned !== modelData.pinned)
              onClicked: {
                if (index !== surface.controller.selectedIndex) surface.controller.select(index)
                search.forceActiveFocus()
              }
              onActivated: {
                surface.controller.copySelected()
                search.forceActiveFocus()
              }
            }
          }
          ClipboardActionButton {
            id: more
            Layout.fillWidth: true
            // Stays enabled while a page loads, so keyboard focus is not dropped mid-request.
            visible: surface.controller.cursor.length > 0
            theme: surface.theme
            outlined: true
            text: surface.controller.pending.more ? "Loading…" : "Load more"
            toolTipText: "Down on the last entry also loads more"
            onClicked: {
              if (!surface.controller.loading) surface.controller.refresh(true)
              if (!more.activeFocus) search.forceActiveFocus()
            }
          }
        }
      }

      ClipboardPreviewPane {
        id: preview
        visible: surface.hasEntries
        anchors.left: listPane.right
        anchors.leftMargin: surface.theme.gap
        anchors.right: parent.right
        anchors.top: parent.top
        anchors.bottom: parent.bottom
        theme: surface.theme
        controller: surface.controller
        nowSeconds: surface.nowSeconds
      }

      ClipboardEmptyState {
        id: emptyState
        anchors.fill: parent
        visible: !surface.hasEntries
        theme: surface.theme
        controller: surface.controller
        onEnableRequested: invoker => dialog.show(Ui.ClipboardDialogPurpose.Enable, invoker || search)
        onResumeRequested: invoker => surface.toggleCapture(invoker || search)
      }

      DeckToast {
        id: toast
        z: 2
        anchors.horizontalCenter: parent.horizontalCenter
        anchors.bottom: parent.bottom
        anchors.bottomMargin: surface.theme.gap
        theme: surface.theme
      }
    }

    Rectangle {
      Layout.fillWidth: true
      visible: surface.controller.error.length > 0
      implicitHeight: errorLabel.implicitHeight + Math.round(14 * surface.theme.scale)
      radius: surface.theme.radiusSmall
      color: surface.theme.withAlpha(surface.theme.urgent, 0.12)
      border.width: 1
      border.color: surface.theme.withAlpha(surface.theme.urgent, 0.45)

      Text {
        id: errorLabel
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.verticalCenter: parent.verticalCenter
        anchors.leftMargin: Math.round(12 * surface.theme.scale)
        anchors.rightMargin: Math.round(12 * surface.theme.scale)
        text: surface.controller.error
        textFormat: Text.PlainText
        wrapMode: Text.Wrap
        color: surface.theme.urgent
        font.family: surface.theme.fontFamily
        font.pixelSize: surface.theme.fontSmall
        Accessible.role: Accessible.AlertMessage
        Accessible.name: text
      }
    }

    Rectangle {
      Layout.fillWidth: true
      Layout.preferredHeight: 1
      color: surface.theme.separator
    }

    ClipboardActionBar {
      id: actionBar
      Layout.fillWidth: true
      theme: surface.theme
      controller: surface.controller
      onCopyRequested: invoker => {
        surface.controller.copySelected()
        if (!invoker) search.forceActiveFocus()
      }
      onPinRequested: surface.pinSelected()
      onSequenceRequested: surface.controller.startSequence()
      onOpenRequested: invoker => {
        surface.openInvoker = invoker || search
        surface.controller.prepareOpen()
      }
      onKeywordsRequested: invoker => dialog.show(Ui.ClipboardDialogPurpose.Keywords, invoker || search)
      onDeleteRequested: surface.controller.mutate(Protocol.ClipboardActionIds.Delete)
    }
  }
  Timer { id: debounce; interval: 80; onTriggered: surface.controller.refresh(false) }
  Timer {
    interval: 30000
    repeat: true
    running: surface.visible
    onTriggered: surface.nowSeconds = Math.floor(Date.now() / 1000)
  }
  Connections {
    target: surface.controller
    function onOpenPrepared() { dialog.show(Ui.ClipboardDialogPurpose.Open, surface.openInvoker || search) }
    function onNoticeChanged() { if (surface.visible && surface.controller.notice.length > 0) toast.show(surface.controller.notice) }
    function onSelectedIndexChanged() {
      if (surface.controller.selectedIndex >= 0) rows.positionViewAtIndex(surface.controller.selectedIndex, ListView.Contain)
    }
  }
  ClipboardBrowserDialogs { id: dialog; theme: surface.theme; controller: surface.controller }
  ClipboardLimitsDialog { id: limitsDialog; theme: surface.theme; controller: surface.controller }
}
