import "AgentMessages.js" as AgentMessages
import "AgentRequests.js" as AgentRequests
import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol

// Client mirror of the daemon-owned assistant conversation.
//
// The daemon owns the turn. Closing the overlay, returning to search, or
// launching an application must not cancel it, so this controller keeps its
// transcript until `clear()` is called explicitly. Nothing here decides
// authority: trust, grants, and confirmations are daemon-issued and this
// object only renders and relays them.
QtObject {
  id: root

  readonly property var requestKinds: Object.freeze({
    Status: "status",
    Backends: "backends",
    Preferences: "preferences",
    Login: "login",
    LoginAction: "login-action",
    Conversation: "conversation",
    Turn: "turn",
    Input: "input",
    Resolve: "resolve",
    Cancel: "cancel",
    Clear: "clear",
    Prewarm: "prewarm",
    Models: "models",
    Challenge: "challenge",
    Trust: "trust",
    Grants: "grants",
    Output: "output"
  })

  property string state: Protocol.AgentConversationStates.Idle
  property var status: null
  // Tri-state on purpose: `null` means "not asked yet", so the surface can
  // tell an assistant that is switched off from one that was never queried.
  property var enabled: null
  property var backend: null
  property var backends: []
  property string selectedBackendId: ""
  property var login: null
  property bool loginStarting: false
  property string loginError: ""
  property string loginActionStatus: ""
  property int loginActionRequestId: 0
  property string loginActionId: ""
  property var preferences: null
  property var trust: null
  property var entries: []
  property string activeText: ""
  property string activeReasoning: ""
  property string activeStatus: ""
  property var tools: []
  property var pending: null
  property var usage: null
  property string error: ""
  property string settingsError: ""
  // The category of the last settings error, such as "conflict" for a stale save.
  property string settingsErrorCategory: ""
  // The prompt of the turn start in flight until the daemon acknowledges it,
  // and a prompt whose start the daemon refused, kept so it can be sent again.
  property string pendingStartPrompt: ""
  property string unsentPrompt: ""
  property int generation: 0
  property int lastSequence: 0
  property string activeTurnId: ""
  property int outputGeneration: 0
  property var outputActions: []
  property bool savedOutputAvailable: false
  property bool overwritePending: false
  property string overwriteConfirmationToken: ""
  property string outputStatus: ""
  property int activeRequestId: 0
  property int conversationRequestId: 0
  property int outputRequestId: 0
  property int pendingRequestCount: 0
  property var models: []
  property int latestModelsRequestId: 0
  property var challenge: null
  property var grants: []

  property string outputActionPending: ""
  property string outputPathPending: ""

  // requestId -> request kind. Correlating by id is what keeps a late reply
  // from a superseded request out of the visible conversation.
  property var trackedRequests: ({})

  property string pendingText: ""
  property string pendingReasoning: ""

  signal request(var message)

  // Asks the owner to re-open the conversation (a fresh agent.conversation.get),
  // which re-subscribes to events and applies the daemon's authoritative state.
  signal resyncRequested()

  // Asks the owner for a fresh agent.status.get. A sign-in that finishes in
  // the browser or through a pasted code changes the backend's authentication
  // state, which only a status read carries; the login card follows that.
  signal statusRefreshRequested()

  readonly property bool loginPending: root.login !== null && root.login !== undefined
    && String(root.login.state || "") === Protocol.AgentAuthenticationStates.Pending
  property int loginPollTicks: 0
  property Timer loginPollTimer: Timer {
    interval: 3000
    repeat: true
    running: root.loginPending && root.loginPollTicks < 300
    onTriggered: {
      root.loginPollTicks++
      root.statusRefreshRequested()
    }
  }
  onLoginPendingChanged: if (!root.loginPending) root.loginPollTicks = 0

  // Streamed deltas arrive far faster than a human reads. One coalesced
  // repaint per 50 ms keeps the transcript legible instead of strobing.
  property Timer deltaFlushTimer: Timer {
    interval: 50
    repeat: false
    onTriggered: root.flushDeltas()
  }

  // A turn that never leaves "starting" — a dropped event subscriber, or a
  // session that never signals — would hang the transcript forever. While
  // "starting" persists, re-request the snapshot: it re-subscribes and resolves
  // the true state (running, or idle when the daemon holds no conversation).
  property Timer startingWatchdog: Timer {
    interval: 12000
    repeat: true
    onTriggered: if (root.state === Ui.AgentState.Starting) root.resyncRequested()
  }

  onStateChanged: {
    if (root.state === Ui.AgentState.Starting) root.startingWatchdog.restart()
    else root.startingWatchdog.stop()
  }

  function flushDeltas() {
    if (root.pendingText.length > 0) {
      root.activeText += root.pendingText
      root.pendingText = ""
    }
    if (root.pendingReasoning.length > 0) {
      root.activeReasoning += root.pendingReasoning
      root.pendingReasoning = ""
    }
  }

  function dispatch(requestId, kind, message) { return AgentRequests.dispatch(root, requestId, kind, message) }

  function release(requestId) { return AgentRequests.release(root, requestId) }

  function cancelChallenge() { return AgentRequests.cancelChallenge(root) }

  function refreshStatus(requestId) { return AgentRequests.refreshStatus(root, requestId) }

  function requestBackends(requestId) { return AgentRequests.requestBackends(root, requestId) }

  // The inline enable card stores level 2 with approved folders. Every other
  // stored preference is carried through untouched so enabling never quietly
  // resets a choice the user made earlier.
  function enable(requestId) { return AgentRequests.enable(root, requestId) }

  // The backend a sign-in request targets: an explicit ID, else the selected
  // backend from the preferences (Codex when nothing is selected).
  function loginBackendId(backendId) { return AgentRequests.loginBackendId(root, backendId) }

  function defaultLoginMethod(backendId) { return AgentRequests.defaultLoginMethod(root, backendId) }

  function startLogin(requestId, method, backendId) { return AgentRequests.startLogin(root, requestId, method, backendId) }

  function invokeLoginAction(requestId, actionId, resultId) {
    return AgentRequests.invokeLoginAction(root, requestId, actionId, resultId)
  }

  function completeLogin(requestId, code, backendId) { return AgentRequests.completeLogin(root, requestId, code, backendId) }

  function cancelLogin(requestId, backendId) { return AgentRequests.cancelLogin(root, requestId, backendId) }

  function logout(requestId, backendId) { return AgentRequests.logout(root, requestId, backendId) }

  // Resuming: the same requestId keeps receiving events for an active turn,
  // so the conversation request stays correlated until its `done`.
  function open(requestId) { return AgentRequests.open(root, requestId) }

  function startTurn(requestId, text, backendId) { return AgentRequests.startTurn(root, requestId, text, backendId) }

  function answer(requestId, pendingId, answers) { return AgentRequests.answer(root, requestId, pendingId, answers) }

  function resolve(requestId, pendingId, decision) { return AgentRequests.resolve(root, requestId, pendingId, decision) }

  function cancelTurn(requestId) { return AgentRequests.cancelTurn(root, requestId) }

  function clear(requestId) { return AgentRequests.clear(root, requestId) }

  // Best-effort intent prewarm. The daemon spawns and connects the session
  // ahead of Enter and acks with a plain done. This is fire-and-forget: it
  // must never change state, add a transcript entry, or surface an error, so
  // the reply is swallowed in handleMessage below.
  function prewarm(requestId) { return AgentRequests.prewarm(root, requestId) }

  function requestModels(requestId, includeHidden, backendId) { return AgentRequests.requestModels(root, requestId, includeHidden, backendId) }

  function updatePreferences(requestId, values) { return AgentRequests.updatePreferences(root, requestId, values) }

  function requestChallenge(requestId, level) { return AgentRequests.requestChallenge(root, requestId, level) }

  function updateTrust(requestId, challengeId, level, phrase, duration) { return AgentRequests.updateTrust(root, requestId, challengeId, level, phrase, duration) }

  function requestGrants(requestId) { return AgentRequests.requestGrants(root, requestId) }

  function removeGrant(requestId, id) { return AgentRequests.removeGrant(root, requestId, id) }

  function clearGrants(requestId) { return AgentRequests.clearGrants(root, requestId) }

  function outputAction(action, requestId, path, token) { return AgentRequests.outputAction(root, action, requestId, path, token) }

  function handleMessage(message) { return AgentMessages.handleMessage(root, message) }

  function describeError(value) { return AgentMessages.describeError(root, value) }

  function applyStatus(value) { return AgentMessages.applyStatus(root, value) }

  function applySnapshot(snapshot) { return AgentMessages.applySnapshot(root, snapshot) }

  function applyEvent(event) { return AgentMessages.applyEvent(root, event) }

  function applyTool(tool) { return AgentMessages.applyTool(root, tool) }

  function finishTurn(done) { return AgentMessages.finishTurn(root, done) }

  function resetConversation() { return AgentMessages.resetConversation(root) }

  // The daemon keeps the turn: the transcript stays on screen so a reconnect
  // resumes rather than pretending the answer never happened.
  function disconnect() { return AgentMessages.disconnect(root) }
}
