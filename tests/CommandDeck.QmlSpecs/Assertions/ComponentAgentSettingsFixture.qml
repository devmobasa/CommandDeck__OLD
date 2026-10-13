import QtQuick
import Quickshell
import "." as CommandDeck

CommandDeck.SettingsPanel {
  required property var host

  id: agentSettingsPanel
  parent: host.part_host
  width: 700
  height: 320
  visible: false
  theme: host.part_theme
  iconResolver: function(iconName) { return "" }
  property var agentPreferencesEmitted: null
  property int agentChallengeLevel: -1
  property var agentTrustUpdate: null
  property string agentGrantRemoved: ""
  property int agentGrantsCleared: 0
  property int agentLogouts: 0
  property int agentLogins: 0
  property string agentModelsBackendRequested: ""
  onAgentLoginRequested: agentLogins++
  onAgentModelsRefreshRequested: backendId => agentModelsBackendRequested = backendId
  onAgentPreferencesChanged: preferences => agentPreferencesEmitted = preferences
  onAgentTrustChallengeRequested: level => agentChallengeLevel = level
  onAgentTrustUpdateRequested: (challengeId, level, phrase, duration) =>
    agentTrustUpdate = ({
      challengeId: challengeId,
      level: level,
      phrase: phrase,
      duration: duration
    })
  onAgentGrantRemoveRequested: grantId => agentGrantRemoved = grantId
  onAgentGrantsClearRequested: agentGrantsCleared++
  onAgentLogoutRequested: agentLogouts++
}
