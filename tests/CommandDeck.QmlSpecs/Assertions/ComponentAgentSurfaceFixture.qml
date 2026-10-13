import QtQuick
import "." as CommandDeck

CommandDeck.AgentSurface {
  width: 700
  height: 460
  opened: false
  property int backToSearchCount: 0
  property string lastDecision: ""
  onBackToSearch: backToSearchCount++
  onDecisionRequested: (pendingId, decision) => lastDecision = pendingId + ":" + decision
}
