pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
  readonly property alias quicklinksCard: quicklinksCard
  required property var host
  readonly property alias part_indexRootsCard: indexRootsCard
  readonly property alias part_fileSearchCard: fileSearchCard
  readonly property alias part_homeFolderOnlyCheck: homeFolderOnlyCheck
  readonly property alias part_omarchyMenuStateCheck: omarchyMenuStateCheck
  readonly property alias part_sourcesCard: sourcesCard
  readonly property alias part_manageExtensions: manageExtensions
  readonly property alias part_sourceRepeater: sourceList.repeater
  readonly property alias part_sourceList: sourceList
  readonly property alias part_heyCard: heyCard
  readonly property alias part_behaviorCard: behaviorCard
  readonly property alias part_fallbackChoice: fallbackChoice

  id: searchScroll
  clip: true
  contentWidth: availableWidth

  ColumnLayout {
    width: searchScroll.availableWidth
    spacing: Math.round(14 * searchScroll.host.theme.scale)

    SettingsCard {
      id: fileSearchCard
      searchQuery: searchScroll.host.part_settingsSearch.text
      searchTerms: "search home folder only indexed files configuration"
      theme: searchScroll.host.theme
      title: "File search"
      description: "Limit file and configuration results to your home folder."

      SettingsToggle {
        id: homeFolderOnlyCheck
        theme: searchScroll.host.theme
        Layout.fillWidth: true
        text: "Home folder only"
        help: "Search indexed files in ~ and its subfolders. Turn off to include all configured folders. Applies immediately."
        checked: searchScroll.host.snapshot && searchScroll.host.snapshot.preferences
          ? searchScroll.host.snapshot.preferences.searchHomeOnly === true
          : false
        enabled: !searchScroll.host.loading
        Accessible.name: "Home folder only"
        onClicked: {
          const preferences = searchScroll.host.clonePreferences()
          if (!preferences) return
          preferences.searchHomeOnly = checked
          searchScroll.host.preferencesChanged(preferences)
        }
      }
    }

    SettingsCard {
      id: sourcesCard
      searchQuery: searchScroll.host.part_settingsSearch.text
      searchTerms: "search sources extensions install packages applications files windows workspaces commands quicklinks clipboard snippets home order drag reorder checkbox keyboard"
      theme: searchScroll.host.theme
      title: "Search sources"
      description: "Search: the source answers what you type. Home: its items show before you type. After Top Matches, results are grouped in this order: drag a source by its grip, or press Alt+Up or Alt+Down on a row."
      DeckButton {
        id: manageExtensions
        theme: searchScroll.host.theme
        text: "Manage extensions"
        Accessible.name: "Manage extensions"
        onClicked: searchScroll.host.extensionsRequested()
      }

      SettingsSourceList {
        id: sourceList
        host: searchScroll.host
      }
    }

    SettingsHeyCard {
      id: heyCard
      host: searchScroll.host
    }

    SettingsCard {
      id: behaviorCard
      searchQuery: searchScroll.host.part_settingsSearch.text
      searchTerms: "search inline calculator arithmetic dates days weeks top matches ranking best grouped sections web fallback hide after opening item close copying reduce motion animation workspace layout previews snapshots"
      theme: searchScroll.host.theme
      title: "Behavior"

      SettingsToggle {
        objectName: "topMatchesToggle"
        theme: searchScroll.host.theme
        Layout.fillWidth: true
        text: "Top Matches"
        help: "Show the best one to three results above the grouped sections."
        checked: searchScroll.host.historyChoice("topMatches")
        enabled: !searchScroll.host.loading
        Accessible.name: "Top Matches"
        onClicked: searchScroll.host.setHistoryChoice("topMatches", checked)
      }

      SettingsToggle {
        objectName: "inlineCalculatorToggle"
        theme: searchScroll.host.theme
        Layout.fillWidth: true
        text: "Inline calculator"
        help: "Show calculations and date counts above search results without an = prefix."
        checked: searchScroll.host.historyChoice("inlineCalculator")
        enabled: !searchScroll.host.loading
        Accessible.name: "Inline calculator"
        onClicked: searchScroll.host.setHistoryChoice("inlineCalculator", checked)
      }

      SettingsRow {
        theme: searchScroll.host.theme
        label: "Web fallback"
        help: "Quicklink used when nothing executable matches."

        SettingsChoice {
          id: fallbackChoice
          theme: searchScroll.host.theme
          Layout.fillWidth: true
          showCurrentDescription: false
          property var choices: searchScroll.host.fallbackChoices()
          options: choices.map(value => ({ id: value.id, title: value.title }))
          enabled: !searchScroll.host.loading
          Accessible.name: "Web fallback quicklink"
          Accessible.description: searchScroll.host.fallbackSelectionUnavailable
            ? "The configured quicklink is unavailable and will be cleared on the next edit."
            : "Choose the quicklink used when no executable result is available."
          Component.onCompleted: syncSelection()
          onChoicesChanged: syncSelection()
          Connections {
            target: searchScroll.host
            function onSnapshotChanged() { fallbackChoice.syncSelection() }
          }
          function syncSelection() {
            const selected = searchScroll.host.snapshot && searchScroll.host.snapshot.preferences
              ? searchScroll.host.snapshot.preferences.webFallbackQuicklinkId || ""
              : ""
            currentIndex = Math.max(0, choices.findIndex(value => value.id === selected))
          }
          onActivated: index => {
            const preferences = searchScroll.host.clonePreferences()
            if (!preferences) return
            preferences.webFallbackQuicklinkId = choices[index].id || null
            searchScroll.host.preferencesChanged(preferences)
          }
        }
      }

      SettingsToggle {
        id: hideAfterActionCheck
        theme: searchScroll.host.theme
        Layout.fillWidth: true
        text: "Hide after opening an item"
        help: "Close after opening or copying. Navigation, output, and errors stay visible."
        checked: !searchScroll.host.snapshot || !searchScroll.host.snapshot.preferences
          || searchScroll.host.snapshot.preferences.hideAfterAction !== false
        enabled: !searchScroll.host.loading
        Accessible.name: "Hide after opening an item"
        onClicked: {
          const preferences = searchScroll.host.clonePreferences()
          if (!preferences) return
          preferences.hideAfterAction = checked
          searchScroll.host.preferencesChanged(preferences)
        }
      }

      SettingsToggle {
        id: reduceMotionCheck
        theme: searchScroll.host.theme
        Layout.fillWidth: true
        text: "Reduce motion"
        help: "Skip animations in the launcher and its panels."
        checked: searchScroll.host.snapshot && searchScroll.host.snapshot.preferences
          ? searchScroll.host.snapshot.preferences.reduceMotion
          : false
        enabled: !searchScroll.host.loading
        Accessible.name: "Reduce Motion"
        onClicked: {
          const preferences = searchScroll.host.clonePreferences()
          if (!preferences) return
          preferences.reduceMotion = checked
          searchScroll.host.preferencesChanged(preferences)
        }
      }

      SettingsToggle {
        id: omarchyMenuStateCheck
        objectName: "omarchyMenuStateToggle"
        theme: searchScroll.host.theme
        Layout.fillWidth: true
        text: "Omarchy menu state"
        help: "Mark checked Omarchy menu items and dim unavailable ones, by running the menu's own conditions."
        checked: searchScroll.host.historyChoice("omarchyMenuState")
        enabled: !searchScroll.host.loading
        Accessible.name: "Omarchy menu state"
        onClicked: searchScroll.host.setHistoryChoice("omarchyMenuState", checked)
      }

      SettingsToggle {
        objectName: "workspacePreviewToggle"
        theme: searchScroll.host.theme
        Layout.fillWidth: true
        text: "Workspace layout previews"
        help: "Show window outlines and labels."
        checked: !searchScroll.host.snapshot || !searchScroll.host.snapshot.preferences
          || searchScroll.host.snapshot.preferences.workspacePreviewMode !== "off"
        enabled: !searchScroll.host.loading
        Accessible.name: "Workspace layout previews"
        onClicked: {
          const preferences = searchScroll.host.clonePreferences()
          if (!preferences) return
          preferences.workspacePreviewMode = checked ? "layout" : "off"
          searchScroll.host.preferencesChanged(preferences)
        }
      }

      SettingsToggle {
        objectName: "workspaceSnapshotsToggle"
        theme: searchScroll.host.theme
        Layout.fillWidth: true
        text: "Workspace window snapshots"
        help: searchScroll.host.workspaceSnapshotsAvailable
          ? "Show up to four still window images until dismissal, for at most 15 seconds. Inactive apps may supply older frames."
          : "Unavailable: requires the desktop capture lifecycle integration."
        checked: !!searchScroll.host.snapshot && !!searchScroll.host.snapshot.preferences
          && searchScroll.host.snapshot.preferences.workspacePreviewMode === "snapshots"
        enabled: !searchScroll.host.loading && searchScroll.host.workspaceSnapshotsAvailable
        Accessible.name: "Workspace window snapshots"
        onActiveFocusChanged: {
          if (!activeFocus) return
          const viewport = searchScroll.contentItem as Flickable
          if (!viewport) return
          const bottom = mapToItem(viewport.contentItem, 0, height).y
          viewport.contentY = Math.max(0, Math.min(bottom - viewport.height,
            viewport.contentHeight - viewport.height))
        }
        onClicked: {
          const preferences = searchScroll.host.clonePreferences()
          if (!preferences) return
          preferences.workspacePreviewMode = checked ? "snapshots" : "layout"
          searchScroll.host.preferencesChanged(preferences)
        }
      }
    }

    SettingsIndexRootsCard {
      id: indexRootsCard
      host: searchScroll.host
    }

    SettingsQuicklinksCard {
      id: quicklinksCard
      host: searchScroll.host
    }
  }
}
