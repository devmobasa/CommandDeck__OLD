import QtQuick
import QtQuick.Layouts
import "Protocol.js" as Protocol

// Settings → Search → HEY mail: where Enter opens a HEY thread. The HEY CLI
// keeps the login; a change saves as soon as it is chosen. The copy avoids
// "sign in" so a Settings search for it still finds the assistant first.
SettingsCard {
  id: card

  required property var host
  readonly property alias openTargetControl: openTargetChoice

  searchQuery: card.host.part_settingsSearch.text
  searchTerms: "hey mail email threads open web app terminal tui"
  theme: card.host.theme
  title: "HEY mail"
  description: "Search your HEY mail with hey: or mail:. The HEY CLI keeps your HEY login; CommandDeck never sees it."

  SettingsRow {
    theme: card.host.theme
    label: "Open threads in"
    help: "Where Enter on a HEY result opens it."

    SettingsChoice {
      id: openTargetChoice
      theme: card.host.theme
      Layout.fillWidth: true
      showCurrentDescription: false
      options: [
        { id: Protocol.HeyOpenTargets.Web, title: "HEY web app",
          description: "Opens the thread the way Omarchy's HEY launcher does." },
        { id: Protocol.HeyOpenTargets.Terminal, title: "HEY terminal app",
          description: "Opens the thread in hey tui, reusing one that is already running." }
      ]
      enabled: !card.host.loading
      Accessible.name: "Open HEY threads in"
      Component.onCompleted: syncSelection()
      Connections {
        target: card.host
        function onSnapshotChanged() { openTargetChoice.syncSelection() }
      }
      function syncSelection() {
        const preferences = card.host.snapshot ? card.host.snapshot.preferences : null
        currentIndex = preferences && preferences.heyOpenTarget === Protocol.HeyOpenTargets.Terminal ? 1 : 0
      }
      onActivated: index => {
        const preferences = card.host.clonePreferences()
        if (!preferences) return
        preferences.heyOpenTarget = openTargetChoice.options[index].id
        card.host.preferencesChanged(preferences)
      }
    }
  }
}
