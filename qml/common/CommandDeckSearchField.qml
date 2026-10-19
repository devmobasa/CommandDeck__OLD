pragma ComponentBehavior: Bound
import QtQuick
import "ListKeys.js" as ListKeys
import "ShortcutSlots.js" as ShortcutSlots
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
  id: root

  required property var theme
  required property SearchNavigationController controller
  property bool focusRequestsEnabled: true

  Connections {
    target: root.controller
    function onFocusRevisionChanged() {
      if (!root.surfaceVisible || !root.focusRequestsEnabled) return
      root.setInputText(root.controller.queryText)
      Qt.callLater(function() {
        if (root.surfaceVisible && root.focusRequestsEnabled) searchInput.forceActiveFocus()
      })
    }
  }
  property bool surfaceVisible: false
  // True while Ctrl is down in the field, for badges that show only then.
  readonly property bool controlHeld: searchInput.controlDown && searchInput.activeFocus
  property bool loading: false
  property Item additionalTabStop: null
  property Item precedingTabStop: additionalTabStop
  readonly property alias inputControl: searchInput
  readonly property alias settingsControl: settingsButton
  readonly property var filterChipControl: filterRepeater.count > 0
    ? filterRepeater.itemAt(0)
    : null
  readonly property int filterChipCount: filterRepeater.count
  function filterChipAt(index) {
    return index >= 0 && index < filterRepeater.count ? filterRepeater.itemAt(index) : null
  }
  signal searchEdited()
  signal actionsRequested()
  // Up with nothing to move to on an empty or recalled query; restart is true
  // when the query is empty, so the newest entry comes first again.
  signal historyRecallRequested(bool restart)
  // Ctrl+= (or Ctrl++) is 1, Ctrl+- is -1, Ctrl+0 is 0 for the automatic size.
  signal zoomRequested(int direction)

  function zoomDirection(key, modifiers) {
    if ((modifiers & ~(Qt.KeypadModifier | Qt.ShiftModifier)) !== Qt.ControlModifier) return null
    if (key === Qt.Key_Equal || key === Qt.Key_Plus) return 1
    if (key === Qt.Key_Minus) return -1
    if (key === Qt.Key_0 && (modifiers & Qt.ShiftModifier) === 0) return 0
    return null
  }
  signal shortcutRequested(string actionId)
  // Ctrl+Z restores the last hidden item while its undo strip is visible;
  // otherwise the field keeps its ordinary text undo.
  property bool undoAvailable: false
  signal undoRequested()
  property var directActions: []

  // Ctrl+Enter and Ctrl+Shift+Enter open the action menu when the row has no
  // second or third action to run; other chords stay with the text field.
  function handleActionShortcut(key, modifiers) {
    const slot = ShortcutSlots.slotForKey(key, modifiers)
    const action = ShortcutSlots.entryForSlot(directActions, slot)
    if (action) {
      shortcutRequested(action.id)
      return true
    }

    const control = (modifiers & ~Qt.KeypadModifier) === Qt.ControlModifier
    if ((control && key === Qt.Key_K)
        || slot === Ui.ShortcutSlot.Secondary || slot === Ui.ShortcutSlot.Tertiary) {
      actionsRequested()
      return true
    }
    return false
  }
  function handleSectionJump(key, modifiers) {
    if ((modifiers & ~Qt.KeypadModifier) !== Qt.ControlModifier) return false
    if (key !== Qt.Key_Up && key !== Qt.Key_Down) return false
    controller.jumpSection(key === Qt.Key_Down ? 1 : -1)
    return true
  }
  function setInputText(value) {
    searchInput.text = value || ""
    searchInput.cursorPosition = searchInput.length
  }
  // Result selection skips section headers.
  readonly property var resultKeys: ({
    step: delta => root.controller.moveSelection(delta),
    first: () => { root.controller.selectedIndex = root.controller.firstSelectableIndex() },
    last: () => { root.controller.selectedIndex = root.controller.lastSelectableIndex() }
  })
  // Discoverability chrome. These hints teach what the field accepts; they are
  // advisory only and never take focus or intercept typing. The known filter
  // tokens are supplied by CommandDeck.qml (they originate on filterDescriptors).
  property var filterDescriptors: []

  // A1 - rotating example placeholder. The first entry stays the descriptive
  // string so the accessible name/description never regresses.
  readonly property var placeholderHints: [
    "Search apps, files, config, windows, commands…",
    "Try  = 100 km to miles",
    "Try  svc: nginx",
    "Try  ai: summarize this",
    "Type  ?  for filters"
  ]
  property int placeholderIndex: 0
  // Rotate only while the launcher shows, the field is idle (empty), not
  // composing an input method, and motion is allowed. Typing a single character
  // stops it at once, and it resets to the descriptive first entry whenever
  // rotation stops.
  readonly property bool placeholderRotating: root.surfaceVisible
    && !root.theme.reduceMotion
    && searchInput.text.length === 0
    && !searchInput.inputMethodComposing
  onPlaceholderRotatingChanged: if (!placeholderRotating) placeholderIndex = 0

  // A4 - live prefix hint. Returns { token, label } when the leading token is a
  // non-empty, colon-free prefix of a known filter token/alias, else null. A
  // filter prefix is always the first thing typed, before any space.
  function prefixHintFor(text) {
    if (!text || text.length === 0) return null
    if (/\s/.test(text)) return null
    let head = text.charAt(0) === "-" ? text.slice(1) : text
    if (head.length === 0 || head.indexOf(":") >= 0) return null
    const token = head.toLowerCase()
    const descriptors = root.filterDescriptors || []
    for (let i = 0; i < descriptors.length; i++) {
      const descriptor = descriptors[i]
      const candidates = [descriptor.token].concat(descriptor.aliases || [])
      for (let j = 0; j < candidates.length; j++) {
        const candidate = String(candidates[j] || "")
        const bare = candidate.replace(/:$/, "").toLowerCase()
        if (bare.length > 0 && bare.indexOf(token) === 0) {
          return { token: candidate, label: String(descriptor.label || candidate) }
        }
      }
    }
    return null
  }

  // D11 - calculator nudge heuristic. Digits combined with an operator, or a
  // leading number followed by a unit word. A leading "=" is already the
  // calculator, so it never nudges.
  function looksMathy(text) {
    if (!text) return false
    const trimmed = text.replace(/^\s+/, "")
    if (trimmed.length === 0 || trimmed.charAt(0) === "=") return false
    if (/\d/.test(trimmed) && /[+\-*/^%]/.test(trimmed)) return true
    if (/^\d+(?:\.\d+)?\s*[A-Za-z]/.test(trimmed)) return true
    return false
  }

  readonly property var prefixHint: root.prefixHintFor(searchInput.text)
  // Only one hint shows at a time. The prefix hint wins when both could apply,
  // and the calculator nudge stays silent while a filter prefix is active.
  readonly property string activeHintKind: prefixHint !== null
    ? Ui.FilterTokenKind.Prefix
    : (searchInput.text.length > 0
        && root.controller.activeFilters.length === 0
        && root.looksMathy(searchInput.text)
      ? Ui.FilterTokenKind.Calculator
      : "")
  readonly property string activeHintText: activeHintKind === Ui.FilterTokenKind.Prefix
    ? "↹ " + prefixHint.token + "  " + prefixHint.label
    : (activeHintKind === Ui.FilterTokenKind.Calculator
      ? "Press  =  for the calculator (units, currency, functions)"
      : "")

  implicitHeight: root.theme.inputHeight
  radius: root.theme.radiusSmall
  color: root.theme.surface

  // A1 - rotation timer. It only runs while placeholderRotating is true, so it
  // is inert while the launcher is closed or under reduced motion, and stops
  // the instant the user types.
  Timer {
    id: placeholderTimer
    interval: 3500
    repeat: true
    running: root.placeholderRotating
    onTriggered: root.placeholderIndex =
      (root.placeholderIndex + 1) % root.placeholderHints.length
  }

  RowLayout {
    anchors.fill: parent
    anchors.leftMargin: 16
    anchors.rightMargin: 16
    spacing: 12

    Text {
      text: "⌕"
      color: root.theme.textMuted
      font.family: root.theme.fontFamily
      font.pixelSize: Math.round(root.theme.fontHeading * 1.4)
    }

    Repeater {
      id: filterRepeater
      model: root.controller.activeFilters

      FilterChip {
        required property var modelData
        theme: root.theme
        label: {
          const value = root.controller.filterLabel(modelData)
          return modelData.excluded ? "Not " + value : value
        }
        KeyNavigation.tab: searchInput
        KeyNavigation.backtab: root.precedingTabStop || settingsButton
        onRemoveRequested: root.controller.removeFilter(modelData.id, modelData.excluded)
      }
    }

    TextField {
      id: searchInput
      Layout.fillWidth: true
      // A1 - the visible placeholder cycles through runnable hints; under
      // reduced motion it stays on the descriptive first entry.
      placeholderText: root.placeholderHints[
        root.theme.reduceMotion ? 0 : root.placeholderIndex]
      color: root.theme.text
      placeholderTextColor: root.theme.textFaint
      background: null
      selectByMouse: true
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontHeading
      Accessible.role: Accessible.EditableText
      Accessible.name: "Search CommandDeck"
      Accessible.description: root.controller.searchAccessibilityDescription()
      KeyNavigation.tab: settingsButton
      KeyNavigation.backtab: root.filterChipControl || root.precedingTabStop || settingsButton

      onTextChanged: {
        if (root.controller.tryConvertPrefix(text)) {
          text = root.controller.queryText
          cursorPosition = length
          if (root.surfaceVisible) root.searchEdited()
          return
        }
        if (root.controller.activeFilters.length > 0
            && root.controller.queryText.length === 0
            && /^\s+/.test(text)) {
          text = text.replace(/^\s+/, "")
          cursorPosition = length
          return
        }
        if (root.surfaceVisible && root.controller.queryText !== text) {
          root.controller.queryText = text
        }
        if (root.surfaceVisible) root.searchEdited()
      }

      property bool controlDown: false
      onActiveFocusChanged: if (!activeFocus) controlDown = false
      Keys.onReleased: event => {
        if (event.key === Qt.Key_Control) controlDown = false
      }

      Keys.onPressed: event => {
        if (event.key === Qt.Key_Control) controlDown = true
        const recall = event.key === Qt.Key_Up && (event.modifiers & ~Qt.KeypadModifier) === Qt.NoModifier
          ? root.controller.recallWanted()
          : ""
        if (recall.length > 0) {
          root.historyRecallRequested(recall === "restart")
          event.accepted = true
          return
        }
        // The field drives the result list with the keys every list answers.
        if (ListKeys.handle(event, root.resultKeys, 8, true)) return

        const control = (event.modifiers & Qt.ControlModifier) !== 0
        const alt = (event.modifiers & Qt.AltModifier) !== 0
        const shift = (event.modifiers & Qt.ShiftModifier) !== 0
        const enter = event.key === Qt.Key_Return || event.key === Qt.Key_Enter

        if (root.controller.handleHelpShortcut(event.key, event.modifiers)) {
          event.accepted = true
        } else if (root.controller.handleSettingsShortcut(event.key, event.modifiers)) {
          event.accepted = true
        } else if (event.key === Qt.Key_Tab && !control && !alt && !shift) {
          const completion = root.prefixHint !== null
            ? (searchInput.text.charAt(0) === "-" ? "-" : "") + root.prefixHint.token
            : root.controller.completionText()
          if (completion.length > 0) {
            searchInput.text = completion
            searchInput.cursorPosition = searchInput.length
            event.accepted = true
          }
        } else if (event.key === Qt.Key_Backspace
                   && root.controller.removeFilterFromBackspace(
                     searchInput.cursorPosition)) {
          event.accepted = true
        } else if (event.modifiers === Qt.ControlModifier && event.key >= Qt.Key_1 && event.key <= Qt.Key_9) {
          root.controller.invokeOrdinal(event.key - Qt.Key_0)
          event.accepted = true
        } else if (root.handleSectionJump(event.key, event.modifiers)) {
          event.accepted = true
        } else if (root.zoomDirection(event.key, event.modifiers) !== null) {
          root.zoomRequested(root.zoomDirection(event.key, event.modifiers))
          event.accepted = true
        } else if (event.modifiers === Qt.ControlModifier && event.key === Qt.Key_U) {
          // Readline's Ctrl+U: delete from the start of the query to the caret.
          searchInput.remove(0, searchInput.cursorPosition)
          event.accepted = true
        } else if (control && event.key === Qt.Key_Z && root.undoAvailable) {
          root.undoRequested()
          event.accepted = true
        } else if (root.handleActionShortcut(event.key, event.modifiers)) {
          event.accepted = true
        } else if (enter && event.modifiers === Qt.NoModifier) {
          root.controller.invokePrimary()
          event.accepted = true
        } else if (event.key === Qt.Key_Right && event.modifiers === Qt.NoModifier
                   && searchInput.cursorPosition === searchInput.length
                   && root.controller.selectedResult()) {
          root.actionsRequested()
          event.accepted = true
        } else if (event.key === Qt.Key_Escape) {
          root.controller.dismiss()
          event.accepted = true
        }
      }
    }

    // A4/D11 - advisory hint chip. Ghost text that teaches; it takes no focus
    // and accepts no clicks, so it can never steal typing from the field. Only
    // one hint shows at a time (see activeHintKind).
    Rectangle {
      id: hintChip
      visible: root.activeHintKind !== ""
      Layout.alignment: Qt.AlignVCenter
      implicitWidth: hintLabel.implicitWidth + Math.round(16 * root.theme.scale)
      implicitHeight: hintLabel.implicitHeight + Math.round(8 * root.theme.scale)
      radius: root.theme.radiusSmall
      color: root.theme.withAlpha(
        root.activeHintKind === Ui.FilterTokenKind.Prefix ? root.theme.accent : root.theme.textFaint,
        0.12)
      Accessible.ignored: true

      Text {
        id: hintLabel
        objectName: "searchHintLabel"
        anchors.centerIn: parent
        text: root.activeHintText
        textFormat: Text.PlainText
        color: root.activeHintKind === Ui.FilterTokenKind.Prefix
          ? root.theme.accent
          : root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
      }
    }

    Text {
      text: "Esc"
      color: root.theme.textFaint
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
    }

    DeckButton {
      id: settingsButton
      theme: root.theme
      compact: true
      text: "⚙"
      flat: true
      ThemedToolTip {
        theme: root.theme
        visible: settingsButton.hovered
        text: "Settings (Ctrl+,)"
      }
      Accessible.name: "Open settings"
      KeyNavigation.tab: root.additionalTabStop || root.filterChipControl || searchInput
      KeyNavigation.backtab: searchInput
      onClicked: root.controller.openSettings()
    }
  }

  SearchLoadingIndicator {
    anchors.left: parent.left
    anchors.right: parent.right
    anchors.top: parent.bottom
    anchors.topMargin: Math.round(2 * root.theme.scale)
    height: implicitHeight
    theme: root.theme
    loading: root.loading && root.surfaceVisible
  }
}
