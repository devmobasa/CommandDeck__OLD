.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function deleteAiTemplate(root, template) {
  root.aiSettingsError = ""
  root.aiTemplateBusy = true
  root.activeAiTemplatesRequestId = ++root.requestSequence
  root.part_protocol.send({ version: Protocol.Version, requestId: root.activeAiTemplatesRequestId,
    operation: Protocol.Operations.AiTemplatesDelete, aiTemplateId: template.id, confirmed: true })
}

function confirmDeleteAiTemplate(root, template) {
  root.aiTemplateDeleteTarget = template
  root.confirmationOpen = true
}

function saveAiTemplate(root, template) {
  if (root.aiTemplateBusy) return
  root.aiSettingsError = ""
  root.aiTemplateBusy = true
  root.activeAiTemplatesRequestId = ++root.requestSequence
  root.part_protocol.send({ version: Protocol.Version, requestId: root.activeAiTemplatesRequestId,
    operation: Protocol.Operations.AiTemplatesSave, aiTemplate: template })
}

function requestAiTemplates(root) {
  root.aiSettingsError = ""
  root.aiTemplateBusy = true
  root.activeAiTemplatesRequestId = ++root.requestSequence
  root.part_protocol.send({ version: Protocol.Version, requestId: root.activeAiTemplatesRequestId,
    operation: Protocol.Operations.AiTemplatesGet })
}

function updateAiSelection(root, providerId, modelId) {
  if (root.aiSelectionBusy) return
  root.aiSelectionBusy = true
  root.aiSettingsError = ""
  root.activeAiSelectionRequestId = ++root.requestSequence
  root.part_protocol.send({ version: Protocol.Version, requestId: root.activeAiSelectionRequestId,
    operation: Protocol.Operations.AiSelectionUpdate, providerId: providerId, modelId: modelId })
}

function requestAiModels(root, providerId) {
  root.aiSettingsError = ""
  root.aiModels = []
  root.activeAiModelsRequestId = ++root.requestSequence
  root.part_protocol.send({ version: Protocol.Version, requestId: root.activeAiModelsRequestId,
    operation: Protocol.Operations.AiModelsGet, providerId: providerId, refresh: true })
}

function requestAiSelection(root) {
  root.aiSelectionBusy = true
  root.activeAiSelectionRequestId = ++root.requestSequence
  root.part_protocol.send({ version: Protocol.Version, requestId: root.activeAiSelectionRequestId,
    operation: Protocol.Operations.AiSelectionGet })
}

function requestAiProviders(root) {
  root.activeAiProvidersRequestId = ++root.requestSequence
  root.part_protocol.send({ version: Protocol.Version, requestId: root.activeAiProvidersRequestId,
    operation: Protocol.Operations.AiProvidersGet })
}

function openSettings(root) {
  root.settingsOpen = true
  root.preferencesSaveError = ""
  root.helpOpen = false
  root.actionPanelOpen = false
  root.agentOpen = false
  root.requestPreferencesSnapshot()
  root.requestAiProviders()
  root.requestAiSelection()
  root.requestAiTemplates()
  root.part_aiSettingsController.load(++root.requestSequence)
  root.requestAgentSettings()
}

function requestBootstrap(root) {
  root.activeBootstrapRequestId = ++root.requestSequence
  root.part_protocol.send({
    version: Protocol.Version,
    requestId: root.activeBootstrapRequestId,
    operation: Protocol.Operations.Bootstrap
  })
}
