pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import QtQuick.Layouts

SettingsCard {
  id: accountCard
  required property var host
  readonly property alias part_agentEnabledCheck: agentEnabledCheck
  readonly property alias part_agentLogin: agentLogin
  readonly property alias part_agentLogout: agentLogout
  readonly property alias part_answerOnlyNote: answerOnlyNote

  searchQuery: host.part_settingsSearch.text
  searchTerms: "assistant enable account sign in out login logout authentication codex claude cursor grok backend"
  theme: host.theme
  title: "Assistant"
  description: "Signed-in conversations with Codex, Claude Code, Cursor, or Grok. Tools require your chosen trust level. Ask with codex:, claude:, cursor:, grok:, or ask: in the launcher."

  SettingsToggle {
    id: agentEnabledCheck
    theme: accountCard.host.theme
    Layout.fillWidth: true
    text: "Enable assistant"
    help: "Turning it off cancels the active turn and revokes every grant."
    checked: accountCard.host.agentEnabled
    Accessible.name: "Enable the embedded assistant"
    Accessible.description: "Disabling cancels turns and revokes every grant"
    onToggled: accountCard.host.mutateAgentPreference(Protocol.AgentPreferenceFields.Enabled, checked)
  }

  SettingsRow {
    theme: accountCard.host.theme
    label: "Account"
    help: accountCard.host.agentAccountSummary
    divider: false

    DeckButton {
      id: agentLogin
      theme: accountCard.host.theme
      kind: Ui.ButtonVariant.Primary
      enabled: accountCard.host.agentEnabled
      visible: !accountCard.host.agentAuthenticated
        && accountCard.host.agentLoginState !== Protocol.AgentAuthenticationStates.NotApplicable
      text: accountCard.host.agentLoginState === Protocol.AgentAuthenticationStates.Pending
        ? "Continue sign-in" : "Sign in"
      Accessible.name: "Sign in to the assistant backend"
      onClicked: accountCard.host.agentLoginRequested()
    }

    DeckButton {
      id: agentLogout
      theme: accountCard.host.theme
      visible: accountCard.host.agentAuthenticated
      text: "Sign out"
      Accessible.name: "Sign out of the assistant backend"
      Accessible.description: "Destructive action. Deletes the owned credential"
      onClicked: accountCard.host.agentLogoutRequested()
    }
  }

  Text {
    id: answerOnlyNote
    Layout.fillWidth: true
    // A status may carry no backend (none chosen or installed yet).
    visible: {
      const status = accountCard.host.agentStatus
      const backend = status ? status.backend : null
      return !!backend && backend.kind === Protocol.AgentBackendKinds.AnswerOnly
    }
    text: "The selected backend provides quick answers. Choose a signed-in assistant backend below for conversations and tools."
    wrapMode: Text.Wrap
    color: accountCard.host.theme.textMuted
    font.family: accountCard.host.theme.fontFamily
    font.pixelSize: accountCard.host.theme.fontBody
    Accessible.role: Accessible.StaticText
    Accessible.name: text
  }
}
