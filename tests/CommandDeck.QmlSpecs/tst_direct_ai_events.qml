import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_direct_ai"
    function test_ai_preflight_stream_and_output_actions_use_real_events() {
      fixture.part_aiSurfaceController.begin({
        providerId: "cloud",
        modelId: "model-safe",
        userText: "Review exact text",
        attachments: []
      }, 100)
      compare(fixture.part_aiSurfaceController.sentRequests[0].operation, "ai.preflight")
      fixture.part_aiSurfaceController.handleMessage({
        type: Protocol.Messages.AiPreflightResult,
        requestId: 100,
        aiPreflight: fixture.part_aiPreflightSurface.preflight
      })
      fixture.part_aiPreflightSurface.opened = true
      nextTurn()
      const send = findChild(fixture.part_aiPreflightSurface, "aiPreflightSend")
      send.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_aiPreflightSurface.acceptCount, 1)
      compare(fixture.part_aiSurfaceController.sentRequests[1].operation, "ai.start")
      fixture.part_aiPreflightSurface.opened = false

      fixture.part_aiStreamingSurface.opened = true
      const start = fixture.part_aiSurfaceController.sentRequests[1]
      fixture.part_aiSurfaceController.handleMessage({
        type: Protocol.Messages.AiChunk,
        requestId: start.requestId,
        sessionId: "stale-session",
        aiChunk: { generation: 77, sequence: 1, text: "stale session output" }
      })
      fixture.part_aiSurfaceController.handleMessage({
        type: Protocol.Messages.AiChunk,
        requestId: start.requestId,
        sessionId: start.sessionId,
        aiChunk: { generation: 76, sequence: 1, text: "stale generation output" }
      })
      compare(fixture.part_aiSurfaceController.output, "")
      compare(fixture.part_aiSurfaceController.pendingOutput, "")
      fixture.part_aiSurfaceController.handleMessage({
        type: Protocol.Messages.AiChunk,
        requestId: start.requestId,
        sessionId: start.sessionId,
        aiChunk: { generation: 77, sequence: 1, text: "streamed output" }
      })
      fixture.part_aiSurfaceController.handleMessage({
        type: Protocol.Messages.AiDone,
        requestId: start.requestId,
        sessionId: start.sessionId,
        aiDone: {
          generation: 77,
          outcome: "completed",
          usage: { inputTokens: 3, outputTokens: 2 }
        }
      })
      compare(fixture.part_aiSurfaceController.output, "streamed output")
      nextTurn()
      const save = findChild(fixture.part_aiStreamingSurface, "aiOutputSave")
      waitForValue(fixture.part_aiSurfaceController, "state", "completed")
      fixture.part_aiStreamingSurface.saveDirectory = "/tmp"
      verify(save.visible && save.enabled)
      verify(fixture.part_aiStreamingSurface.defaultSavePath().indexOf("/tmp/commanddeck-answer-") === 0)
      fixture.part_aiStreamingSurface.saveTo("/tmp/commanddeck-ai-output.txt")
      compare(fixture.part_aiStreamingSurface.requestedAction, "save")
      compare(fixture.part_aiStreamingSurface.requestedPath, "/tmp/commanddeck-ai-output.txt")
      compare(fixture.part_aiSurfaceController.sentRequests[2].operation, "ai.output")

      fixture.part_aiSurfaceController.handleMessage({
        type: Protocol.Messages.ActionResult,
        requestId: fixture.part_aiSurfaceController.sentRequests[2].requestId,
        actionResult: {
          succeeded: false,
          message: "Confirm overwrite",
          confirmationToken: "server-owned-token"
        }
      })
      nextTurn()
      const overwrite = findChild(fixture.part_aiStreamingSurface, "aiOutputOverwrite")
      overwrite.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_aiStreamingSurface.requestedAction, "save")
      compare(fixture.part_aiStreamingSurface.requestedToken, "server-owned-token")

      fixture.part_aiSurfaceController.handleMessage({
        type: Protocol.Messages.ActionResult,
        requestId: fixture.part_aiSurfaceController.sentRequests[3].requestId,
        actionResult: { succeeded: true, message: "Saved" }
      })
      nextTurn()
      const open = findChild(fixture.part_aiStreamingSurface, "aiOutputOpen")
      const copy = findChild(fixture.part_aiStreamingSurface, "aiOutputCopy")
      verify(open.visible && open.enabled)
      verify(copy.visible && copy.enabled)
      open.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_aiStreamingSurface.requestedAction, "open")
      copy.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_aiStreamingSurface.requestedAction, "copy")

      fixture.part_aiSurfaceController.begin({
        providerId: "cloud",
        modelId: "model-safe",
        userText: "Cancel this request",
        attachments: []
      }, ++fixture.part_aiStreamingSurface.requestSequence)
      fixture.part_aiSurfaceController.handleMessage({
        type: Protocol.Messages.AiPreflightResult,
        requestId: fixture.part_aiSurfaceController.activeRequestId,
        aiPreflight: fixture.part_aiPreflightSurface.preflight
      })
      fixture.part_aiPreflightSurface.opened = true
      nextTurn()
      findChild(fixture.part_aiPreflightSurface, "aiPreflightSend").forceActiveFocus()
      keyClick(Qt.Key_Space)
      fixture.part_aiPreflightSurface.opened = false
      const cancelStart = fixture.part_aiSurfaceController.sentRequests[
        fixture.part_aiSurfaceController.sentRequests.length - 1]
      const cancelButton = findChild(fixture.part_aiStreamingSurface, "aiOutputCancel")
      cancelButton.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_aiStreamingSurface.cancelCount, 1)
      compare(fixture.part_aiSurfaceController.sentRequests[
        fixture.part_aiSurfaceController.sentRequests.length - 1].operation, "cancel")
      fixture.part_aiSurfaceController.handleMessage({
        type: Protocol.Messages.AiDone,
        requestId: cancelStart.requestId,
        sessionId: cancelStart.sessionId,
        aiDone: { generation: 77, outcome: "cancelled" }
      })
      compare(fixture.part_aiSurfaceController.state, "cancelled")
      nextTurn()
      const retryButton = findChild(fixture.part_aiStreamingSurface, "aiOutputRetry")
      retryButton.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_aiStreamingSurface.retryCount, 1)
      compare(fixture.part_aiSurfaceController.sentRequests[
        fixture.part_aiSurfaceController.sentRequests.length - 1].operation, "ai.preflight")
      fixture.part_aiSurfaceController.handleMessage({
        type: Protocol.Messages.Error,
        requestId: fixture.part_aiSurfaceController.activeRequestId,
        error: "provider unavailable"
      })
      compare(fixture.part_aiSurfaceController.state, "failed")

      const closeButton = findChild(fixture.part_aiStreamingSurface, "aiOutputClose")
      closeButton.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_aiStreamingSurface.dismissCount, 1)
      compare(fixture.part_aiSurfaceController.state, "idle")
      compare(fixture.part_aiSurfaceController.lastDraft, null)

      fixture.part_aiStreamingSurface.opened = true
      const failureCases = [
        { category: "authentication", summary: "Authentication failed" },
        { category: "rate-limited", summary: "Rate limited" },
        { category: "timeout", summary: "Provider timed out" }
      ]
      for (let index = 0; index < failureCases.length; index++) {
        fixture.part_aiSurfaceController.begin({
          providerId: "cloud",
          modelId: "model-safe",
          userText: "Failure case " + index,
          attachments: []
        }, ++fixture.part_aiStreamingSurface.requestSequence)
        fixture.part_aiSurfaceController.handleMessage({
          type: Protocol.Messages.AiPreflightResult,
          requestId: fixture.part_aiSurfaceController.activeRequestId,
          aiPreflight: fixture.part_aiPreflightSurface.preflight
        })
        fixture.part_aiSurfaceController.confirm(++fixture.part_aiStreamingSurface.requestSequence)
        const failureStart = fixture.part_aiSurfaceController.sentRequests[
          fixture.part_aiSurfaceController.sentRequests.length - 1]
        fixture.part_aiSurfaceController.handleMessage({
          type: Protocol.Messages.AiDone,
          requestId: failureStart.requestId,
          sessionId: failureStart.sessionId,
          aiDone: {
            generation: 77,
            outcome: "failed",
            error: failureCases[index]
          }
        })
        compare(fixture.part_aiSurfaceController.state, "failed")
        compare(fixture.part_aiSurfaceController.error, failureCases[index].summary)
      }
      fixture.part_aiSurfaceController.disconnect()
      compare(fixture.part_aiSurfaceController.state, "connection-lost")
      compare(fixture.part_aiSurfaceController.output, "")
      fixture.part_aiSurfaceController.clear()
      fixture.part_aiSurfaceController.disconnect()
      compare(fixture.part_aiSurfaceController.state, "idle")
      compare(fixture.part_aiSurfaceController.error, "")
      fixture.part_aiSurfaceController.begin({
        providerId: "cloud",
        modelId: "model-safe",
        userText: "After reconnect",
        attachments: []
      }, ++fixture.part_aiStreamingSurface.requestSequence)
      compare(fixture.part_aiSurfaceController.state, "preflighting")
      compare(fixture.part_aiSurfaceController.sentRequests[
        fixture.part_aiSurfaceController.sentRequests.length - 1].operation, "ai.preflight")
      fixture.part_aiStreamingSurface.opened = false
    }

    function test_settings_ai_selection_acknowledgement_uses_real_events() {
      fixture.part_settingsPanel.aiProviders = [
        { id: "local", displayName: "Local" },
        { id: "cloud", displayName: "Cloud" }
      ]
      fixture.part_settingsPanel.aiModels = [{ id: "model-safe", displayName: "Model Safe" }]
      fixture.part_settingsPanel.aiSelection = {
        providerId: "local",
        modelId: "model-safe",
        explanation: "Selection acknowledged"
      }
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("ai")
      nextTurn()
      fixture.part_settingsPanel.synchronizeAiChoices()
      compare(fixture.part_settingsPanel.aiProviderControl.currentIndex, 0)

      fixture.part_settingsPanel.aiProviderControl.forceActiveFocus()
      keyClick(Qt.Key_Down)
      compare(fixture.part_settingsPanel.aiModelsRequestedFor, "cloud")
      fixture.part_settingsPanel.aiModelControl.currentIndex = 0
      fixture.part_settingsPanel.aiSelectionSaveControl.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_settingsPanel.aiSelectionProviderRequested, "cloud")
      compare(fixture.part_settingsPanel.aiSelectionModelRequested, "model-safe")

      fixture.part_settingsPanel.aiSelectionBusy = true
      verify(!fixture.part_settingsPanel.aiSelectionSaveControl.enabled)
      fixture.part_settingsPanel.aiSelection = {
        providerId: "cloud",
        modelId: "model-safe",
        explanation: "Selection acknowledged"
      }
      fixture.part_settingsPanel.aiSelectionBusy = false
      fixture.part_settingsPanel.synchronizeAiChoices()
      compare(fixture.part_settingsPanel.aiProviderControl.currentIndex, 1)
      compare(fixture.part_settingsPanel.aiModelControl.currentIndex, 0)
      compare(fixture.part_settingsPanel.aiStatusControl.text, "Selection acknowledged")
    }
  }
}
