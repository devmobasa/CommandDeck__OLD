pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
  required property var host
  readonly property alias part_filterAliasesCard: filterAliasesCard
  readonly property alias part_filterAliasInput: filterAliasInput
  readonly property alias part_filterAliasTarget: filterAliasTarget
  readonly property alias part_filterAliasSave: filterAliasSave
  readonly property alias part_filterAliasRepeater: filterAliasRepeater
  readonly property alias part_savedSearchesCard: savedSearchesCard
  readonly property alias part_savedSearchName: savedSearchName
  readonly property alias part_savedSearchShortcut: savedSearchShortcut
  readonly property alias part_savedSearchHome: savedSearchHome
  readonly property alias part_savedSearchSave: savedSearchSave
  readonly property alias part_savedSearchRepeater: savedSearchRepeater

  id: filtersScroll
  clip: true
  contentWidth: availableWidth

  ColumnLayout {
    width: filtersScroll.availableWidth
    spacing: Math.round(14 * filtersScroll.host.theme.scale)

    SettingsCard {
      id: filterAliasesCard
      searchQuery: filtersScroll.host.part_settingsSearch.text
      searchTerms: "filters aliases tokens add remove"
      theme: filtersScroll.host.theme
      title: "Filter aliases"
      description: "Short tokens that stand for a filter, so a: can mean ai: and cfg: can mean config:."

      RowLayout {
        Layout.fillWidth: true
        spacing: Math.round(8 * filtersScroll.host.theme.scale)

        SettingsField {
          id: filterAliasInput
          theme: filtersScroll.host.theme
          Layout.fillWidth: true
          placeholderText: "Alias token"
          enabled: !filtersScroll.host.loading && !filtersScroll.host.snapshotMutationBusy
          Accessible.name: "New filter alias"
          Accessible.description: "One to 32 ASCII letters, digits, dots, underscores, or hyphens"
          onAccepted: filtersScroll.host.saveFilterAlias()
        }

        SettingsChoice {
          id: filterAliasTarget
          theme: filtersScroll.host.theme
          Layout.preferredWidth: Math.round(220 * filtersScroll.host.theme.scale)
          showCurrentDescription: false
          options: (filtersScroll.host.filterDescriptors || []).map(value => ({
            id: value.id, title: value.label, description: value.token
          }))
          enabled: !filtersScroll.host.loading && !filtersScroll.host.snapshotMutationBusy && count > 0
          Accessible.name: "Filter alias target"
        }

        DeckButton {
          id: filterAliasSave
          theme: filtersScroll.host.theme
          kind: Ui.ButtonVariant.Primary
          text: "Add"
          enabled: !filtersScroll.host.loading && !filtersScroll.host.snapshotMutationBusy
            && /^[A-Za-z0-9][A-Za-z0-9._-]{0,31}:?$/.test(filterAliasInput.text.trim())
            && !filtersScroll.host.filterAliasTokenIsBuiltIn(filterAliasInput.text)
            && filterAliasTarget.currentIndex >= 0
          Accessible.name: "Add filter alias"
          onClicked: filtersScroll.host.saveFilterAlias()
        }
      }

      Text {
        visible: filtersScroll.host.filterAliasError.length > 0
        Layout.fillWidth: true
        textFormat: Text.PlainText
        text: filtersScroll.host.filterAliasError
        wrapMode: Text.Wrap
        color: filtersScroll.host.theme.urgent
        font.family: filtersScroll.host.theme.fontFamily
        font.pixelSize: filtersScroll.host.theme.fontBody
        Accessible.role: Accessible.StaticText
        Accessible.name: "Filter alias error: " + text
      }

      Repeater {
        id: filterAliasRepeater
        model: filtersScroll.host.filterAliases || []

        RowLayout {
          id: aliasRow
          required property var modelData
          readonly property alias removeControl: removeFilterAliasButton
          Layout.fillWidth: true
          Layout.preferredHeight: Math.round(44 * filtersScroll.host.theme.scale)
          spacing: Math.round(10 * filtersScroll.host.theme.scale)
          Accessible.role: Accessible.ListItem
          Accessible.name: aliasRow.modelData.alias + " filters " + aliasRow.modelData.filterId

          Text {
            textFormat: Text.PlainText
            text: aliasRow.modelData.alias + ":"
            color: filtersScroll.host.theme.text
            font.family: "monospace"
            font.pixelSize: Math.round(filtersScroll.host.theme.fontBody * 1.1)
            font.weight: Font.DemiBold
          }

          Text {
            Layout.fillWidth: true
            text: "→ " + aliasRow.modelData.filterId
            color: filtersScroll.host.theme.textMuted
            elide: Text.ElideRight
            font.family: filtersScroll.host.theme.fontFamily
            font.pixelSize: Math.round(filtersScroll.host.theme.fontBody * 1.1)
          }

          DeckButton {
            id: removeFilterAliasButton
            theme: filtersScroll.host.theme
            compact: true
            text: "Remove"
            enabled: !filtersScroll.host.loading && !filtersScroll.host.snapshotMutationBusy
            Accessible.name: "Remove filter alias " + aliasRow.modelData.alias
            onClicked: filtersScroll.host.filterAliasRemoveRequested(aliasRow.modelData.alias)
          }
        }
      }
    }

    SettingsCard {
      id: savedSearchesCard
      searchQuery: filtersScroll.host.part_settingsSearch.text
      searchTerms: "filters saved searches name shortcut home run edit delete"
      theme: filtersScroll.host.theme
      title: "Saved searches"
      description: "Keep the current query and filters under a name, optionally with a shortcut and a Home row."

      RowLayout {
        Layout.fillWidth: true
        spacing: Math.round(8 * filtersScroll.host.theme.scale)

        SettingsField {
          id: savedSearchName
          theme: filtersScroll.host.theme
          Layout.fillWidth: true
          placeholderText: "Name"
          maximumLength: 64
          enabled: !filtersScroll.host.loading && !filtersScroll.host.snapshotMutationBusy
          Accessible.name: "Saved search name"
          onAccepted: filtersScroll.host.saveSavedSearch()
        }

        SettingsField {
          id: savedSearchShortcut
          theme: filtersScroll.host.theme
          Layout.preferredWidth: Math.round(170 * filtersScroll.host.theme.scale)
          placeholderText: "Ctrl+Alt+R"
          enabled: !filtersScroll.host.loading && !filtersScroll.host.snapshotMutationBusy
          Accessible.name: "Saved search shortcut"
          Accessible.description: "Optional launcher shortcut checked against effective bindings"
          onAccepted: filtersScroll.host.saveSavedSearch()
        }

        SettingsToggle {
          id: savedSearchHome
          theme: filtersScroll.host.theme
          Layout.preferredWidth: Math.round(110 * filtersScroll.host.theme.scale)
          text: "Home"
          checked: true
          enabled: !filtersScroll.host.loading && !filtersScroll.host.snapshotMutationBusy
          Accessible.name: "Show saved search on Home"
        }

        DeckButton {
          id: savedSearchSave
          theme: filtersScroll.host.theme
          kind: Ui.ButtonVariant.Primary
          text: filtersScroll.host.editingSavedSearch ? "Update" : "Save current"
          enabled: !filtersScroll.host.loading && !filtersScroll.host.snapshotMutationBusy
            && savedSearchName.text.trim().length > 0
          Accessible.name: filtersScroll.host.editingSavedSearch
            ? "Update saved search"
            : "Save current search"
          onClicked: filtersScroll.host.saveSavedSearch()
        }
      }

      DeckButton {
        theme: filtersScroll.host.theme
        compact: true
        visible: filtersScroll.host.editingSavedSearch !== null
        text: "Cancel edit"
        Accessible.name: "Cancel saved search edit"
        onClicked: filtersScroll.host.clearSavedSearchEditor()
      }

      Text {
        visible: filtersScroll.host.savedSearchError.length > 0
        Layout.fillWidth: true
        textFormat: Text.PlainText
        text: filtersScroll.host.savedSearchError
        wrapMode: Text.Wrap
        color: filtersScroll.host.theme.urgent
        font.family: filtersScroll.host.theme.fontFamily
        font.pixelSize: filtersScroll.host.theme.fontBody
        Accessible.role: Accessible.StaticText
        Accessible.name: "Saved search error: " + text
      }

      Repeater {
        id: savedSearchRepeater
        model: filtersScroll.host.savedSearches || []

        RowLayout {
          id: savedSearchRow
          required property var modelData
          readonly property alias runControl: runSavedSearchButton
          readonly property alias editControl: editSavedSearchButton
          readonly property alias deleteControl: deleteSavedSearchButton
          Layout.fillWidth: true
          Layout.preferredHeight: Math.round(44 * filtersScroll.host.theme.scale)
          spacing: Math.round(8 * filtersScroll.host.theme.scale)
          Accessible.role: Accessible.ListItem
          Accessible.name: savedSearchRow.modelData.name
            + (savedSearchRow.modelData.shortcut ? ", shortcut " + savedSearchRow.modelData.shortcut : "")

          ColumnLayout {
            Layout.fillWidth: true
            spacing: 0

            Text {
              Layout.fillWidth: true
              textFormat: Text.PlainText
              text: savedSearchRow.modelData.name
              color: filtersScroll.host.theme.text
              font.family: filtersScroll.host.theme.fontFamily
              font.pixelSize: Math.round(filtersScroll.host.theme.fontBody * 1.1)
              font.weight: Font.Medium
              elide: Text.ElideRight
            }

            Text {
              Layout.fillWidth: true
              visible: text.length > 0
              textFormat: Text.PlainText
              text: (savedSearchRow.modelData.shortcut ? String(savedSearchRow.modelData.shortcut) : "")
                + (savedSearchRow.modelData.showOnHome ? (savedSearchRow.modelData.shortcut ? " · " : "") + "On Home" : "")
              color: filtersScroll.host.theme.textFaint
              font.family: filtersScroll.host.theme.fontFamily
              font.pixelSize: filtersScroll.host.theme.fontSmall
              elide: Text.ElideRight
            }
          }

          DeckButton {
            id: runSavedSearchButton
            theme: filtersScroll.host.theme
            compact: true
            text: "Run"
            enabled: !savedSearchRow.modelData.unavailable
              && !filtersScroll.host.loading && !filtersScroll.host.snapshotMutationBusy
            Accessible.name: "Run saved search " + savedSearchRow.modelData.name
            onClicked: filtersScroll.host.savedSearchRunRequested(savedSearchRow.modelData)
          }

          DeckButton {
            id: editSavedSearchButton
            theme: filtersScroll.host.theme
            compact: true
            text: "Edit"
            enabled: !savedSearchRow.modelData.unavailable
              && !filtersScroll.host.loading && !filtersScroll.host.snapshotMutationBusy
            Accessible.name: "Edit saved search " + savedSearchRow.modelData.name
            onClicked: filtersScroll.host.editSavedSearch(savedSearchRow.modelData)
          }

          DeckButton {
            id: deleteSavedSearchButton
            theme: filtersScroll.host.theme
            kind: Ui.ButtonVariant.Danger
            compact: true
            text: "Delete"
            enabled: !filtersScroll.host.loading && !filtersScroll.host.snapshotMutationBusy
            Accessible.name: "Delete saved search " + savedSearchRow.modelData.name
            Accessible.description: "Destructive action. Requires confirmation"
            onClicked: filtersScroll.host.savedSearchDeleteRequested(savedSearchRow.modelData)
          }
        }
      }
    }
  }
}
