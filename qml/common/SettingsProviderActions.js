.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function synchronizeAiManagement(root) {
  root.part_aiEnableToggle.checked = root.aiDirectEnabled
}

function restoreAiFocus(root) {
  const invoker = root.aiEditorInvoker
  root.aiEditorInvoker = null
  if (invoker && invoker.visible && invoker.enabled) {
    invoker.forceActiveFocus()
    return
  }
  root.part_aiAddProvider.forceActiveFocus()
}

function toggleAiProviderEnabled(root, provider) {
  if (!root.aiManagementMutable) return false
  const copy = JSON.parse(JSON.stringify(provider))
  copy.enabled = provider.enabled === false
  root.aiProviderSaveRequested(copy, false, root.aiSettingsRevision, false)
  return true
}

function confirmAiProviderRemoval(root, provider, invoker) {
  if (!root.aiManagementMutable) return false
  root.aiEditorInvoker = invoker || null
  root.aiRemovalTarget = provider
  root.part_aiRemoveConfirmation.opened = true
  return true
}

function openAiProviderEditor(root, provider, invoker) {
  if (!root.aiManagementMutable) return false
  root.aiEditorInvoker = invoker || null
  if (provider) root.part_aiProviderEditor.openForEdit(provider)
  else root.part_aiProviderEditor.openForAdd()
  return true
}

function aiProviderRowSummary(root, provider) {
  const parts = [String(provider.kind || ""), root.aiProviderLocality(provider), root.aiProviderDestination(provider)]
  parts.push("model " + String(provider.model || ""))
  parts.push(provider.enabled === false ? "disabled" : "enabled")
  const test = root.aiRowTestStates[String(provider.id)]
  parts.push(test ? test : "not tested")
  return parts.join(" · ")
}

function aiProviderDestination(root, provider) {
  return String(provider.baseUrl || "").replace(/\/+$/, "")
}

function aiProviderLocality(root, provider) {
  const match = /^https?:\/\/(\[[^\]]+\]|[^\/:?#]+)/.exec(String(provider.baseUrl || ""))
  if (!match) return "unknown"
  const host = match[1]
  return host === "[::1]" || /^127\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(host) ? Protocol.AiLocality.Local : Protocol.AiLocality.Remote
}

function renderedAiProviderAt(root, index) {
  return index >= 0 && index < root.part_aiProviderRepeater.count
    ? root.part_aiProviderRepeater.itemAt(index)
    : null
}
