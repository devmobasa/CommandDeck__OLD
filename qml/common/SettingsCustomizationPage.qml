pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
  required property var host
  readonly property alias part_displayCard: displayCard
  readonly property alias part_displaySizeSlider: displaySizeSlider
  readonly property alias part_displaySizeReset: displaySizeReset
  readonly property alias part_layoutWidth: layoutWidth
  readonly property alias part_layoutHeight: layoutHeight
  readonly property alias part_layoutCard: layoutCard
  readonly property alias part_rememberSearchesCheck: rememberSearchesCheck
  readonly property alias part_learnFromUseCheck: learnFromUseCheck
  readonly property alias part_compactLayoutCheck: compactLayoutCheck
  readonly property alias part_previewPaneCheck: previewPaneCheck
  readonly property alias part_closingCard: closingCard
  readonly property alias part_escapeClearsCheck: escapeClearsCheck
  readonly property alias part_keepSearchChoice: keepSearchChoice
  readonly property alias part_colorsCard: colorsCard
  readonly property alias part_transparencyCard: transparencyCard
  readonly property alias part_panelTransparencyCheck: panelTransparencyCheck
  readonly property alias part_panelTransparencySlider: panelTransparencySlider
  readonly property alias part_favoritesCard: favoritesCard
  readonly property alias part_hiddenItemsCard: hiddenItemsCard
  readonly property alias part_dataCard: dataCard
  readonly property alias part_resetCard: resetCard

  id: customizationScroll
  clip: true
  contentWidth: availableWidth

  ColumnLayout {
    width: customizationScroll.availableWidth
    spacing: Math.round(14 * customizationScroll.host.theme.scale)

    SettingsCard {
      id: displayCard
      searchQuery: customizationScroll.host.part_settingsSearch.text
      searchTerms: "customization display size scale zoom percent larger smaller screen match recommended previous"
      theme: customizationScroll.host.theme
      title: "Display size"
      description: "Scale text and controls together. Recommended follows your workspace and Omarchy font settings. Applies immediately."

      SettingsRow {
        objectName: "displaySizeRow"
        theme: customizationScroll.host.theme
        label: "Size"
        help: customizationScroll.host.displaySizeDescription
        divider: false

        DisplaySizeSlider {
          id: displaySizeSlider
          theme: customizationScroll.host.theme
          Layout.fillWidth: true
          from: Ui.DisplaySizePercent.Minimum
          to: Ui.DisplaySizePercent.Maximum
          stepSize: Ui.DisplaySizePercent.Step
          legacy: customizationScroll.host.displaySizeLegacy
          maximumFittingPercent: customizationScroll.host.maximumFittingPercent
          Accessible.name: "Launcher display size"
          Component.onCompleted: value = customizationScroll.host.displaySizePercent
          Connections {
            target: customizationScroll.host
            function onDisplaySizePercentChanged() {
              if (!displaySizeSlider.pressed && !displaySizeSlider.dragging)
                displaySizeSlider.value = customizationScroll.host.displaySizePercent
            }
          }
          onMoved: customizationScroll.host.displaySizePercentSelected(Math.round(value))
        }

        DeckButton {
          id: displaySizeReset
          theme: customizationScroll.host.theme
          text: "Recommended"
          compact: true
          enabled: customizationScroll.host.displaySizePercent !== Ui.DisplaySizePercent.Default
            || customizationScroll.host.displaySizeLegacy
          // Mouse activation returns to search; keyboard activation returns to the slider.
          focusPolicy: Qt.TabFocus
          down: pressed || resetPointer.pressed

          function resetSize(fromPointer) {
            customizationScroll.host.displaySizeRecommendedRequested()
            if (fromPointer) customizationScroll.host.part_settingsSearch.forceActiveFocus()
            else displaySizeSlider.sliderControl.forceActiveFocus()
          }

          onClicked: resetSize(false)
          MouseArea {
            id: resetPointer
            anchors.fill: parent
            acceptedButtons: Qt.LeftButton
            onClicked: displaySizeReset.resetSize(true)
          }
        }
      }
    }

    SettingsCard {
      id: layoutCard
      searchQuery: customizationScroll.host.part_settingsSearch.text
      searchTerms: "customization layout width height panel percent reset compact dense rows one line badges footer shortcuts preview pane details"
      theme: customizationScroll.host.theme
      title: "Layout"
      description: "Panel space and result details, without changing text or control size. Applies immediately."

      LayoutSizeRow {
        id: layoutWidth
        theme: customizationScroll.host.theme
        label: "Width"
        help: "Give the preview more room. The panel shrinks to fit your screen."
        searchControl: customizationScroll.host.part_settingsSearch
        percent: customizationScroll.host.launcherWidthPercent
        onSelected: percent => customizationScroll.host.launcherSizeSelected(percent, customizationScroll.host.launcherHeightPercent)
      }

      LayoutSizeRow {
        id: layoutHeight
        theme: customizationScroll.host.theme
        label: "Height"
        help: "Show more result rows. The panel shrinks to fit your screen."
        searchControl: customizationScroll.host.part_settingsSearch
        percent: customizationScroll.host.launcherHeightPercent
        onSelected: percent => customizationScroll.host.launcherSizeSelected(customizationScroll.host.launcherWidthPercent, percent)
      }

      SettingsToggle {
        id: compactLayoutCheck
        theme: customizationScroll.host.theme
        Layout.fillWidth: true
        text: "Compact layout"
        help: "One line per result. Ctrl+number badges appear while Ctrl is held, and the footer keeps the primary action, Actions and Help."
        checked: customizationScroll.host.compactLayout
        Accessible.name: "Compact layout"
        Accessible.description: "Show one line per result and fewer hints"
        onClicked: customizationScroll.host.compactLayoutSelected(checked)
      }

      SettingsToggle {
        id: previewPaneCheck
        theme: customizationScroll.host.theme
        Layout.fillWidth: true
        text: "Preview pane"
        help: "Details and file previews beside the results. Ctrl+I shows or hides it from the launcher; hidden, it loads no previews."
        checked: customizationScroll.host.previewPaneVisible
        Accessible.name: "Preview pane"
        Accessible.description: "Show result details and file previews beside the results"
        onClicked: customizationScroll.host.previewPaneSelected(checked)
      }
    }

    SettingsCard {
      id: closingCard
      searchQuery: customizationScroll.host.part_settingsSearch.text
      searchTerms: "customization escape close reopen keep last search home root timeout"
      theme: customizationScroll.host.theme
      title: "Closing and reopening"
      description: "What Escape does and how a reopened launcher starts. Shift+Escape always returns to Home."

      SettingsToggle {
        id: escapeClearsCheck
        theme: customizationScroll.host.theme
        Layout.fillWidth: true
        text: "Escape clears the search first"
        help: "Off closes the launcher at once. On clears a typed search or filter first; the next Escape closes."
        checked: customizationScroll.host.escapeClearsSearch
        Accessible.name: "Escape clears the search first"
        Accessible.description: "Clear a non-empty search before closing"
        onClicked: customizationScroll.host.launcherBehaviorSelected(checked, customizationScroll.host.keepSearchSeconds)
      }

      SettingsRow {
        theme: customizationScroll.host.theme
        label: "Keep the last search"
        help: "Reopen on the search you closed, with its selection. A search an action ran from is not kept."
        divider: false

        SettingsChoice {
          id: keepSearchChoice
          theme: customizationScroll.host.theme
          Layout.fillWidth: true
          options: customizationScroll.host.keepSearchOptions
          Accessible.name: "Keep the last search"
          Accessible.description: "How long a closed search is kept for reopening"
          Component.onCompleted: currentIndex = customizationScroll.host.keepSearchIndex(customizationScroll.host.keepSearchSeconds)
          Connections {
            target: customizationScroll.host
            function onKeepSearchSecondsChanged() {
              keepSearchChoice.currentIndex = customizationScroll.host.keepSearchIndex(customizationScroll.host.keepSearchSeconds)
            }
          }
          onActivated: index => customizationScroll.host.launcherBehaviorSelected(
            customizationScroll.host.escapeClearsSearch, customizationScroll.host.keepSearchOptions[index].id)
        }
      }
    }

    SettingsColorsCard {
      id: colorsCard
      host: customizationScroll.host
    }

    SettingsCard {
      id: transparencyCard
      searchQuery: customizationScroll.host.part_settingsSearch.text
      searchTerms: "customization transparent background opacity amount wallpaper"
      theme: customizationScroll.host.theme
      title: "Transparency"
      description: "Off is a solid panel. On lets the wallpaper show through. Applies immediately."

      SettingsToggle {
        id: panelTransparencyCheck
        theme: customizationScroll.host.theme
        Layout.fillWidth: true
        text: "Transparent background"
        help: "Off is a solid panel. On lets the desktop show through the launcher and its dimmed backdrop."
        checked: customizationScroll.host.panelTransparencyEnabled
        enabled: !customizationScroll.host.loading
        Accessible.name: "Transparent background"
        Accessible.description: "Enable a see-through launcher panel"
        onClicked: customizationScroll.host.panelTransparencyChanged(
          checked, customizationScroll.host.panelTransparencyPercent)
      }

      SettingsRow {
        theme: customizationScroll.host.theme
        label: "Amount"
        help: "How see-through the launcher is. Capped so it stays readable."
        divider: false
        controlMinimumWidth: Math.round(260 * customizationScroll.host.theme.scale)

        SettingsSlider {
          id: panelTransparencySlider
          theme: customizationScroll.host.theme
          Layout.fillWidth: true
          from: 0
          to: 80
          stepSize: 1
          enabled: customizationScroll.host.panelTransparencyEnabled && !customizationScroll.host.loading
          Accessible.name: "Transparency amount"
          Accessible.description: "How see-through the launcher is, from 0 to 80 percent"
          Component.onCompleted: value = customizationScroll.host.panelTransparencyPercent
          Connections {
            target: customizationScroll.host
            function onPanelTransparencyPercentChanged() {
              if (!panelTransparencySlider.pressed)
                panelTransparencySlider.value = customizationScroll.host.panelTransparencyPercent
            }
          }
          onMoved: customizationScroll.host.panelTransparencyChanged(
            customizationScroll.host.panelTransparencyEnabled, Math.round(value))
        }
      }
    }

    SettingsCard {
      id: favoritesCard
      searchQuery: customizationScroll.host.part_settingsSearch.text
      searchTerms: "customization favorites aliases bookmarks order unavailable remove"
      theme: customizationScroll.host.theme
      title: "Favorites and aliases"
      description: customizationScroll.host.snapshot && (customizationScroll.host.snapshot.customizations || []).length > 0
        ? "Pinned results and their aliases, in Home order."
        : "Nothing pinned yet. Favorite a result from its actions to see it here."

      Text {
        visible: customizationScroll.host.customizationOverflow
        Layout.fillWidth: true
        text: customizationScroll.host.customizationOverflowText
        wrapMode: Text.Wrap
        color: customizationScroll.host.theme.urgent
        font.family: customizationScroll.host.theme.fontFamily
        font.pixelSize: customizationScroll.host.theme.fontBody
        Accessible.role: Accessible.StaticText
        Accessible.name: text
      }

      Repeater {
        model: customizationScroll.host.snapshot ? customizationScroll.host.snapshot.customizations || [] : []

        RowLayout {
          id: favoriteRow
          required property var modelData
          required property int index
          Layout.fillWidth: true
          Layout.preferredHeight: Math.round(44 * customizationScroll.host.theme.scale)
          spacing: Math.round(8 * customizationScroll.host.theme.scale)
          Accessible.role: Accessible.ListItem
          Accessible.name: favoriteRow.modelData.title
            + (favoriteRow.modelData.isAvailable ? "" : ", unavailable")

          Text {
            Layout.fillWidth: true
            textFormat: Text.PlainText
            text: favoriteRow.modelData.title
              + (favoriteRow.modelData.aliases.length > 0 ? " · " + favoriteRow.modelData.aliases[0] : "")
            color: favoriteRow.modelData.isAvailable ? customizationScroll.host.theme.text : customizationScroll.host.theme.textMuted
            font.family: customizationScroll.host.theme.fontFamily
            font.pixelSize: Math.round(customizationScroll.host.theme.fontBody * 1.1)
            font.weight: Font.Medium
            elide: Text.ElideRight
          }

          Text {
            visible: !favoriteRow.modelData.isAvailable
            text: "Unavailable"
            color: customizationScroll.host.theme.urgent
            font.family: customizationScroll.host.theme.fontFamily
            font.pixelSize: customizationScroll.host.theme.fontSmall
            Accessible.role: Accessible.StaticText
            Accessible.name: favoriteRow.modelData.title + " is unavailable"
          }

          DeckButton {
            theme: customizationScroll.host.theme
            kind: Ui.ButtonVariant.Danger
            compact: true
            visible: !favoriteRow.modelData.isAvailable
            text: "Remove"
            enabled: !customizationScroll.host.loading && !customizationScroll.host.snapshotMutationBusy
            Accessible.name: "Remove unavailable " + favoriteRow.modelData.title
            Accessible.description: "Removes this orphaned bookmark and its aliases"
            onClicked: customizationScroll.host.removeUnavailable(favoriteRow.modelData.id)
          }

          DeckButton {
            theme: customizationScroll.host.theme
            compact: true
            text: "↑"
            enabled: customizationScroll.host.favoritesReorderAvailable
              && !customizationScroll.host.loading && !customizationScroll.host.snapshotMutationBusy && favoriteRow.index > 0
            Accessible.name: "Move " + favoriteRow.modelData.title + " up"
            onClicked: customizationScroll.host.moveFavorite(favoriteRow.index, -1)
          }

          DeckButton {
            theme: customizationScroll.host.theme
            compact: true
            text: "↓"
            enabled: customizationScroll.host.favoritesReorderAvailable
              && !customizationScroll.host.loading && !customizationScroll.host.snapshotMutationBusy
              && customizationScroll.host.snapshot && favoriteRow.index < customizationScroll.host.snapshot.customizations.length - 1
            Accessible.name: "Move " + favoriteRow.modelData.title + " down"
            onClicked: customizationScroll.host.moveFavorite(favoriteRow.index, 1)
          }
        }
      }
    }

    SettingsHiddenItemsCard {
      id: hiddenItemsCard
      host: customizationScroll.host
    }

    SettingsCard {
      id: dataCard
      searchQuery: customizationScroll.host.part_settingsSearch.text
      searchTerms: "customization data query history remember searches learn ranking usage export backup privacy"
      theme: customizationScroll.host.theme
      title: "Data"
      description: "History stays on this computer and can be reviewed or cleared here. Export writes every personalization to a file."

      SettingsToggle {
        id: rememberSearchesCheck
        theme: customizationScroll.host.theme
        Layout.fillWidth: true
        text: "Remember searches"
        help: "Keep the searches you run a result from, for Up-arrow recall and learned abbreviations such as ff for Firefox."
        checked: customizationScroll.host.historyChoice("rememberSearches")
        enabled: !customizationScroll.host.loading
        Accessible.name: "Remember searches"
        onClicked: customizationScroll.host.setHistoryChoice("rememberSearches", checked)
      }

      SettingsToggle {
        id: learnFromUseCheck
        theme: customizationScroll.host.theme
        Layout.fillWidth: true
        text: "Learn from what you open"
        help: "Rank what you open often and recently higher. Turning it off keeps the history; Reset Ranking clears it."
        checked: customizationScroll.host.historyChoice("learnFromUse")
        enabled: !customizationScroll.host.loading
        Accessible.name: "Learn from what you open"
        onClicked: customizationScroll.host.setHistoryChoice("learnFromUse", checked)
      }

      RowLayout {
        Layout.fillWidth: true
        spacing: Math.round(8 * customizationScroll.host.theme.scale)

        DeckButton {
          theme: customizationScroll.host.theme
          text: "Query history"
          enabled: !customizationScroll.host.loading && !customizationScroll.host.snapshotMutationBusy
          Accessible.name: "Manage query history"
          onClicked: customizationScroll.host.historyRequested()
        }

        DeckButton {
          id: exportButton
          theme: customizationScroll.host.theme
          text: "Export"
          Accessible.name: "Export personalizations"
          onClicked: customizationScroll.host.exportRequested()
        }

        Item { Layout.fillWidth: true }
      }
    }

    SettingsCard {
      id: resetCard
      searchQuery: customizationScroll.host.part_settingsSearch.text
      searchTerms: "customization reset defaults"
      theme: customizationScroll.host.theme
      title: "Reset"
      description: "Returns sources, favorites, aliases, saved searches, and assistant grants to their defaults. Removes added folders and re-enables configuration folders. Asks for confirmation."
      danger: true

      RowLayout {
        Layout.fillWidth: true

        Item { Layout.fillWidth: true }

        DeckButton {
          id: resetButton
          theme: customizationScroll.host.theme
          kind: Ui.ButtonVariant.Danger
          text: "Reset to defaults"
          enabled: !customizationScroll.host.loading && !customizationScroll.host.snapshotMutationBusy
          Accessible.name: "Reset personalizations to defaults"
          Accessible.description: "Destructive action. Requires confirmation"
          onClicked: customizationScroll.host.resetRequested()
        }
      }
    }
  }
}
