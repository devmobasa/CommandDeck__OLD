pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
  required property var host
  readonly property alias part_aiManagementCard: aiManagementCard
  readonly property alias part_aiEnableToggle: aiEnableToggle
  readonly property alias part_aiManagementStatus: aiManagementStatus
  readonly property alias part_aiProviderRepeater: aiProviderRepeater
  readonly property alias part_aiAddProvider: aiAddProvider
  readonly property alias part_aiReload: aiReload
  readonly property alias part_aiProvidersCard: aiProvidersCard
  readonly property alias part_aiProviderChoice: aiProviderChoice
  readonly property alias part_aiModelChoice: aiModelChoice
  readonly property alias part_aiSelectionSave: aiSelectionSave
  readonly property alias part_aiStatus: aiStatus
  readonly property alias part_templatesCard: templatesCard
  readonly property alias part_aiTemplateRepeater: aiTemplateRepeater
  readonly property alias part_aiTemplateId: aiTemplateId
  readonly property alias part_aiTemplateName: aiTemplateName
  readonly property alias part_aiTemplateSystem: aiTemplateSystem
  readonly property alias part_aiTemplateUser: aiTemplateUser

  id: aiScroll
  clip: true
  contentWidth: availableWidth

  ColumnLayout {
    width: aiScroll.availableWidth
    spacing: Math.round(14 * aiScroll.host.theme.scale)

    SettingsCard {
      id: aiManagementCard
      visible: matchesSearch && aiScroll.host.aiFeaturesEnabled
      searchQuery: aiScroll.host.part_settingsSearch.text
      searchTerms: "ai providers direct enable ollama openai grok xai endpoint api key credential model provider add edit remove test"
      theme: aiScroll.host.theme
      title: "Quick answer providers"
      description: "Ollama and OpenAI-compatible endpoints for one-question answers. Each request has a review step. Credentials are referenced by name, never stored here."

      SettingsToggle {
        id: aiEnableToggle
        theme: aiScroll.host.theme
        Layout.fillWidth: true
        text: "Enable direct AI"
        help: "Off means no direct request can be sent. Providers stay editable either way."
        enabled: aiScroll.host.aiManagementMutable
        Accessible.name: "Enable direct AI"
        onToggled: aiScroll.host.aiEnableRequested(checked, aiScroll.host.aiSettingsRevision)
      }

      Text {
        id: aiManagementStatus
        Layout.fillWidth: true
        textFormat: Text.PlainText
        text: aiScroll.host.aiManagementSummary
        wrapMode: Text.Wrap
        color: aiScroll.host.aiSettingsStatus === Ui.ProviderSettingsState.Failed || aiScroll.host.aiSettingsStatus === Ui.ProviderSettingsState.Conflict
          || aiScroll.host.aiConfigurationError !== null || !aiScroll.host.aiSettingsSupported
          ? aiScroll.host.theme.urgent : aiScroll.host.theme.textMuted
        font.family: aiScroll.host.theme.fontFamily
        font.pixelSize: aiScroll.host.theme.fontBody
        Accessible.role: Accessible.StaticText
        Accessible.name: "Provider settings status: " + text
      }

      Repeater {
        id: aiProviderRepeater
        model: aiScroll.host.aiManagedProviders

        RowLayout {
          id: providerRow
          required property var modelData
          readonly property alias editControl: editAiProviderButton
          readonly property alias toggleControl: toggleAiProviderButton
          readonly property alias removeControl: removeAiProviderButton
          Layout.fillWidth: true
          Layout.preferredHeight: Math.round(48 * aiScroll.host.theme.scale)
          spacing: Math.round(8 * aiScroll.host.theme.scale)
          Accessible.role: Accessible.ListItem
          Accessible.name: String(providerRow.modelData.displayName || providerRow.modelData.id) + ", " + aiScroll.host.aiProviderRowSummary(providerRow.modelData)

          ColumnLayout {
            Layout.fillWidth: true
            spacing: 0

            Text {
              Layout.fillWidth: true
              textFormat: Text.PlainText
              text: String(providerRow.modelData.displayName || providerRow.modelData.id)
                + (providerRow.modelData.enabled === false ? "  (disabled)" : "")
              color: providerRow.modelData.enabled === false ? aiScroll.host.theme.textMuted : aiScroll.host.theme.text
              font.family: aiScroll.host.theme.fontFamily
              font.pixelSize: Math.round(aiScroll.host.theme.fontBody * 1.1)
              font.weight: Font.Medium
              elide: Text.ElideRight
            }

            Text {
              Layout.fillWidth: true
              textFormat: Text.PlainText
              text: aiScroll.host.aiProviderRowSummary(providerRow.modelData)
              color: aiScroll.host.theme.textFaint
              font.family: aiScroll.host.theme.fontFamily
              font.pixelSize: aiScroll.host.theme.fontSmall
              elide: Text.ElideRight
            }
          }

          DeckButton {
            id: editAiProviderButton
            theme: aiScroll.host.theme
            compact: true
            text: "Edit"
            enabled: aiScroll.host.aiManagementMutable
            Accessible.name: "Edit AI provider " + String(providerRow.modelData.displayName || providerRow.modelData.id)
            onClicked: aiScroll.host.openAiProviderEditor(providerRow.modelData, editAiProviderButton)
          }

          DeckButton {
            id: toggleAiProviderButton
            theme: aiScroll.host.theme
            compact: true
            text: providerRow.modelData.enabled === false ? "Enable" : "Disable"
            enabled: aiScroll.host.aiManagementMutable
            Accessible.name: (providerRow.modelData.enabled === false ? "Enable" : "Disable")
              + " AI provider " + String(providerRow.modelData.displayName || providerRow.modelData.id)
            onClicked: aiScroll.host.toggleAiProviderEnabled(providerRow.modelData)
          }

          DeckButton {
            id: removeAiProviderButton
            theme: aiScroll.host.theme
            kind: Ui.ButtonVariant.Danger
            compact: true
            text: "Remove"
            enabled: aiScroll.host.aiManagementMutable
            Accessible.name: "Remove AI provider " + String(providerRow.modelData.displayName || providerRow.modelData.id)
            Accessible.description: "Destructive action. Requires confirmation"
            onClicked: aiScroll.host.confirmAiProviderRemoval(providerRow.modelData, removeAiProviderButton)
          }
        }
      }

      RowLayout {
        Layout.fillWidth: true
        spacing: Math.round(8 * aiScroll.host.theme.scale)

        DeckButton {
          id: aiAddProvider
          theme: aiScroll.host.theme
          kind: Ui.ButtonVariant.Primary
          text: "Add provider"
          enabled: aiScroll.host.aiManagementMutable && aiScroll.host.aiManagedProviders.length < 8
          Accessible.name: "Add an AI provider"
          onClicked: aiScroll.host.openAiProviderEditor(null, aiAddProvider)
        }

        DeckButton {
          id: aiReload
          theme: aiScroll.host.theme
          text: aiScroll.host.aiSettingsStatus === Ui.ProviderSettingsState.ConnectionLost ? "Reconnect" : "Reload"
          enabled: !aiScroll.host.aiSettingsBusy
          Accessible.name: "Reload provider settings from the daemon"
          onClicked: aiScroll.host.aiSettingsReloadRequested()
        }
      }
    }

    SettingsCard {
      id: aiProvidersCard
      visible: matchesSearch && aiScroll.host.aiFeaturesEnabled
      searchQuery: aiScroll.host.part_settingsSearch.text
      searchTerms: "ai providers model refresh selection save selected active"
      theme: aiScroll.host.theme
      title: "Selected provider and model"
      description: "Which of the providers above answers ai: questions and the Explain, Summarize, and Propose Text actions. Refresh lists models from that provider only when you ask; Save stores the pair."

      SettingsRow {
        theme: aiScroll.host.theme
        label: "Provider"

        SettingsChoice {
          id: aiProviderChoice
          theme: aiScroll.host.theme
          Layout.fillWidth: true
          showCurrentDescription: false
          options: (aiScroll.host.aiProviders || []).map(value => ({ id: value.id, title: value.displayName }))
          placeholder: aiScroll.host.aiSettingsLoaded && !aiScroll.host.aiDirectEnabled
            ? "Enable direct AI above to choose a provider"
            : (aiScroll.host.aiManagedProviders.length > 0 ? "No enabled providers" : "Add a provider above")
          enabled: !aiScroll.host.aiSelectionBusy
          Accessible.name: "AI provider"
          onActivated: {
            const provider = aiScroll.host.aiProviders[currentIndex]
            if (provider) aiScroll.host.aiModelsRefreshRequested(provider.id)
          }
        }

        DeckButton {
          theme: aiScroll.host.theme
          text: "Refresh"
          enabled: aiProviderChoice.currentIndex >= 0
            && !aiScroll.host.aiSelectionBusy
          Accessible.name: "Refresh AI models"
          onClicked: {
            const provider = aiScroll.host.aiProviders[aiProviderChoice.currentIndex]
            if (provider) aiScroll.host.aiModelsRefreshRequested(provider.id)
          }
        }
      }

      SettingsRow {
        theme: aiScroll.host.theme
        label: "Model"
        divider: false

        SettingsChoice {
          id: aiModelChoice
          theme: aiScroll.host.theme
          Layout.fillWidth: true
          showCurrentDescription: false
          options: (aiScroll.host.aiModels || []).map(value => ({ id: value.id, title: value.displayName }))
          placeholder: "Refresh to list models"
          enabled: !aiScroll.host.aiSelectionBusy
          Accessible.name: "AI model"
        }

        DeckButton {
          id: aiSelectionSave
          theme: aiScroll.host.theme
          kind: Ui.ButtonVariant.Primary
          text: "Save"
          enabled: !aiScroll.host.aiSelectionBusy
            && aiProviderChoice.currentIndex >= 0 && aiModelChoice.currentIndex >= 0
          Accessible.name: "Save AI provider and model selection"
          onClicked: {
            const provider = aiScroll.host.aiProviders[aiProviderChoice.currentIndex]
            const model = aiScroll.host.aiModels[aiModelChoice.currentIndex]
            if (provider && model) aiScroll.host.aiSelectionUpdateRequested(provider.id, model.id)
          }
        }
      }

      Text {
        id: aiStatus
        Layout.fillWidth: true
        textFormat: Text.PlainText
        text: aiScroll.host.aiError.length > 0
          ? aiScroll.host.aiError
          : aiScroll.host.aiSettingsLoaded && !aiScroll.host.aiDirectEnabled
            ? "Direct AI is off. The saved selection is kept and used once direct AI is enabled above."
            : aiScroll.host.aiSelection && aiScroll.host.aiSelection.explanation
              ? aiScroll.host.aiSelection.explanation : "Models refresh only when requested."
        wrapMode: Text.Wrap
        color: aiScroll.host.aiError.length > 0 ? aiScroll.host.theme.urgent : aiScroll.host.theme.textMuted
        font.family: aiScroll.host.theme.fontFamily
        font.pixelSize: aiScroll.host.theme.fontBody
        Accessible.role: Accessible.StaticText
        Accessible.name: "AI status: " + text
      }
    }

    SettingsCard {
      id: templatesCard
      visible: matchesSearch && aiScroll.host.aiFeaturesEnabled
      searchQuery: aiScroll.host.part_settingsSearch.text
      searchTerms: "ai providers prompt templates system user input new edit delete"
      theme: aiScroll.host.theme
      title: "Prompt templates"
      description: "Reusable prompts for the direct providers. The user template must contain {{input}}."

      Repeater {
        id: aiTemplateRepeater
        model: aiScroll.host.aiTemplates || []
        delegate: RowLayout {
          id: templateRow
          required property var modelData
          Layout.fillWidth: true
          Layout.preferredHeight: Math.round(44 * aiScroll.host.theme.scale)
          spacing: Math.round(8 * aiScroll.host.theme.scale)

          Text {
            Layout.fillWidth: true
            textFormat: Text.PlainText
            text: templateRow.modelData.name
            color: aiScroll.host.theme.text
            elide: Text.ElideRight
            font.family: aiScroll.host.theme.fontFamily
            font.pixelSize: Math.round(aiScroll.host.theme.fontBody * 1.1)
            font.weight: Font.Medium
            Accessible.name: "AI template " + templateRow.modelData.name
          }
          DeckButton {
            theme: aiScroll.host.theme
            compact: true
            text: "Edit"
            enabled: !aiScroll.host.aiTemplateBusy
            Accessible.name: "Edit AI template " + templateRow.modelData.name
            onClicked: aiScroll.host.editAiTemplate(templateRow.modelData)
          }
          DeckButton {
            theme: aiScroll.host.theme
            kind: Ui.ButtonVariant.Danger
            compact: true
            text: "Delete"
            enabled: !aiScroll.host.aiTemplateBusy
            Accessible.name: "Delete AI template " + templateRow.modelData.name
            Accessible.description: "Destructive action. Requires confirmation"
            onClicked: aiScroll.host.aiTemplateDeleteRequested(templateRow.modelData)
          }
        }
      }

      RowLayout {
        Layout.fillWidth: true
        Layout.topMargin: Math.round(6 * aiScroll.host.theme.scale)
        spacing: Math.round(8 * aiScroll.host.theme.scale)

        SettingsField {
          id: aiTemplateId
          theme: aiScroll.host.theme
          Layout.fillWidth: true
          enabled: !aiScroll.host.aiTemplateBusy && !aiScroll.host.editingAiTemplate
          placeholderText: "Template ID"
          Accessible.name: "AI template ID"
        }
        SettingsField {
          id: aiTemplateName
          theme: aiScroll.host.theme
          Layout.fillWidth: true
          enabled: !aiScroll.host.aiTemplateBusy
          placeholderText: "Template name"
          Accessible.name: "AI template name"
        }
      }
      SettingsTextArea {
        id: aiTemplateSystem
        theme: aiScroll.host.theme
        Layout.fillWidth: true
        enabled: !aiScroll.host.aiTemplateBusy
        placeholderText: "System template (optional)"
        Accessible.name: "AI system template"
      }
      SettingsTextArea {
        id: aiTemplateUser
        theme: aiScroll.host.theme
        Layout.fillWidth: true
        enabled: !aiScroll.host.aiTemplateBusy
        text: "{{input}}"
        placeholderText: "User template; include {{input}}"
        Accessible.name: "AI user template"
        Accessible.description: "Must include the literal input placeholder"
      }
      RowLayout {
        Layout.fillWidth: true
        spacing: Math.round(8 * aiScroll.host.theme.scale)
        DeckButton {
          theme: aiScroll.host.theme
          kind: Ui.ButtonVariant.Primary
          text: aiScroll.host.editingAiTemplate ? "Update template" : "Save template"
          enabled: !aiScroll.host.aiTemplateBusy
          Accessible.name: text
          onClicked: aiScroll.host.saveAiTemplate()
        }
        DeckButton {
          theme: aiScroll.host.theme
          text: "New template"
          enabled: !aiScroll.host.aiTemplateBusy
          onClicked: aiScroll.host.editAiTemplate(null)
        }
        Item { Layout.fillWidth: true }
      }
    }
  }
}
