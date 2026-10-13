.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function saveAiTemplate(root) {
  const id = root.part_aiTemplateId.text.trim()
  const name = root.part_aiTemplateName.text.trim()
  const user = root.part_aiTemplateUser.text
  if (!/^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/.test(id)
      || name.length < 1
      || user.indexOf("{{input}}") < 0) return false
  root.aiTemplateSaveRequested({
    id: id,
    name: name,
    workflowKind: root.editingAiTemplate && root.editingAiTemplate.workflowKind
      ? root.editingAiTemplate.workflowKind : root.templateDefaults.Workflow,
    systemTemplate: root.part_aiTemplateSystem.text,
    userTemplate: user,
    parameters: [root.templateDefaults.Parameter],
    position: root.editingAiTemplate ? Number(root.editingAiTemplate.position || 0) : 0
  })
  root.editingAiTemplate = null
  return true
}

function editAiTemplate(root, template) {
  root.editingAiTemplate = template
  root.part_aiTemplateId.text = template ? template.id || "" : ""
  root.part_aiTemplateName.text = template ? template.name || "" : ""
  root.part_aiTemplateSystem.text = template ? template.systemTemplate || "" : ""
  root.part_aiTemplateUser.text = template ? template.userTemplate || "{{input}}" : "{{input}}"
  root.part_aiTemplateUser.forceActiveFocus()
}

function synchronizeAiChoices(root) {
  if (!root.aiSelection) return
  const providerIndex = root.indexOfId(root.aiProviders, root.aiSelection.providerId)
  if (providerIndex >= 0) root.part_aiProviderChoice.currentIndex = providerIndex
  const modelIndex = root.indexOfId(root.aiModels, root.aiSelection.modelId)
  if (modelIndex >= 0) root.part_aiModelChoice.currentIndex = modelIndex
}

function indexOfId(root, values, id) {
  for (let index = 0; index < (values || []).length; index++) {
    if (values[index].id === id) return index
  }
  return -1
}
