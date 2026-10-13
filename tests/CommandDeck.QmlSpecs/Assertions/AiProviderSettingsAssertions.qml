import QtQuick
import "Protocol.js" as Protocol
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(){
    host.part_aiSettingsSpec.load(11)
    host.expect(host.part_aiSettingsSpec.sent[0].operation === Protocol.Operations.AiSettingsGet
        && host.part_aiSettingsSpec.sent[0].version === 3 && host.part_aiSettingsSpec.status === "loading",
      "loading provider settings sends one typed request")
    host.part_aiSettingsSpec.handleMessage({ type: Protocol.Messages.AiSettingsResult, requestId: 11, aiSettings: {
      enabled: true, revision: 4, source: "managed-document", providers: [{ id: "p" }],
      presets: [{ id: "ollama" }], reservedIds: ["codex.local"] } })
    host.part_aiSettingsSpec.handleMessage({ type: Protocol.Messages.Done, requestId: 11 })
    host.expect(host.part_aiSettingsSpec.revision === 4 && host.part_aiSettingsSpec.enabled && host.part_aiSettingsSpec.status === "idle"
        && host.part_aiSettingsSpec.providers.length === 1 && host.part_aiSettingsSpec.reservedIds.length === 1,
      "an acknowledged settings result becomes the snapshot")

    host.part_aiSettingsSpec.invalidateDraft()
    host.part_aiSettingsSpec.testDraft(12, { id: "d" })
    const testRequest = host.part_aiSettingsSpec.sent[host.part_aiSettingsSpec.sent.length - 1]
    host.expect(testRequest.operation === Protocol.Operations.AiProvidersTest && testRequest.draftRevision === 1
        && testRequest.expectedRevision === 4 && host.part_aiSettingsSpec.testBusy,
      "a draft test carries the draft and settings revisions")
    host.part_aiSettingsSpec.handleMessage({ type: Protocol.Messages.AiProviderTestResult, requestId: 12,
      aiProviderTest: { draftRevision: 0, outcome: "models", models: [{ id: "old" }] } })
    host.expect(host.part_aiSettingsSpec.testResult === null,
      "a late test reply for an older draft revision is discarded")
    host.part_aiSettingsSpec.handleMessage({ type: Protocol.Messages.AiProviderTestResult, requestId: 12,
      aiProviderTest: { draftRevision: 1, outcome: "models", models: [{ id: "new" }] } })
    host.part_aiSettingsSpec.handleMessage({ type: Protocol.Messages.Done, requestId: 12 })
    host.expect(host.part_aiSettingsSpec.testResult.models[0].id === "new" && !host.part_aiSettingsSpec.testBusy,
      "the matching draft revision populates the test result and clears busy")

    host.expect(host.part_aiSettingsSpec.saveProvider(13, { id: "p" }, false, true),
      "a save is accepted while idle")
    const saveRequest = host.part_aiSettingsSpec.sent[host.part_aiSettingsSpec.sent.length - 1]
    host.expect(saveRequest.operation === Protocol.Operations.AiProvidersSave && saveRequest.expectedRevision === 4
        && saveRequest.selectAfterSave === true && saveRequest.create === false && host.part_aiSettingsSpec.busy,
      "a save carries the expected revision and the explicit select-after-save flag")
    host.expect(!host.part_aiSettingsSpec.saveProvider(14, { id: "q" }, true, false),
      "a second mutation is refused while one is in flight")
    host.part_aiSettingsSpec.handleMessage({ type: Protocol.Messages.Error, requestId: 13, error: "conflict: The settings changed on disk." })
    host.part_aiSettingsSpec.handleMessage({ type: Protocol.Messages.Done, requestId: 13 })
    host.expect(host.part_aiSettingsSpec.status === "conflict" && host.part_aiSettingsSpec.errorCategory === "conflict"
        && host.part_aiSettingsSpec.errorText === "The settings changed on disk." && !host.part_aiSettingsSpec.busy,
      "a categorised daemon error becomes a conflict state with its summary")

    host.part_aiSettingsSpec.saveProvider(15, { id: "p" }, false, true)
    host.part_aiSettingsSpec.handleMessage({ type: Protocol.Messages.AiProviderSaveResult, requestId: 15, aiProviderSave: {
      providerId: "p", settings: { enabled: true, revision: 5, source: "managed-document", providers: [{ id: "p" }] },
      selectionRequested: true, selectionApplied: false, selectionError: { category: "disabled", summary: "off" } } })
    host.expect(host.part_aiSettingsSpec.status === "saved-selection-failed" && host.part_aiSettingsSpec.revision === 5
        && host.part_aiSettingsSpec.lastSave.selectionError.category === "disabled",
      "a committed provider with a failed selection is reported as saved-but-not-selected")

    host.part_aiSettingsSpec.load(16)
    host.part_aiSettingsSpec.handleMessage({ type: Protocol.Messages.Error, requestId: 16, error: "Unknown operation 'ai.settings.get'." })
    host.expect(!host.part_aiSettingsSpec.supported && host.part_aiSettingsSpec.status === "unsupported",
      "an older daemon yields an explicit unsupported management state")
    host.part_aiSettingsSpec.saveProvider(17, { id: "p" }, false, false)
    host.part_aiSettingsSpec.disconnect()
    host.expect(host.part_aiSettingsSpec.status === "connection-lost" && !host.part_aiSettingsSpec.busy
        && !host.part_aiSettingsSpec.handleMessage({ type: Protocol.Messages.AiProviderSaveResult, requestId: 17, aiProviderSave: {} }),
      "a dropped bridge marks the outcome unknown and ignores late replies")
  }
}
