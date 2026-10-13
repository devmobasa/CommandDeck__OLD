pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
  required property var host
  readonly property alias part_typeChoice: typeChoice
  readonly property alias part_nameField: nameField
  readonly property alias part_baseUrlField: baseUrlField
  readonly property alias part_credentialKindChoice: credentialKindChoice
  readonly property alias part_credentialNameField: credentialNameField
  readonly property alias part_remoteConsentToggle: remoteConsentToggle
  readonly property alias part_testButton: testButton
  readonly property alias part_testCancelButton: testCancelButton
  readonly property alias part_modelField: modelField
  readonly property alias part_chooseModelButton: chooseModelButton
  readonly property alias part_contextField: contextField
  readonly property alias part_enabledToggle: enabledToggle
  readonly property alias part_idField: idField
  readonly property alias part_errorSummary: errorSummary
  readonly property alias part_testStatus: testStatus
  readonly property alias part_cancelButton: cancelButton
  readonly property alias part_saveButton: saveButton
  readonly property alias part_saveUseButton: saveUseButton

  id: editorScroll
  anchors.fill: parent
  anchors.margins: Math.round(24 * editorScroll.host.theme.scale)
  clip: true
  contentWidth: availableWidth

  ColumnLayout {
    width: editorScroll.availableWidth
    spacing: Math.round(10 * editorScroll.host.theme.scale)

    Text {
      Layout.fillWidth: true
      text: editorScroll.host.mode === editorScroll.host.editorModes.Add ? "Add AI provider" : "Edit AI provider"
      color: editorScroll.host.theme.text
      font.family: editorScroll.host.theme.fontFamily
      font.pixelSize: editorScroll.host.theme.fontHeading
      font.weight: Font.DemiBold
      Accessible.role: Accessible.Heading
      Accessible.name: text
    }

    Text {
      Layout.fillWidth: true
      text: "Definitions are saved by the daemon without any network traffic. Only Test / fetch models connects, and only when you ask."
      color: editorScroll.host.theme.textMuted
      wrapMode: Text.Wrap
      font.family: editorScroll.host.theme.fontFamily
      font.pixelSize: editorScroll.host.theme.fontBody
    }

    SettingsRow {
      theme: editorScroll.host.theme
      label: "Provider type"
      help: editorScroll.host.selectedPreset ? String(editorScroll.host.selectedPreset.description || "") : "Custom OpenAI Chat Completions endpoint."

      SettingsChoice {
        id: typeChoice
        theme: editorScroll.host.theme
        Layout.fillWidth: true
        showCurrentDescription: false
        options: editorScroll.host.typeOptions
        enabled: !editorScroll.host.busy
        Accessible.name: "Provider type"
        Accessible.description: "Arrow keys choose; Enter continues to the display name"
        onActivated: {
          editorScroll.host.applyPreset()
          editorScroll.host.suggestId()
          editorScroll.host.markDirty()
        }
        // Runs after the component's own handler, which already hands Tab to
        // the editor's fence; Enter continues to the display name.
        Keys.onPressed: event => {
          if ((event.key === Qt.Key_Return || event.key === Qt.Key_Enter) && !typeChoice.popup.visible) {
            nameField.forceActiveFocus()
            event.accepted = true
          }
        }
      }
    }

    SettingsRow {
      theme: editorScroll.host.theme
      label: "Display name"
      help: editorScroll.host.fieldErrors.name || ""

      SettingsField {
        id: nameField
        theme: editorScroll.host.theme
        Layout.fillWidth: true
        maximumLength: 96
        enabled: !editorScroll.host.busy
        Accessible.name: "Provider display name"
        onTextChanged: editorScroll.host.markDirty()
      }
    }

    SettingsRow {
      theme: editorScroll.host.theme
      label: "Base URL"
      help: editorScroll.host.fieldErrors.baseUrl || (editorScroll.host.kind === Protocol.AiAdapterKinds.Ollama
        ? "Plain HTTP is allowed only for a literal loopback IP such as 127.0.0.1."
        : "The adapter appends /chat/completions and /models.")

      SettingsField {
        id: baseUrlField
        theme: editorScroll.host.theme
        Layout.fillWidth: true
        enabled: !editorScroll.host.busy
        Accessible.name: "Provider base URL"
        onTextChanged: {
          editorScroll.host.invalidateTest()
          editorScroll.host.markDirty()
        }
      }
    }

    SettingsRow {
      theme: editorScroll.host.theme
      label: "Credential"
      help: editorScroll.host.fieldErrors.credentialName || (editorScroll.host.credentialKind === Protocol.AiCredentialKinds.Environment
        ? "A variable exported in your shell is not in the daemon's environment. Use systemctl --user set-environment, then restart the service."
        : editorScroll.host.credentialKind === Protocol.AiCredentialKinds.Systemd
          ? "The credential must already be provisioned for the service (LoadCredential). Naming it here does not create it."
          : "No credential is sent.")

      SettingsChoice {
        id: credentialKindChoice
        theme: editorScroll.host.theme
        Layout.preferredWidth: Math.round(190 * editorScroll.host.theme.scale)
        showCurrentDescription: false
        options: [
          { id: Protocol.AiCredentialKinds.None, title: "None" },
          { id: Protocol.AiCredentialKinds.Environment, title: "Environment variable" },
          { id: Protocol.AiCredentialKinds.Systemd, title: "systemd credential" }
        ]
        enabled: !editorScroll.host.busy
        Accessible.name: "Credential kind"
        onActivated: {
          editorScroll.host.invalidateTest()
          editorScroll.host.markDirty()
        }
      }

      SettingsField {
        id: credentialNameField
        theme: editorScroll.host.theme
        Layout.fillWidth: true
        visible: editorScroll.host.credentialKind !== Protocol.AiCredentialKinds.None
        placeholderText: editorScroll.host.credentialKind === Protocol.AiCredentialKinds.Systemd ? "commanddeck-openai" : "OPENAI_API_KEY"
        maximumLength: 64
        enabled: !editorScroll.host.busy
        Accessible.name: "Credential reference name"
        Accessible.description: "The name of the variable or systemd credential, never the secret itself"
        onTextChanged: {
          editorScroll.host.invalidateTest()
          editorScroll.host.markDirty()
        }
      }
    }

    SettingsToggle {
      id: remoteConsentToggle
      theme: editorScroll.host.theme
      Layout.fillWidth: true
      visible: editorScroll.host.remoteConsentNeeded
      text: "Allow requests to leave this machine"
      help: editorScroll.host.fieldErrors.remoteConsent || ("Prompts you confirm will be sent to " + editorScroll.host.destinationText() + " over HTTPS.")
      enabled: !editorScroll.host.busy
      Accessible.name: "Allow remote endpoint"
      onToggled: {
        editorScroll.host.invalidateTest()
        editorScroll.host.markDirty()
      }
    }

    SettingsRow {
      theme: editorScroll.host.theme
      label: "Connection"
      help: editorScroll.host.testSummary

      DeckButton {
        id: testButton
        theme: editorScroll.host.theme
        text: editorScroll.host.testBusy ? "Testing…" : "Test / fetch models"
        enabled: !editorScroll.host.busy && !editorScroll.host.testBusy && editorScroll.host.directAiEnabled
        Accessible.name: "Test the connection and fetch models"
        Accessible.description: "Sends no prompt; asks the endpoint for its model list"
        onClicked: editorScroll.host.runTest()
      }

      DeckButton {
        id: testCancelButton
        theme: editorScroll.host.theme
        visible: editorScroll.host.testBusy
        text: "Cancel"
        Accessible.name: "Cancel the connection test"
        onClicked: editorScroll.host.testCancelRequested()
      }
    }

    SettingsRow {
      theme: editorScroll.host.theme
      label: "Model"
      help: editorScroll.host.fieldErrors.model || "Enter a model ID manually or choose one from a test. Saving does not check that the model accepts requests."

      SettingsField {
        id: modelField
        theme: editorScroll.host.theme
        Layout.fillWidth: true
        placeholderText: editorScroll.host.kind === Protocol.AiAdapterKinds.Ollama ? "qwen3:8b" : "gpt-5-mini"
        maximumLength: 128
        enabled: !editorScroll.host.busy
        Accessible.name: "Model ID"
        onTextChanged: editorScroll.host.markDirty()
      }

      DeckButton {
        id: chooseModelButton
        theme: editorScroll.host.theme
        visible: editorScroll.host.testModels.length > 0
        text: "Choose…"
        enabled: !editorScroll.host.busy
        Accessible.name: "Choose a model from the test results"
        onClicked: editorScroll.host.part_picker.opened = true
      }
    }

    SettingsRow {
      theme: editorScroll.host.theme
      label: "Context limit"
      help: editorScroll.host.fieldErrors.contextLimit || "Optional token budget shown with usage; leave empty to omit."

      SettingsField {
        id: contextField
        theme: editorScroll.host.theme
        Layout.preferredWidth: Math.round(180 * editorScroll.host.theme.scale)
        placeholderText: "Optional"
        inputMethodHints: Qt.ImhDigitsOnly
        maximumLength: 8
        enabled: !editorScroll.host.busy
        Accessible.name: "Context limit"
        onTextChanged: editorScroll.host.markDirty()
      }
    }

    SettingsToggle {
      id: enabledToggle
      theme: editorScroll.host.theme
      Layout.fillWidth: true
      text: "Enabled"
      help: "A disabled provider keeps its definition and selection but cannot run inference."
      enabled: !editorScroll.host.busy
      Accessible.name: "Provider enabled"
      onToggled: editorScroll.host.markDirty()
    }

    SettingsRow {
      theme: editorScroll.host.theme
      label: "Provider ID"
      help: editorScroll.host.fieldErrors.id || (editorScroll.host.mode === editorScroll.host.editorModes.Add
        ? "Stable identifier used by the CLI and the selection; it cannot change after saving."
        : "Read-only after creation.")
      divider: false

      SettingsField {
        id: idField
        theme: editorScroll.host.theme
        Layout.fillWidth: true
        readOnly: editorScroll.host.mode !== editorScroll.host.editorModes.Add
        maximumLength: 64
        enabled: !editorScroll.host.busy
        Accessible.name: "Provider ID"
        onTextChanged: editorScroll.host.markDirty()
      }
    }

    Text {
      id: errorSummary
      Layout.fillWidth: true
      visible: editorScroll.host.errorText.length > 0
      textFormat: Text.PlainText
      text: editorScroll.host.errorText
      color: editorScroll.host.theme.urgent
      wrapMode: Text.Wrap
      font.family: editorScroll.host.theme.fontFamily
      font.pixelSize: editorScroll.host.theme.fontBody
      Accessible.role: Accessible.Alert
      Accessible.name: "Save error: " + text
    }

    Text {
      id: testStatus
      Layout.fillWidth: true
      visible: !editorScroll.host.directAiEnabled
      text: "Direct AI is off. Save keeps this definition; enable direct AI on the providers page to test or use it."
      color: editorScroll.host.theme.textMuted
      wrapMode: Text.Wrap
      font.family: editorScroll.host.theme.fontFamily
      font.pixelSize: editorScroll.host.theme.fontSmall
      Accessible.role: Accessible.StaticText
      Accessible.name: text
    }

    RowLayout {
      Layout.fillWidth: true
      spacing: Math.round(8 * editorScroll.host.theme.scale)

      Item { Layout.fillWidth: true }

      DeckButton {
        id: cancelButton
        theme: editorScroll.host.theme
        text: "Cancel"
        enabled: !editorScroll.host.busy
        Accessible.name: "Cancel editing the provider"
        onClicked: editorScroll.host.requestCancel()
      }

      DeckButton {
        id: saveButton
        theme: editorScroll.host.theme
        kind: Ui.ButtonVariant.Primary
        text: editorScroll.host.busy ? "Saving…" : "Save"
        enabled: !editorScroll.host.busy
        Accessible.name: "Save the provider without selecting it"
        onClicked: editorScroll.host.submit(false)
      }

      DeckButton {
        id: saveUseButton
        theme: editorScroll.host.theme
        visible: editorScroll.host.directAiEnabled
        text: "Save and use"
        enabled: !editorScroll.host.busy
        Accessible.name: "Save the provider and select it with this model"
        onClicked: editorScroll.host.submit(true)
      }
    }

    Text {
      Layout.fillWidth: true
      text: "Tab Next · Shift+Tab Previous · Enter/Space Activate · Esc " + (editorScroll.host.dirty ? "Discard…" : "Cancel")
      color: editorScroll.host.theme.textMuted
      font.family: editorScroll.host.theme.fontFamily
      font.pixelSize: editorScroll.host.theme.fontCaption
      Accessible.role: Accessible.StaticText
      Accessible.name: text
    }
  }
}
