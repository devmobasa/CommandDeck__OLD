pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
  required property var host
  readonly property alias part_editorsCard: editorsCard
  readonly property alias part_preferredEditorChoice: preferredEditorChoice
  readonly property alias part_graphicalEditorExecutable: graphicalEditorExecutable
  readonly property alias part_graphicalEditorArguments: graphicalEditorArguments
  readonly property alias part_terminalEditorExecutable: terminalEditorExecutable
  readonly property alias part_terminalEditorArguments: terminalEditorArguments
  readonly property alias part_editorSave: editorSave

  id: editorsScroll
  clip: true
  contentWidth: availableWidth

  ColumnLayout {
    width: editorsScroll.availableWidth
    spacing: Math.round(14 * editorsScroll.host.theme.scale)

    SettingsCard {
      id: editorsCard
      searchQuery: editorsScroll.host.part_settingsSearch.text
      searchTerms: "editors preferred graphical terminal executable arguments"
      theme: editorsScroll.host.theme
      title: "Editors"
      description: "How CommandDeck opens a file at a line. Arguments are a JSON array; {path}, {line}, {column}, and {key} are filled in."

      SettingsRow {
        theme: editorsScroll.host.theme
        label: "Preferred editor"

        SettingsChoice {
          id: preferredEditorChoice
          theme: editorsScroll.host.theme
          Layout.fillWidth: true
          showCurrentDescription: false
          options: [
            { id: Protocol.EditorKinds.Graphical, title: "Graphical", description: "Launched directly." },
            { id: Protocol.EditorKinds.Terminal, title: "Terminal", description: "Launched through xdg-terminal-exec." }
          ]
          enabled: !editorsScroll.host.loading && !editorsScroll.host.snapshotMutationBusy
          Accessible.name: "Preferred editor adapter"
        }
      }

      SettingsRow {
        theme: editorsScroll.host.theme
        label: "Graphical editor"
        help: "Executable and argument template."
        controlMinimumWidth: Math.round(360 * editorsScroll.host.theme.scale)

        ColumnLayout {
          Layout.fillWidth: true
          spacing: Math.round(6 * editorsScroll.host.theme.scale)

          SettingsField {
            id: graphicalEditorExecutable
            theme: editorsScroll.host.theme
            Layout.fillWidth: true
            placeholderText: "Graphical editor executable"
            enabled: !editorsScroll.host.loading && !editorsScroll.host.snapshotMutationBusy
            Accessible.name: "Graphical editor executable"
          }

          SettingsField {
            id: graphicalEditorArguments
            theme: editorsScroll.host.theme
            Layout.fillWidth: true
            placeholderText: '["--goto","{path}:{line}:{column}"]'
            enabled: !editorsScroll.host.loading && !editorsScroll.host.snapshotMutationBusy
            Accessible.name: "Graphical editor argv template"
            Accessible.description: "JSON string array using path, line, column, and key placeholders"
          }
        }
      }

      SettingsRow {
        theme: editorsScroll.host.theme
        label: "Terminal editor"
        help: "Runs without a shell."
        controlMinimumWidth: Math.round(360 * editorsScroll.host.theme.scale)
        divider: false

        ColumnLayout {
          Layout.fillWidth: true
          spacing: Math.round(6 * editorsScroll.host.theme.scale)

          SettingsField {
            id: terminalEditorExecutable
            theme: editorsScroll.host.theme
            Layout.fillWidth: true
            placeholderText: "Terminal editor executable"
            enabled: !editorsScroll.host.loading && !editorsScroll.host.snapshotMutationBusy
            Accessible.name: "Terminal editor executable"
          }

          SettingsField {
            id: terminalEditorArguments
            theme: editorsScroll.host.theme
            Layout.fillWidth: true
            placeholderText: '["+{line}","{path}"]'
            enabled: !editorsScroll.host.loading && !editorsScroll.host.snapshotMutationBusy
            Accessible.name: "Terminal editor argv template"
            Accessible.description: "Executed through xdg-terminal-exec without a shell"
          }
        }
      }

      RowLayout {
        Layout.fillWidth: true
        Layout.topMargin: Math.round(6 * editorsScroll.host.theme.scale)

        Item { Layout.fillWidth: true }

        DeckButton {
          id: editorSave
          theme: editorsScroll.host.theme
          kind: Ui.ButtonVariant.Primary
          text: "Save editors"
          enabled: !editorsScroll.host.loading && !editorsScroll.host.snapshotMutationBusy
            && graphicalEditorExecutable.text.trim().length > 0
            && terminalEditorExecutable.text.trim().length > 0
          Accessible.name: "Save editor adapters"
          onClicked: editorsScroll.host.saveEditors()
        }
      }
    }
  }
}
