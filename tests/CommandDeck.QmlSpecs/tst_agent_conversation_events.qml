import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_agent_conversation"
    function test_agent_composer_enter_starts_one_turn() {
      fixture.part_agentEventSurface.opened = true
      wait(0)
      const composer = findChild(fixture.part_agentEventSurface, "agentComposer")
      verify(composer !== null)
      tryVerify(function() { return composer.activeFocus })
      typeText("Rebuild the index")
      keyClick(Qt.Key_Return)
      compare(fixture.part_agentEventSurface.submitted, "Rebuild the index")
      compare(composer.text, "")
      compare(fixture.part_agentEventController.sentRequests.length, 1)
      compare(fixture.part_agentEventController.sentRequests[0].operation, "agent.turn.start")
      compare(fixture.part_agentEventController.sentRequests[0].version, 3)
      compare(fixture.part_agentEventController.sentRequests[0].query, "Rebuild the index")
      compare(fixture.part_agentEventController.state, "starting")
      compare(fixture.part_agentEventController.entries.length, 1)

      const statusLine = findChild(fixture.part_agentEventSurface, "agentStatusLine")
      verify(statusLine.text.indexOf("Starting Codex") >= 0)
      verify(statusLine.text.indexOf("Closing leaves the turn running") >= 0)
      fixture.part_agentEventSurface.opened = false
    }

    function test_agent_composer_blocked_while_turn_active() {
      // A turn already running or waiting on a decision must not start a second
      // turn: the daemon rejects it, and doing so re-surfaced the previous turn's
      // parked card over the new question. Send is disabled and Enter is a no-op.
      fixture.part_agentEventController.sentRequests = []
      fixture.part_agentEventController.pending = null
      fixture.part_agentEventSurface.submitted = ""
      fixture.part_agentEventController.state = "running"
      fixture.part_agentEventSurface.opened = true
      wait(0)
      const composer = findChild(fixture.part_agentEventSurface, "agentComposer")
      const send = findChild(fixture.part_agentEventSurface, "agentSend")
      verify(composer !== null && send !== null)
      composer.forceActiveFocus()
      tryVerify(function() { return composer.activeFocus })
      typeText("Ask something else")
      verify(!send.enabled)
      keyClick(Qt.Key_Return)
      compare(fixture.part_agentEventSurface.submitted, "")
      compare(fixture.part_agentEventController.sentRequests.length, 0)
      compare(composer.text, "Ask something else")

      // Same guard while a pending decision is waiting.
      fixture.part_agentEventController.state = "waiting"
      keyClick(Qt.Key_Return)
      compare(fixture.part_agentEventSurface.submitted, "")
      compare(fixture.part_agentEventController.sentRequests.length, 0)

      // Reset so a later test starts from a clean, idle controller.
      composer.text = ""
      fixture.part_agentEventController.state = "idle"
      fixture.part_agentEventController.sentRequests = []
      fixture.part_agentEventSurface.submitted = ""
      fixture.part_agentEventSurface.opened = false
    }

    function test_agent_refused_start_returns_the_prompt_to_the_composer() {
      // A start the daemon refuses (here, permissions changed while it was
      // starting) is not shown as sent; the prompt returns to the composer and
      // Enter sends it again.
      const controller = fixture.part_agentEventController
      controller.sentRequests = []
      controller.entries = []
      controller.state = "idle"
      fixture.part_agentEventSurface.submitted = ""
      fixture.part_agentEventSurface.opened = true
      wait(0)
      const composer = findChild(fixture.part_agentEventSurface, "agentComposer")
      verify(composer !== null)
      composer.forceActiveFocus()
      tryVerify(function() { return composer.activeFocus })
      typeText("Rebuild the index again")
      keyClick(Qt.Key_Return)
      compare(controller.sentRequests.length, 1)
      compare(composer.text, "")
      const refusedId = controller.sentRequests[0].requestId

      controller.handleMessage({
        type: Protocol.Messages.Error,
        requestId: refusedId,
        error: "Assistant permissions changed while starting; send again."
      })
      controller.handleMessage({ type: Protocol.Messages.Done, requestId: refusedId })
      verify(controller.entries.every(entry => entry.text !== "Rebuild the index again"))
      compare(composer.text, "Rebuild the index again")
      verify(composer.activeFocus)

      keyClick(Qt.Key_Return)
      compare(controller.sentRequests.length, 2)
      compare(controller.sentRequests[1].operation, "agent.turn.start")
      compare(controller.sentRequests[1].query, "Rebuild the index again")
      compare(composer.text, "")

      controller.state = "idle"
      controller.entries = []
      controller.sentRequests = []
      fixture.part_agentEventSurface.submitted = ""
      fixture.part_agentEventSurface.opened = false
    }

    function test_agent_escape_parks_the_pending_card_then_returns_to_search() {
      fixture.part_agentEventController.pending = {
        id: "pending-event-1",
        kind: "context",
        prompt: "Read the project folder?",
        context: { operation: "read", path: "/home/user/project/notes.md" },
        decisions: ["allow", "deny"]
      }
      fixture.part_agentEventController.state = "waiting"
      fixture.part_agentEventSurface.opened = true
      wait(0)
      verify(fixture.part_agentEventSurface.pendingCardVisible)
      const allowFolder = findChild(fixture.part_agentEventSurface, "agentContextAllowFolder")
      verify(!allowFolder.visible)

      keyClick(Qt.Key_Escape)
      verify(!fixture.part_agentEventSurface.pendingCardVisible)
      verify(fixture.part_agentEventSurface.pendingParked)
      compare(fixture.part_agentEventSurface.decisions.length, 0)
      compare(fixture.part_agentEventController.pending.id, "pending-event-1")
      compare(fixture.part_agentEventSurface.backToSearchCount, 0)
      compare(fixture.part_agentEventController.sentRequests.length, 0)

      keyClick(Qt.Key_Escape)
      compare(fixture.part_agentEventSurface.backToSearchCount, 1)
      compare(fixture.part_agentEventSurface.decisions.length, 0)

      const showPending = findChild(fixture.part_agentEventSurface, "agentShowPending")
      verify(showPending.visible)
      showPending.forceActiveFocus()
      keyClick(Qt.Key_Space)
      verify(fixture.part_agentEventSurface.pendingCardVisible)

      const deny = findChild(fixture.part_agentEventSurface, "agentContextDeny")
      deny.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_agentEventSurface.decisions[0], "pending-event-1:deny")
      compare(fixture.part_agentEventController.sentRequests[0].operation, "agent.context.resolve")
      compare(fixture.part_agentEventController.sentRequests[0].decision, "deny")
      fixture.part_agentEventSurface.opened = false
    }

    function test_agent_events_stream_into_the_transcript_and_trap_focus() {
      fixture.part_agentEventSurface.opened = true
      wait(0)
      fixture.part_agentEventController.startTurn(950, "Summarize the repository", "")
      const requestId = fixture.part_agentEventController.activeRequestId
      fixture.part_agentEventController.handleMessage({
        type: Protocol.Messages.AgentEvent,
        requestId: requestId,
        agentEvent: { kind: "text", generation: 3, sequence: 1, text: "partial " }
      })
      fixture.part_agentEventController.handleMessage({
        type: Protocol.Messages.AgentEvent,
        requestId: requestId,
        agentEvent: { kind: "text", generation: 3, sequence: 2, text: "answer" }
      })
      fixture.part_agentEventController.handleMessage({
        type: Protocol.Messages.AgentEvent,
        requestId: requestId,
        agentEvent: {
          kind: "reasoning", generation: 3, sequence: 3, text: "weighing options"
        }
      })
      tryCompare(fixture.part_agentEventController, "activeText", "partial answer")
      compare(fixture.part_agentEventController.activeReasoning, "weighing options")
      wait(0)

      const reasoningToggle = findChild(fixture.part_agentEventSurface, "agentReasoningToggle")
      const reasoningBody = findChild(fixture.part_agentEventSurface, "agentReasoningBody")
      verify(reasoningToggle.visible)
      verify(!reasoningBody.visible)
      reasoningToggle.forceActiveFocus()
      keyClick(Qt.Key_Space)
      verify(reasoningBody.visible)

      fixture.part_agentEventController.handleMessage({
        type: Protocol.Messages.AgentEvent,
        requestId: requestId,
        agentEvent: {
          kind: Protocol.AgentEventKinds.Done, generation: 3, sequence: 4,
          done: {
            turnId: "t-event", outcome: "completed", outputGeneration: 4,
            outputActions: ["copy", "save", "open"]
          }
        }
      })
      compare(fixture.part_agentEventController.state, "completed")
      compare(fixture.part_agentEventController.entries.length, 2)
      compare(fixture.part_agentEventController.entries[1].role, "assistant")
      compare(fixture.part_agentEventController.entries[1].text, "partial answer")
      compare(fixture.part_agentEventController.activeText, "")
      wait(0)

      const composer = findChild(fixture.part_agentEventSurface, "agentComposer")
      composer.forceActiveFocus()
      keyClick(Qt.Key_Tab)
      verify(fixture.part_agentEventSurface.focusTargets().some(item => item.activeFocus))
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      verify(fixture.part_agentEventSurface.focusTargets().some(item => item.activeFocus))

      const save = findChild(fixture.part_agentEventSurface, "agentSave")
      fixture.part_agentEventSurface.saveDirectory = "/tmp"
      verify(save.visible && save.enabled)
      fixture.part_agentEventSurface.copyText("who founded OpenAI")
      compare(fixture.part_agentEventSurface.lastCopiedText, "who founded OpenAI")
      fixture.part_agentEventSurface.saveTo("/tmp/commanddeck-agent-output.md")
      compare(fixture.part_agentEventSurface.requestedOutputAction, "save")
      fixture.part_agentEventSurface.opened = false
    }

    function test_agent_question_card_answers_with_enter_and_options() {
      fixture.part_agentEventController.pending = {
        id: "question-pending",
        kind: "question",
        prompt: "Which build target?",
        questions: [{
          id: "question-1",
          header: "Target",
          prompt: "Which build target?",
          options: ["debug", "release"],
          allowFreeText: true
        }],
        decisions: []
      }
      fixture.part_agentEventController.state = "waiting"
      fixture.part_agentEventSurface.opened = true
      wait(0)

      const field = findChild(fixture.part_agentEventSurface, "agentQuestionText")
      verify(field !== null && field.visible)
      field.forceActiveFocus()
      typeText("release")
      keyClick(Qt.Key_Return)
      compare(fixture.part_agentEventSurface.answeredPendingId, "question-pending")
      compare(fixture.part_agentEventSurface.answeredText, "release")
      compare(field.text, "")
      compare(fixture.part_agentEventController.sentRequests[0].operation, "agent.turn.input")
      compare(
        fixture.part_agentEventController.sentRequests[0].pendingRequestId, "question-pending")
      compare(fixture.part_agentEventController.sentRequests[0].answers["question-1"], "release")
      // Answering hides the card at once; a fresh question raises a new one.
      compare(fixture.part_agentEventController.pending, null)

      fixture.part_agentEventController.pending = {
        id: "question-pending",
        kind: "question",
        prompt: "Which build target?",
        questions: [{
          id: "question-1",
          header: "Target",
          prompt: "Which build target?",
          options: ["debug", "release"],
          allowFreeText: true
        }],
        decisions: []
      }
      fixture.part_agentEventController.state = "waiting"
      wait(0)

      const option = findChild(fixture.part_agentEventSurface, "agentQuestionOption0")
      verify(option !== null && option.visible)
      option.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_agentEventSurface.answeredText, "debug")
      compare(fixture.part_agentEventController.sentRequests.length, 2)
      fixture.part_agentEventSurface.opened = false
    }

    function test_agent_prewarm_is_fire_and_forget() {
      // Intent prewarm dispatches exactly one agent.prewarm request.
      fixture.part_agentEventController.prewarm(970)
      compare(fixture.part_agentEventController.sentRequests.length, 1)
      compare(fixture.part_agentEventController.sentRequests[0].operation, "agent.prewarm")
      compare(fixture.part_agentEventController.sentRequests[0].version, 3)
      compare(fixture.part_agentEventController.sentRequests[0].requestId, 970)
      // It never touches the visible conversation: no state change, no entry.
      compare(fixture.part_agentEventController.state, "idle")
      compare(fixture.part_agentEventController.entries.length, 0)

      // The done ack is swallowed without surfacing anything.
      verify(fixture.part_agentEventController.handleMessage({ type: Protocol.Messages.Done, requestId: 970 }))
      compare(fixture.part_agentEventController.state, "idle")
      compare(fixture.part_agentEventController.error, "")

      // An error reply is swallowed too: best-effort, so nothing is surfaced.
      fixture.part_agentEventController.prewarm(971)
      verify(fixture.part_agentEventController.handleMessage({
        type: Protocol.Messages.Error, requestId: 971, error: "spawn failed"
      }))
      compare(fixture.part_agentEventController.state, "idle")
      compare(fixture.part_agentEventController.error, "")
    }

    function test_agent_model_refresh_clears_stale_models_and_ignores_late_results() {
      fixture.part_agentEventController.models = [{ id: "gpt-5-codex" }]

      fixture.part_agentEventController.requestModels(980, false, "codex.local")
      compare(fixture.part_agentEventController.models.length, 0,
        "refresh hides models from the previous inventory while loading")
      compare(fixture.part_agentEventController.sentRequests[0].backendId, "codex.local")

      fixture.part_agentEventController.requestModels(981, false, "claude.local")
      verify(fixture.part_agentEventController.handleMessage({
        type: Protocol.Messages.AgentModelsResult, requestId: 980,
        agentModels: { models: [{ id: "stale-codex" }] }
      }))
      compare(fixture.part_agentEventController.models.length, 0,
        "a late response from the previous backend cannot repopulate the picker")

      verify(fixture.part_agentEventController.handleMessage({
        type: Protocol.Messages.Error, requestId: 980, error: "stale failure"
      }))
      compare(fixture.part_agentEventController.settingsError, "")

      verify(fixture.part_agentEventController.handleMessage({
        type: Protocol.Messages.AgentModelsResult, requestId: 981,
        agentModels: { models: [{ id: "claude-sonnet" }] }
      }))
      compare(fixture.part_agentEventController.models.length, 1)
      compare(fixture.part_agentEventController.models[0].id, "claude-sonnet")
      compare(fixture.part_agentEventController.preferences.modelId, "gpt-5-codex",
        "refreshing the inventory does not itself overwrite saved preferences")
    }

    function test_agent_preference_updates_relay_daemon_owned_trust() {
      fixture.part_agentEventController.preferences = {
        enabled: true, backendId: "codex.local", modelId: "gpt-5-codex",
        reasoningEffort: "high", trustLevel: 6,
        trustDuration: "until-downgrade",
        trustLeaseExpiresUnixMilliseconds: null
      }
      const changed = {
        enabled: true, backendId: "claude.local", modelId: "sonnet",
        reasoningEffort: "", contextPolicy: "approved-folders"
      }

      fixture.part_agentEventController.updatePreferences(982, changed)

      const sent = fixture.part_agentEventController.sentRequests[0].agentPreferences
      compare(sent.backendId, "claude.local")
      compare(sent.trustLevel, 6)
      compare(sent.trustDuration, "until-downgrade")
      compare(sent.trustLeaseExpiresUnixMilliseconds, null)
      verify(changed.trustLevel === undefined,
        "relaying daemon-owned trust does not add authority to the settings draft")
    }

    function test_agent_login_code_entry_submits_completion_with_real_keys() {
      fixture.part_agentEventController.backend = {
        id: "claude.local",
        displayName: "Claude Code",
        processLocality: "local",
        inferenceLocality: "remote",
        authenticationState: "pending"
      }
      fixture.part_agentEventController.login = {
        state: "pending",
        backendId: "claude.local",
        method: "browser",
        requiresCode: true,
        verificationUrl: "https://claude.com/oauth?code=true",
        message: "Open the page, sign in, then paste the code it shows.",
        supportedMethods: ["browser"]
      }
      fixture.part_agentEventController.sentRequests = []
      fixture.part_agentEventSurface.opened = true
      wait(0)
      const codeField = findChild(fixture.part_agentEventSurface, "agentLoginCode")
      const submit = findChild(fixture.part_agentEventSurface, "agentLoginCodeSubmit")
      const deviceCode = findChild(fixture.part_agentEventSurface, "agentLoginDeviceCode")
      const browser = findChild(fixture.part_agentEventSurface, "agentLoginBrowser")
      verify(codeField !== null && submit !== null)
      verify(codeField.visible)
      verify(!submit.enabled)
      verify(!deviceCode.visible)
      verify(!browser.visible)
      tryVerify(function() { return codeField.activeFocus })
      typeText("pasted-code")
      verify(submit.enabled)
      keyClick(Qt.Key_Return)
      compare(fixture.part_agentEventSurface.submittedLoginCode, "pasted-code")
      const statusLine = findChild(fixture.part_agentEventSurface, "agentStatusLine")
      verify(statusLine !== null)

      // Unauthenticated Claude Code offers only its browser sign-in.
      fixture.part_agentEventController.login = { state: "unauthenticated", backendId: "claude.local", supportedMethods: ["browser"] }
      fixture.part_agentEventController.backend = {
        id: "claude.local", displayName: "Claude Code", processLocality: "local",
        inferenceLocality: "remote", authenticationState: "unauthenticated"
      }
      wait(0)
      verify(!codeField.visible)
      verify(browser.visible)
      verify(!deviceCode.visible)
      browser.forceActiveFocus()
      tryVerify(function() { return browser.activeFocus })
      keyClick(Qt.Key_Space)
      compare(fixture.part_agentEventSurface.loginMethod, "browser")
      fixture.part_agentEventSurface.opened = false
    }

    function test_device_code_login_shows_progress_errors_and_retry() {
      const controller = fixture.part_agentEventController
      const surface = fixture.part_agentEventSurface
      controller.sentRequests = []
      controller.loginStarting = false
      controller.loginError = ""
      controller.login = { state: "unauthenticated", supportedMethods: ["device-code", "api-key"] }
      controller.backend = {
        id: "codex.local", displayName: "Codex", authenticationState: "unauthenticated"
      }
      surface.opened = true
      wait(0)
      const deviceButton = findChild(surface, "agentLoginDeviceCode")
      const message = findChild(surface, "agentLoginMessage")
      const error = findChild(surface, "agentLoginError")
      const openButton = findChild(surface, "agentOpenLoginPage")
      tryCompare(deviceButton, "activeFocus", true)
      keyClick(Qt.Key_Space)
      compare(controller.sentRequests.length, 1)
      compare(controller.sentRequests[0].operation, "agent.login.start")
      compare(controller.sentRequests[0].loginMethod, "device-code")
      verify(controller.loginStarting)
      verify(!deviceButton.enabled)
      compare(message.text, "Starting sign-in…")

      controller.handleMessage({
        type: Protocol.Messages.Error, requestId: controller.sentRequests[0].requestId,
        error: "Codex could not start. Check the installation."
      })
      verify(!controller.loginStarting)
      verify(error.visible)
      compare(error.text, "Codex could not start. Check the installation.")
      wait(20)
      mouseClick(deviceButton)
      compare(controller.sentRequests.length, 2)
      verify(controller.loginStarting)
      verify(!error.visible)

      controller.handleMessage({
        type: Protocol.Messages.AgentLoginResult, requestId: controller.sentRequests[1].requestId,
        agentLogin: {
          state: "pending", method: "device-code", userCode: "ABCD-1234",
          verificationUrl: "https://example.test/device", supportedMethods: ["device-code"]
        }
      })
      compare(findChild(surface, "agentDeviceCode").text, "ABCD-1234")
      verify(openButton.enabled)
      tryCompare(openButton, "activeFocus", true)
      keyClick(Qt.Key_Return)
      compare(surface.openLoginCount, 1)
      const openRequest = controller.sentRequests[controller.sentRequests.length - 1]
      compare(openRequest.operation, Protocol.Operations.Invoke)
      compare(openRequest.resultId, "agent:login")
      compare(openRequest.actionId, Protocol.AgentLoginActionIds.OpenUrl)
      const actionStatus = findChild(surface, "agentLoginActionStatus")
      compare(actionStatus.text, "The browser request is in progress.")
      controller.handleMessage({
        type: Protocol.Messages.ActionResult, requestId: openRequest.requestId,
        actionResult: { succeeded: false, message: "The browser did not start." }
      })
      compare(error.text, "The browser did not start.")
      verify(error.visible)
      wait(20)
      tryCompare(openButton, "activeFocus", true)
      mouseClick(openButton)
      compare(surface.openLoginCount, 2)
      const retryRequest = controller.sentRequests[controller.sentRequests.length - 1]
      compare(retryRequest.actionId, Protocol.AgentLoginActionIds.OpenUrl)
      verify(!error.visible)
      controller.handleMessage({
        type: Protocol.Messages.ActionResult, requestId: retryRequest.requestId,
        actionResult: { succeeded: true }
      })
      compare(actionStatus.text, "The desktop accepted the request. Check your browser.")
      verify(actionStatus.visible)
      wait(20)
      mouseClick(findChild(surface, "agentCopyCode"))
      compare(surface.copyCodeCount, 1)
      const copyRequest = controller.sentRequests[controller.sentRequests.length - 1]
      compare(copyRequest.actionId, Protocol.AgentLoginActionIds.CopyCode)
      controller.handleMessage({
        type: Protocol.Messages.ActionResult, requestId: copyRequest.requestId,
        actionResult: { succeeded: true }
      })
      compare(actionStatus.text, "The code is on the clipboard.")
      surface.opened = false

      controller.loginStarting = true
      controller.disconnect()
      verify(!controller.loginStarting)
      compare(controller.loginError, "Connection lost during sign-in. Reconnect and try again.")
      controller.loginError = ""
    }

    function test_codex_launcher_warning_offers_keyboard_and_mouse_backend_choice() {
      const controller = fixture.part_agentEventController
      const surface = fixture.part_agentEventSurface
      controller.loginStarting = false
      controller.loginError = ""
      controller.backend = {
        id: "codex.local", displayName: "Codex", authenticationState: "unauthenticated"
      }
      controller.login = { state: "unauthenticated", supportedMethods: ["device-code", "api-key"] }
      controller.status = {
        warnings: ["CommandDeck skipped a Codex launcher shim. It found /usr/bin/codex. Sign-in will check this executable."]
      }
      surface.opened = true

      const device = findChild(surface, "agentLoginDeviceCode")
      const apiKey = findChild(surface, "agentLoginApiKey")
      const choose = findChild(surface, "agentChooseBackend")
      verify(choose !== null && choose.visible)
      verify(surface.codexPathWarning.indexOf("launcher shim") >= 0)
      tryVerify(function() { return choose.parent.parent.width > 0 && choose.parent.parent.height > 0 })
      tryCompare(device, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(apiKey, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(choose, "activeFocus", true)
      keyClick(Qt.Key_Space)
      compare(surface.backendSettingsCount, 1)

      surface.opened = false
      surface.opened = true
      tryVerify(function() { return choose.parent.parent.width > 0 && choose.parent.parent.height > 0 })
      tryCompare(device, "activeFocus", true)
      wait(20)
      mouseClick(choose)
      compare(surface.backendSettingsCount, 2)
      controller.status = null
      controller.loginError = "The provider is unavailable."
      verify(!choose.visible)
      controller.loginError = "The Codex binary is not owned by the current user or root."
      verify(choose.visible)
      surface.opened = false
    }
  }
}
