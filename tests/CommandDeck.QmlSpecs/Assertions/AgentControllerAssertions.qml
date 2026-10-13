import QtQuick
import "Protocol.js" as Protocol
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(){
    // A turn stuck in "starting" arms the resync watchdog; any other state
    // disarms it, so the transcript can self-heal instead of hanging forever.
    host.part_agentSpecController.state = "starting"
    host.expect(host.part_agentSpecController.startingWatchdog.running === true,
      "the resync watchdog arms while a turn is stuck starting")
    host.part_agentSpecController.state = "running"
    host.expect(host.part_agentSpecController.startingWatchdog.running === false,
      "the resync watchdog disarms once the turn advances")
    host.part_agentSpecController.state = "idle"

    host.part_agentSpecController.refreshStatus(400)
    host.expect(host.part_agentSpecController.sentRequests.length === 1
        && host.part_agentSpecController.sentRequests[0].operation === Protocol.Operations.AgentStatusGet
        && host.part_agentSpecController.sentRequests[0].version === 3
        && host.part_agentSpecController.sentRequests[0].requestId === 400,
      "agent controller requests status on protocol version 3")

    host.expect(!host.part_agentSpecController.handleMessage({
        type: Protocol.Messages.AgentStatusResult,
        requestId: 987654,
        agentStatus: { enabled: true }
      }),
      "agent controller ignores replies it never requested")

    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentStatusResult,
      requestId: 400,
      agentStatus: {
        enabled: false,
        backend: {
          id: "codex.local",
          displayName: "Codex",
          kind: "codex-app-server",
          processLocality: "local",
          inferenceLocality: "OpenAI",
          authenticationState: "unauthenticated",
          capabilities: { conversation: true, tools: true }
        },
        trust: { level: 2, levelName: "Approved context" },
        preferences: {
          enabled: false,
          backendId: "codex.local",
          modelId: "gpt-5-codex",
          reasoningEffort: "medium",
          trustLevel: 2,
          contextPolicy: "approved-folders",
          webSearchEnabled: false,
          conversationTtlMinutes: 30,
          turnTimeoutMinutes: 20
        },
        helper: { installed: false },
        pendingRequestCount: 0,
        warnings: []
      }
    })
    host.expect(host.part_agentSpecController.enabled === false
        && host.part_agentSpecController.backend.displayName === "Codex"
        && host.part_agentSpecController.trust.levelName === "Approved context"
        && host.part_agentSpecController.preferences.modelId === "gpt-5-codex",
      "agent status publishes backend, trust, and preference disclosure")

    host.part_agentSpecController.enable(401)
    const enableRequest = host.part_agentSpecController.sentRequests[
      host.part_agentSpecController.sentRequests.length - 1]
    host.expect(enableRequest.operation === Protocol.Operations.AgentPreferencesUpdate
        && enableRequest.agentPreferences.enabled === true
        && enableRequest.agentPreferences.backendId === "codex.local"
        && enableRequest.agentPreferences.trustLevel === 2
        && enableRequest.agentPreferences.contextPolicy === "approved-folders"
        && enableRequest.agentPreferences.turnTimeoutMinutes === 20,
      "inline enable stores level 2 with approved folders and keeps other preferences")

    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentPreferencesResult,
      requestId: 401,
      agentPreferences: { enabled: true, backendId: "codex.local", trustLevel: 2 }
    })
    host.expect(host.part_agentSpecController.enabled === true,
      "an acknowledged preference update enables the assistant")

    host.part_agentSpecController.startLogin(402, "device-code")
    const loginRequest = host.part_agentSpecController.sentRequests[
      host.part_agentSpecController.sentRequests.length - 1]
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentLoginResult,
      requestId: 402,
      agentLogin: {
        state: "pending",
        method: "device-code",
        userCode: "ABCD-1234",
        verificationUrl: "https://example.test/device"
      }
    })
    host.expect(loginRequest.operation === Protocol.Operations.AgentLoginStart
        && loginRequest.loginMethod === "device-code"
        && host.part_agentSpecController.login.userCode === "ABCD-1234",
      "device code login exposes the exact code CommandDeck owns")

    host.part_agentSpecController.startLogin(404, "api-key")
    const apiKeyLoginRequest = host.part_agentSpecController.sentRequests[
      host.part_agentSpecController.sentRequests.length - 1]
    host.expect(apiKeyLoginRequest.operation === Protocol.Operations.AgentLoginStart
        && apiKeyLoginRequest.loginMethod === "api-key"
        && apiKeyLoginRequest.credential === undefined
        && JSON.stringify(apiKeyLoginRequest).indexOf("sk-") < 0,
      "API-key login carries no secret and no override; the daemon resolves agent.apiKeyCredential")

    host.part_agentSpecController.open(403)
    const conversationRequest = host.part_agentSpecController.sentRequests[
      host.part_agentSpecController.sentRequests.length - 1]
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentConversationResult,
      requestId: 403,
      agentConversation: {
        summary: { conversationId: "c1", state: "idle", turnCount: 1 },
        entries: [{ role: "user", text: "earlier question", turnId: "t0" }],
        activeText: "",
        activeReasoning: "",
        tools: [],
        pending: null,
        generation: 5,
        lastSequence: 3,
        outputGeneration: 0,
        outputActions: []
      }
    })
    host.expect(conversationRequest.operation === Protocol.Operations.AgentConversationGet
        && host.part_agentSpecController.conversationRequestId === 403
        && host.part_agentSpecController.entries.length === 1
        && host.part_agentSpecController.generation === 5
        && host.part_agentSpecController.lastSequence === 3
        && host.part_agentSpecController.state === "idle",
      "resuming restores the daemon-held transcript and its epoch")

    host.part_agentSpecController.startTurn(404, "How do I rebuild?", "codex.local")
    const turnRequest = host.part_agentSpecController.sentRequests[
      host.part_agentSpecController.sentRequests.length - 1]
    host.expect(turnRequest.operation === Protocol.Operations.AgentTurnStart
        && turnRequest.query === "How do I rebuild?"
        && turnRequest.backendId === "codex.local"
        && host.part_agentSpecController.state === "starting"
        && host.part_agentSpecController.entries.length === 2
        && host.part_agentSpecController.entries[1].role === "user",
      "starting a turn shows the question before any event arrives")

    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: {
        kind: "status", generation: 5, sequence: 4, turnId: "t1",
        status: "Reading context"
      }
    })
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: { kind: "text", generation: 5, sequence: 5, text: "Run " }
    })
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: { kind: "text", generation: 5, sequence: 6, text: "make." }
    })
    host.expect(host.part_agentSpecController.activeText === ""
        && host.part_agentSpecController.pendingText === "Run make."
        && host.part_agentSpecController.activeStatus === "Reading context"
        && host.part_agentSpecController.state === "running",
      "streamed text coalesces into one repaint instead of one per delta")

    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: { kind: "text", generation: 999, sequence: 7, text: "stale epoch" }
    })
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: { kind: "text", generation: 5, sequence: 6, text: "replayed" }
    })
    host.expect(host.part_agentSpecController.pendingText === "Run make."
        && host.part_agentSpecController.lastSequence === 6,
      "events from a stale epoch or a replayed sequence cannot repopulate the view")

    host.expect(!host.part_agentSpecController.handleMessage({
        type: Protocol.Messages.AgentEvent,
        requestId: 555555,
        agentEvent: { kind: "text", generation: 5, sequence: 7, text: "other turn" }
      }),
      "events for another request id never reach this conversation")

    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: {
        kind: "tool", generation: 5, sequence: 7,
        tool: { id: "tool-1", name: "search", label: "Search", state: "running" }
      }
    })
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: {
        kind: "tool", generation: 5, sequence: 8,
        tool: { id: "tool-1", name: "search", label: "Search", state: "completed" }
      }
    })
    host.expect(host.part_agentSpecController.tools.length === 1
        && host.part_agentSpecController.tools[0].state === "completed",
      "tool rows update in place rather than accumulating duplicates")

    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: {
        kind: "reasoning", generation: 5, sequence: 9, text: "considering options"
      }
    })
    host.part_agentSpecController.flushDeltas()
    host.expect(host.part_agentSpecController.activeText === "Run make."
        && host.part_agentSpecController.activeReasoning === "considering options",
      "flushing publishes answer text and its reasoning summary separately")

    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: {
        kind: "request", generation: 5, sequence: 10,
        request: {
          id: "pending-1", kind: "context", turnId: "t1",
          prompt: "Read the project folder?",
          context: { operation: "read", path: "/home/user/project/notes.md" },
          decisions: ["allow", "allow-folder", "deny", "cancel-turn"]
        }
      }
    })
    host.expect(host.part_agentSpecController.state === "waiting"
        && host.part_agentSpecController.pending.kind === "context"
        && host.part_agentSpecController.pending.context.path === "/home/user/project/notes.md",
      "a parked request holds the turn instead of being auto-approved")

    host.part_agentSpecController.resolve(405, "pending-1", "allow-folder")
    const resolveRequest = host.part_agentSpecController.sentRequests[
      host.part_agentSpecController.sentRequests.length - 1]
    host.expect(resolveRequest.operation === Protocol.Operations.AgentContextResolve
        && resolveRequest.pendingRequestId === "pending-1"
        && resolveRequest.decision === "allow-folder"
        && host.part_agentSpecController.pending === null,
      "resolving a decision hides the card at once instead of waiting for a snapshot")


    host.part_agentSpecController.answer(406, "pending-2", { "question-1": "yes" })
    const answerRequest = host.part_agentSpecController.sentRequests[
      host.part_agentSpecController.sentRequests.length - 1]
    host.expect(answerRequest.operation === Protocol.Operations.AgentTurnInput
        && answerRequest.pendingRequestId === "pending-2"
        && answerRequest.answers["question-1"] === "yes",
      "typed answers are addressed to the exact parked request")

    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: {
        kind: "done", generation: 5, sequence: 11,
        done: {
          turnId: "t1", outcome: "completed", finalOutputBytes: 9,
          outputGeneration: 7, outputActions: ["copy", "save", "open"]
        }
      }
    })
    host.expect(host.part_agentSpecController.state === "completed"
        && host.part_agentSpecController.entries.length === 3
        && host.part_agentSpecController.entries[2].role === "assistant"
        && host.part_agentSpecController.entries[2].text === "Run make."
        && host.part_agentSpecController.activeText === ""
        && host.part_agentSpecController.pending === null
        && host.part_agentSpecController.outputGeneration === 7,
      "a finished turn moves its answer into the transcript with output authority")

    // A request the turn then resumes past clears without an explicit resolve.
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: { kind: "request", generation: 5, sequence: 12,
        request: { id: "pending-x", kind: "question", prompt: "?",
          questions: [{ id: "q", prompt: "?" }], decisions: ["deny", "cancel-turn"] } }
    })
    host.expect(host.part_agentSpecController.pending !== null, "a fresh request re-raises the card")
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 404,
      agentEvent: { kind: "status", generation: 5, sequence: 13, status: "Continuing" }
    })
    host.expect(host.part_agentSpecController.pending === null,
      "the card hides once the turn resumes even without an explicit resolve")

    host.expect(host.part_agentSpecController.handleMessage({
        type: Protocol.Messages.Done,
        requestId: 404
      }) && host.part_agentSpecController.activeRequestId === 0,
      "the turn stream closes when the daemon reports done")

    host.part_agentSpecController.outputAction("save", 407, "/tmp/agent-output.md", "")
    const outputRequest = host.part_agentSpecController.sentRequests[
      host.part_agentSpecController.sentRequests.length - 1]
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.ActionResult,
      requestId: 407,
      actionResult: {
        succeeded: false,
        message: "The target exists; confirm overwrite to continue.",
        confirmationToken: "server-owned-agent-token"
      }
    })
    host.expect(outputRequest.operation === Protocol.Operations.AgentOutput
        && outputRequest.generation === 7
        && host.part_agentSpecController.overwritePending
        && host.part_agentSpecController.overwriteConfirmationToken === "server-owned-agent-token",
      "agent save conflicts stay modal until an explicit overwrite confirmation")

    host.part_agentSpecController.requestChallenge(408, 6)
    const challengeRequest = host.part_agentSpecController.sentRequests[
      host.part_agentSpecController.sentRequests.length - 1]
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentTrustChallengeResult,
      requestId: 408,
      agentTrustChallenge: {
        id: "challenge-1", targetLevel: 6, targetLevelName: "Unrestricted",
        requiresPhrase: true, requiresDuration: true,
        prompt: "Confirm unrestricted authority",
        warnings: ["Protected paths no longer apply"]
      }
    })
    host.expect(challengeRequest.operation === Protocol.Operations.AgentTrustChallenge
        && challengeRequest.trustLevel === 6
        && host.part_agentSpecController.challenge.requiresPhrase === true
        && host.part_agentSpecController.challenge.warnings.length === 1,
      "raising authority starts with a daemon-issued single-use challenge")

    host.part_agentSpecController.updateTrust(
      409, "challenge-1", 6, "ALLOW UNRESTRICTED AGENT", "one-hour")
    const trustRequest = host.part_agentSpecController.sentRequests[
      host.part_agentSpecController.sentRequests.length - 1]
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentTrustResult,
      requestId: 409,
      agentTrust: {
        level: 6, levelName: "Unrestricted", duration: "one-hour",
        leaseExpiresUnixMilliseconds: Date.now() + 3600000
      }
    })
    host.expect(trustRequest.operation === Protocol.Operations.AgentTrustUpdate
        && trustRequest.challengeId === "challenge-1"
        && trustRequest.phrase === "ALLOW UNRESTRICTED AGENT"
        && trustRequest.trustDuration === "one-hour"
        && host.part_agentSpecController.trust.level === 6
        && host.part_agentSpecController.challenge === null,
      "an accepted challenge consumes itself and publishes the new lease")

    host.part_agentSpecController.requestGrants(410)
    host.part_agentSpecController.handleMessage({
      type: Protocol.Messages.AgentGrantsResult,
      requestId: 410,
      agentGrants: {
        grants: [
          { id: "grant-1", kind: "folder", scope: "/home/user/project",
            state: "active", label: "Project folder" },
          { id: "grant-2", kind: "command", scope: "make", state: "active",
            label: "make build" }
        ]
      }
    })
    host.part_agentSpecController.removeGrant(411, "grant-1")
    const grantRequest = host.part_agentSpecController.sentRequests[
      host.part_agentSpecController.sentRequests.length - 1]
    host.expect(host.part_agentSpecController.grants.length === 2
        && grantRequest.operation === Protocol.Operations.AgentGrantsRemove
        && grantRequest.grantId === "grant-1",
      "grants are listed and revoked one at a time by identity")

    host.part_agentSpecController.state = "running"
    host.part_agentSpecController.disconnect()
    host.expect(host.part_agentSpecController.state === "connection-lost"
        && host.part_agentSpecController.entries.length === 3
        && host.part_agentSpecController.activeRequestId === 0,
      "a lost bridge keeps the transcript because the daemon still owns the turn")
    host.part_agentSpecController.state = "idle"
    host.part_agentSpecController.disconnect()
    host.expect(host.part_agentSpecController.state === "idle" && host.part_agentSpecController.error === "",
      "an idle assistant stays idle across a daemon restart")

    const clearCount = host.part_agentSpecController.sentRequests.length
    host.part_agentSpecController.clear(412)
    host.expect(host.part_agentSpecController.sentRequests.length === clearCount + 1
        && host.part_agentSpecController.sentRequests[clearCount].operation === Protocol.Operations.AgentClear
        && host.part_agentSpecController.entries.length === 0
        && host.part_agentSpecController.generation === 0
        && host.part_agentSpecController.state === "idle",
      "only an explicit clear erases the conversation and its epoch")

    // A stale settings save is refused as "conflict: ...": the panel shows the
    // sentence, keeps the category, and asks for the current settings again.
    const controller = host.part_agentSpecController
    let statusRefreshes = 0
    const countRefresh = () => statusRefreshes++
    controller.statusRefreshRequested.connect(countRefresh)
    controller.updatePreferences(413, { enabled: true, webSearchEnabled: true })
    controller.handleMessage({
      type: Protocol.Messages.Error,
      requestId: 413,
      error: "conflict: Assistant permissions changed while saving; review the settings and save again."
    })
    controller.handleMessage({ type: Protocol.Messages.Done, requestId: 413 })
    host.expect(controller.settingsError === "Assistant permissions changed while saving; review the settings and save again."
        && controller.settingsErrorCategory === Protocol.AiErrorCategories.Conflict
        && statusRefreshes === 1
        && controller.trackedRequests[413] === undefined,
      "a stale settings save shows its sentence, keeps its category, reloads, and is released")
    controller.updatePreferences(414, { enabled: true })
    controller.handleMessage({
      type: Protocol.Messages.Error,
      requestId: 414,
      error: "Use the trust challenge to change the trust level."
    })
    host.expect(controller.settingsError === "Use the trust challenge to change the trust level."
        && controller.settingsErrorCategory === "" && statusRefreshes === 1,
      "a settings error without a category shows whole and reloads nothing")
    controller.statusRefreshRequested.disconnect(countRefresh)

    // A turn start the daemon refuses is not shown as sent, and its prompt is
    // kept so it can be sent again.
    controller.startTurn(415, "Draft a release note", "")
    controller.handleMessage({
      type: Protocol.Messages.Error,
      requestId: 415,
      error: "Assistant permissions changed while starting; send again."
    })
    host.expect(controller.entries.every(entry => entry.text !== "Draft a release note")
        && controller.unsentPrompt === "Draft a release note"
        && controller.error === "Assistant permissions changed while starting; send again."
        && controller.state === Protocol.AgentConversationStates.Failed,
      "a refused turn start drops its unsent line and keeps the prompt")

    // A turn the daemon accepted keeps its line when it fails later.
    controller.startTurn(416, "Summarize the log", "")
    host.expect(controller.unsentPrompt === "", "a new start clears the kept prompt")
    controller.handleMessage({
      type: Protocol.Messages.AgentEvent,
      requestId: 416,
      agentEvent: { kind: "status", generation: 9, sequence: 1, status: "Working", turnId: "9" }
    })
    controller.handleMessage({ type: Protocol.Messages.Error, requestId: 416, error: "The turn failed." })
    host.expect(controller.entries.some(entry => entry.text === "Summarize the log")
        && controller.unsentPrompt === "",
      "an acknowledged turn that fails later keeps its line")
    controller.clear(417)
  }
}
