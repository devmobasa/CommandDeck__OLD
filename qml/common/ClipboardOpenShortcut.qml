import QtQuick

Item {
  id: root
  property var preferences: null
  property var savedSearches: []
  property bool active: false
  property bool stateReady: false
  property bool preferenceUpdateInFlight: false
  // Preference drafts are optimistic: never activate an invalid chord while daemon validation is pending.
  readonly property string chord: {
    const value = String(preferences && preferences.clipboardHistoryShortcut !== undefined
      ? preferences.clipboardHistoryShortcut : "").trim().toUpperCase()
    return /^CTRL\+(SHIFT|ALT)\+[A-Z]$/.test(value) && value !== "CTRL+SHIFT+C" ? value : ""
  }
  readonly property bool conflict: chord.length > 0 && savedSearches.some(search =>
    String(search.shortcut || "").toUpperCase() === chord.toUpperCase())
  readonly property string availableChord: stateReady && !preferenceUpdateInFlight && !conflict ? chord : ""
  signal requested()

  Shortcut {
    sequence: root.chord
    enabled: root.active && root.availableChord.length > 0
    autoRepeat: false
    context: Qt.WindowShortcut
    onActivated: root.requested()
  }
}
