pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
  required property var host
  readonly property alias part_assistantCard: assistantCard
  readonly property alias part_agentEnabledCheck: assistantCard.part_agentEnabledCheck
  readonly property alias part_agentLogin: assistantCard.part_agentLogin
  readonly property alias part_agentLogout: assistantCard.part_agentLogout
  readonly property alias part_modelCard: modelCard
  readonly property alias part_agentBackendChoice: agentBackendChoice
  readonly property alias part_agentModelChoice: agentModelChoice
  readonly property alias part_agentModelsRefresh: agentModelsRefresh
  readonly property alias part_agentEffortChoice: agentEffortChoice
  readonly property alias part_agentHiddenModelsCheck: agentHiddenModelsCheck
  readonly property alias part_trustCard: trustCard
  readonly property alias part_agentTrustChoice: agentTrustChoice
  readonly property alias part_agentChallengeCard: agentChallengeCard
  readonly property alias part_agentChallengePrompt: agentChallengePrompt
  readonly property alias part_agentWarningRepeater: agentWarningRepeater
  readonly property alias part_agentPhraseField: agentPhraseField
  readonly property alias part_agentDurationChoice: agentDurationChoice
  readonly property alias part_agentChallengeConfirm: agentChallengeConfirm
  readonly property alias part_agentChallengeCancel: agentChallengeCancel
  readonly property alias part_agentContextPolicyChoice: agentContextPolicyChoice
  readonly property alias part_agentWebSearchCheck: agentWebSearchCheck
  readonly property alias part_limitsCard: limitsCard
  readonly property alias part_agentRetentionSpin: agentRetentionSpin
  readonly property alias part_agentTurnTimeoutSpin: agentTurnTimeoutSpin
  readonly property alias part_grantsCard: grantsCard
  readonly property alias part_agentGrantRepeater: agentGrantRepeater
  readonly property alias part_agentGrantsClear: agentGrantsClear
  readonly property alias part_diagnosticsCard: diagnosticsCard
  readonly property alias part_agentStatusText: agentStatusText

  id: assistantScroll
  clip: true
  contentWidth: availableWidth

  ColumnLayout {
    width: assistantScroll.availableWidth
    spacing: Math.round(14 * assistantScroll.host.theme.scale)

    SettingsAssistantAccountCard {
      id: assistantCard
      host: assistantScroll.host
    }

    SettingsCard {
      id: modelCard
      searchQuery: assistantScroll.host.part_settingsSearch.text
      searchTerms: "assistant codex claude cursor grok backend model reasoning effort refresh hidden"
      theme: assistantScroll.host.theme
      title: "Model"
      description: (assistantScroll.host.agentModels || []).length === 0
        ? (assistantScroll.host.agentAuthenticated
          ? "Loading the model list from your account…"
          : "Sign in to load the models your account can use.")
        : "Models and reasoning levels come from your account. Higher effort answers better and uses your limits faster."

      SettingsRow {
        theme: assistantScroll.host.theme
        label: "Backend"

        SettingsChoice {
          id: agentBackendChoice
          theme: assistantScroll.host.theme
          Layout.fillWidth: true
          options: assistantScroll.host.agentBackendOptions()
          placeholder: "Choose an assistant backend"
          Accessible.name: "Assistant backend"
          onActivated: {
            const backend = assistantScroll.host.assistantBackends[currentIndex]
            if (!backend) return
            assistantScroll.host.mutateAgentPreference(Protocol.AgentPreferenceFields.BackendId, backend.id)
            assistantScroll.host.agentModelsRefreshRequested(String(backend.id))
          }
        }
      }

      SettingsRow {
        theme: assistantScroll.host.theme
        label: "Model"
        help: "The default follows your account. Hidden models appear when the toggle below is on."

        SettingsChoice {
          id: agentModelChoice
          theme: assistantScroll.host.theme
          Layout.fillWidth: true
          options: assistantScroll.host.agentModelOptions()
          placeholder: assistantScroll.host.agentAuthenticated ? "Loading models…" : "Sign in to load models"
          enabled: (assistantScroll.host.agentModels || []).length > 0
          Accessible.name: "Assistant model"
          onActivated: {
            const value = (assistantScroll.host.agentModels || [])[currentIndex]
            if (value) assistantScroll.host.mutateAgentPreference(Protocol.AgentPreferenceFields.ModelId, value.id)
            Qt.callLater(assistantScroll.host.synchronizeAgentEffort)
          }
        }

        DeckButton {
          id: agentModelsRefresh
          theme: assistantScroll.host.theme
          text: "Refresh"
          Accessible.name: "Refresh assistant models"
          onClicked: {
            const backend = assistantScroll.host.assistantBackends[agentBackendChoice.currentIndex]
            assistantScroll.host.agentModelsRefreshRequested(backend ? String(backend.id) : "")
          }
        }
      }

      SettingsRow {
        theme: assistantScroll.host.theme
        label: "Reasoning effort"
        help: "Low answers fastest. Max and Ultra think longest and consume usage limits fastest."

        SettingsChoice {
          id: agentEffortChoice
          theme: assistantScroll.host.theme
          Layout.fillWidth: true
          options: assistantScroll.host.agentEffortOptions()
          placeholder: assistantScroll.host.agentSelectedModel()
            ? "Reasoning effort is not supported"
            : "Pick a model first"
          enabled: assistantScroll.host.agentReasoningEfforts().length > 0
          Accessible.name: "Assistant reasoning effort"
          onActivated: {
            const effort = assistantScroll.host.agentReasoningEfforts()[currentIndex]
            if (effort) assistantScroll.host.mutateAgentPreference(Protocol.AgentPreferenceFields.ReasoningEffort, effort.id)
          }
        }
      }

      SettingsToggle {
        id: agentHiddenModelsCheck
        theme: assistantScroll.host.theme
        Layout.fillWidth: true
        text: "Show hidden models"
        help: "Include models your account can use but Codex does not list by default."
        checked: assistantScroll.host.agentPreferenceValues
          ? assistantScroll.host.agentPreferenceValues.showHiddenModels === true : false
        Accessible.name: "Show hidden assistant models"
        onToggled: assistantScroll.host.mutateAgentPreference(Protocol.AgentPreferenceFields.ShowHiddenModels, checked)
      }
    }

    SettingsCard {
      id: trustCard
      searchQuery: assistantScroll.host.part_settingsSearch.text
      searchTerms: "assistant codex trust level context policy approved folders ask every access web search unrestricted permission sandbox"
      theme: assistantScroll.host.theme
      title: "Trust and context"
      description: "Each level adds authority. Raising it asks for confirmation; the top level needs a typed phrase and expires."

      SettingsRow {
        theme: assistantScroll.host.theme
        label: "Trust level"
        divider: assistantScroll.host.agentChallenge === null || assistantScroll.host.agentChallenge === undefined

        // Trust never travels through the preferences path: changing
        // this control only asks the daemon for a challenge.
        SettingsChoice {
          id: agentTrustChoice
          theme: assistantScroll.host.theme
          Layout.fillWidth: true
          options: assistantScroll.host.agentTrustLevelNames.map((value, level) => ({
            id: String(level),
            title: String(level) + " · " + value,
            description: assistantScroll.host.agentTrustLevelDescriptions[level],
            badge: level === 6 ? "Phrase required" : "",
            warning: level === 6
          }))
          Accessible.name: "Assistant trust level"
          Accessible.description: "Raising authority requires a daemon-issued challenge"
          onActivated: assistantScroll.host.agentTrustChallengeRequested(currentIndex)
        }
      }

      Rectangle {
        id: agentChallengeCard
        Layout.fillWidth: true
        Layout.preferredHeight: agentChallengeColumn.implicitHeight + Math.round(28 * assistantScroll.host.theme.scale)
        visible: assistantScroll.host.agentChallenge !== null && assistantScroll.host.agentChallenge !== undefined
        radius: Math.max(4, assistantScroll.host.theme.radiusSmall)
        color: Qt.rgba(assistantScroll.host.theme.urgent.r, assistantScroll.host.theme.urgent.g, assistantScroll.host.theme.urgent.b, 0.07)
        border.width: 1
        border.color: Qt.rgba(assistantScroll.host.theme.urgent.r, assistantScroll.host.theme.urgent.g, assistantScroll.host.theme.urgent.b, 0.55)

        ColumnLayout {
          id: agentChallengeColumn
          anchors.fill: parent
          anchors.margins: Math.round(14 * assistantScroll.host.theme.scale)
          spacing: Math.round(8 * assistantScroll.host.theme.scale)

          Text {
            id: agentChallengePrompt
            Layout.fillWidth: true
            textFormat: Text.PlainText
            text: assistantScroll.host.agentChallenge
              ? String(assistantScroll.host.agentChallenge.prompt || "Confirm this authority change.")
              : ""
            color: assistantScroll.host.theme.text
            wrapMode: Text.Wrap
            font.family: assistantScroll.host.theme.fontFamily
            font.pixelSize: Math.round(assistantScroll.host.theme.fontBody * 1.15)
            font.weight: Font.DemiBold
            Accessible.role: Accessible.StaticText
            Accessible.name: "Trust challenge: " + text
          }

          Repeater {
            id: agentWarningRepeater
            model: assistantScroll.host.agentChallenge && assistantScroll.host.agentChallenge.warnings
              ? assistantScroll.host.agentChallenge.warnings : []

            delegate: Text {
              required property var modelData
              Layout.fillWidth: true
              text: "• " + String(modelData)
              color: assistantScroll.host.theme.urgent
              wrapMode: Text.Wrap
              font.family: assistantScroll.host.theme.fontFamily
              font.pixelSize: assistantScroll.host.theme.fontBody
            }
          }

          Text {
            Layout.fillWidth: true
            visible: assistantScroll.host.agentChallengeRequiresPhrase
            text: "Type exactly: " + assistantScroll.host.agentRequiredPhrase
            color: assistantScroll.host.theme.textMuted
            wrapMode: Text.Wrap
            font.family: "monospace"
            font.pixelSize: assistantScroll.host.theme.fontBody
          }

          SettingsField {
            id: agentPhraseField
            theme: assistantScroll.host.theme
            Layout.fillWidth: true
            visible: assistantScroll.host.agentChallengeRequiresPhrase
            placeholderText: assistantScroll.host.agentRequiredPhrase
            Accessible.name: "Trust challenge phrase"
            Accessible.description: "Must match the required phrase exactly"
          }

          SettingsChoice {
            id: agentDurationChoice
            theme: assistantScroll.host.theme
            Layout.fillWidth: true
            visible: assistantScroll.host.agentChallengeRequiresDuration
            options: [
              { id: Protocol.AgentTrustDurations.OneHour, title: "One hour", description: "Authority lapses on its own.", badge: "Recommended" },
              { id: Protocol.AgentTrustDurations.UntilDowngrade, title: "Until I downgrade", description: "Stays unrestricted until you lower the level.", badge: "Persistent", warning: true }
            ]
            Accessible.name: "Trust duration"
          }

          RowLayout {
            Layout.fillWidth: true
            spacing: Math.round(8 * assistantScroll.host.theme.scale)

            DeckButton {
              id: agentChallengeConfirm
              theme: assistantScroll.host.theme
              kind: Ui.ButtonVariant.Danger
              text: "Confirm"
              enabled: assistantScroll.host.agentChallengeSatisfied
              Accessible.name: "Confirm the trust change"
              Accessible.description: "Destructive action. Grants new authority"
              onClicked: assistantScroll.host.confirmAgentChallenge()
            }
            DeckButton {
              id: agentChallengeCancel
              theme: assistantScroll.host.theme
              text: "Cancel"
              Accessible.name: "Cancel the trust change"
              onClicked: {
                agentPhraseField.text = ""
                assistantScroll.host.agentChallengeCancelled()
              }
            }
            Item { Layout.fillWidth: true }
          }
        }
      }

      SettingsRow {
        theme: assistantScroll.host.theme
        label: "Context policy"
        help: "How file access is approved at level 2 and above."

        SettingsChoice {
          id: agentContextPolicyChoice
          theme: assistantScroll.host.theme
          Layout.fillWidth: true
          options: [
            { id: Protocol.AgentContextPolicies.ApprovedFolders, title: "Approved folders", description: "Folders you approve stay readable until revoked." },
            { id: Protocol.AgentContextPolicies.AskEveryAccess, title: "Ask every access", description: "Every read asks first; nothing is remembered." }
          ]
          Accessible.name: "Assistant context policy"
          onActivated: assistantScroll.host.mutateAgentPreference(
            "contextPolicy",
            currentIndex === 1 ? Protocol.AgentContextPolicies.AskEveryAccess : Protocol.AgentContextPolicies.ApprovedFolders)
        }
      }

      SettingsToggle {
        id: agentWebSearchCheck
        theme: assistantScroll.host.theme
        Layout.fillWidth: true
        text: "Web search"
        help: "Let the model search the web during a turn."
        checked: assistantScroll.host.agentPreferenceValues
          ? assistantScroll.host.agentPreferenceValues.webSearchEnabled === true : false
        Accessible.name: "Allow assistant web search"
        onToggled: assistantScroll.host.mutateAgentPreference(Protocol.AgentPreferenceFields.WebSearchEnabled, checked)
      }
    }

    SettingsCard {
      id: limitsCard
      searchQuery: assistantScroll.host.part_settingsSearch.text
      searchTerms: "assistant codex conversation retention turn timeout duration"
      theme: assistantScroll.host.theme
      title: "Limits"

      SettingsRow {
        theme: assistantScroll.host.theme
        label: "Conversation retention"
        help: "How long an idle conversation stays open. Zero drops it as soon as the turn ends."

        SettingsNumber {
          id: agentRetentionSpin
          theme: assistantScroll.host.theme
          suffix: "min"
          from: 0
          to: 1440
          Accessible.name: "Conversation retention in minutes"
          Accessible.description: "Zero drops the conversation once the turn ends"
          onValueModified: assistantScroll.host.mutateAgentPreference(
            "conversationTtlMinutes", value)
        }
      }

      SettingsRow {
        theme: assistantScroll.host.theme
        label: "Turn timeout"
        help: "Longest a single answer may work before it is cancelled."
        divider: false

        SettingsNumber {
          id: agentTurnTimeoutSpin
          theme: assistantScroll.host.theme
          suffix: "min"
          from: 5
          to: 120
          Accessible.name: "Turn timeout in minutes"
          onValueModified: assistantScroll.host.mutateAgentPreference(Protocol.AgentPreferenceFields.TurnTimeoutMinutes, value)
        }
      }
    }

    SettingsCard {
      id: grantsCard
      searchQuery: assistantScroll.host.part_settingsSearch.text
      searchTerms: "assistant codex grants revoke all remembered authority permissions"
      theme: assistantScroll.host.theme
      title: "Grants"
      description: (assistantScroll.host.agentGrants || []).length === 0
        ? "Nothing remembered yet. Approved folders and confirmed commands appear here."
        : "Authority the assistant may reuse without asking again."

      Repeater {
        id: agentGrantRepeater
        model: assistantScroll.host.agentGrants || []

        delegate: Item {
          id: grantDelegate
          required property var modelData
          required property int index
          Layout.fillWidth: true
          implicitHeight: Math.round(52 * assistantScroll.host.theme.scale)
          Accessible.role: Accessible.ListItem
          Accessible.name: "Grant "
            + String(grantDelegate.modelData.label || grantDelegate.modelData.id || "")
            + " · " + String(grantDelegate.modelData.kind || "")
            + " · " + String(grantDelegate.modelData.state || "")

          Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.bottom: parent.bottom
            height: 1
            visible: grantDelegate.index < (assistantScroll.host.agentGrants || []).length - 1
            color: assistantScroll.host.theme.separator
          }

          RowLayout {
            anchors.fill: parent
            spacing: Math.round(12 * assistantScroll.host.theme.scale)

            Rectangle {
              Layout.preferredWidth: chipText.implicitWidth + Math.round(14 * assistantScroll.host.theme.scale)
              Layout.preferredHeight: chipText.implicitHeight + Math.round(6 * assistantScroll.host.theme.scale)
              radius: height / 2
              color: Qt.rgba(assistantScroll.host.theme.accent.r, assistantScroll.host.theme.accent.g, assistantScroll.host.theme.accent.b, 0.16)

              Text {
                id: chipText
                anchors.centerIn: parent
                textFormat: Text.PlainText
                text: String(grantDelegate.modelData.kind || "")
                color: assistantScroll.host.theme.text
                font.family: assistantScroll.host.theme.fontFamily
                font.pixelSize: assistantScroll.host.theme.fontCaption
                font.weight: Font.DemiBold
              }
            }

            ColumnLayout {
              Layout.fillWidth: true
              spacing: 0

              Text {
                Layout.fillWidth: true
                textFormat: Text.PlainText
                text: String(grantDelegate.modelData.label
                  || grantDelegate.modelData.scope
                  || grantDelegate.modelData.id || "")
                color: assistantScroll.host.theme.text
                elide: Text.ElideMiddle
                font.family: assistantScroll.host.theme.fontFamily
                font.pixelSize: Math.round(assistantScroll.host.theme.fontBody * 1.1)
                font.weight: Font.Medium
              }

              Text {
                Layout.fillWidth: true
                visible: text.length > 0
                textFormat: Text.PlainText
                text: String(grantDelegate.modelData.scope || "")
                  + (grantDelegate.modelData.state && grantDelegate.modelData.state !== Protocol.AgentGrantStates.Active
                    ? " · " + String(grantDelegate.modelData.state) : "")
                color: assistantScroll.host.theme.textFaint
                elide: Text.ElideMiddle
                font.family: assistantScroll.host.theme.fontFamily
                font.pixelSize: assistantScroll.host.theme.fontSmall
              }
            }

            DeckButton {
              theme: assistantScroll.host.theme
              kind: Ui.ButtonVariant.Danger
              compact: true
              text: "Revoke"
              Accessible.name: "Revoke grant "
                + String(grantDelegate.modelData.label
                  || grantDelegate.modelData.id || "")
              Accessible.description: "Destructive action. Removes this authority"
              onClicked: assistantScroll.host.agentGrantRemoveRequested(
                String(grantDelegate.modelData.id || ""))
            }
          }
        }
      }

      RowLayout {
        Layout.fillWidth: true
        Layout.topMargin: Math.round(6 * assistantScroll.host.theme.scale)
        spacing: Math.round(8 * assistantScroll.host.theme.scale)

        DeckButton {
          id: agentGrantsClear
          theme: assistantScroll.host.theme
          kind: Ui.ButtonVariant.Danger
          text: "Revoke all"
          enabled: (assistantScroll.host.agentGrants || []).length > 0
          Accessible.name: "Revoke every assistant grant"
          Accessible.description: "Destructive action. Removes all remembered authority"
          onClicked: assistantScroll.host.agentGrantsClearRequested()
        }
        Item { Layout.fillWidth: true }
      }
    }

    SettingsCard {
      id: diagnosticsCard
      searchQuery: assistantScroll.host.part_settingsSearch.text
      searchTerms: "assistant codex diagnostics status package helper sandbox warnings"
      theme: assistantScroll.host.theme
      title: "Diagnostics"

      Text {
        id: agentStatusText
        Layout.fillWidth: true
        textFormat: Text.PlainText
        text: assistantScroll.host.agentStatusSummary
        color: assistantScroll.host.agentError.length > 0 ? assistantScroll.host.theme.urgent : assistantScroll.host.theme.textMuted
        wrapMode: Text.Wrap
        font.family: assistantScroll.host.theme.fontFamily
        font.pixelSize: assistantScroll.host.theme.fontBody
        Accessible.role: Accessible.StaticText
        Accessible.name: "Assistant status: " + text
      }
    }
  }
}
