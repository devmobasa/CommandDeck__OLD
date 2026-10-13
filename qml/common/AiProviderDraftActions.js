.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function runTest(root) {
  if (root.testBusy || !root.directAiEnabled) return false
  const errors = root.fieldErrors
  // The model is not needed to list models; everything else must validate
  // before a connection is opened.
  delete errors.model
  if (Object.keys(errors).length > 0) {
    root.focusFirstInvalid()
    return false
  }
  const draft = root.draftProvider()
  if (draft.model.length === 0) draft.model = root.discoveryModelPlaceholder
  root.testRequested(draft)
  return true
}

function focusFirstInvalid(root) {
  const errors = root.fieldErrors
  if (errors.name) root.part_nameField.forceActiveFocus()
  else if (errors.baseUrl) root.part_baseUrlField.forceActiveFocus()
  else if (errors.remoteConsent) root.part_remoteConsentToggle.forceActiveFocus()
  else if (errors.credentialName) root.part_credentialNameField.forceActiveFocus()
  else if (errors.model) root.part_modelField.forceActiveFocus()
  else if (errors.contextLimit) root.part_contextField.forceActiveFocus()
  else if (errors.id) root.part_idField.forceActiveFocus()
}

function submit(root, selectAfterSave) {
  if (root.busy || !root.valid) {
    root.focusFirstInvalid()
    return false
  }
  root.saveRequested(root.draftProvider(), root.mode === root.editorModes.Add, selectAfterSave === true)
  return true
}

function requestCancel(root) {
  if (root.dirty) {
    root.part_discardDialog.opened = true
    return
  }
  root.close()
}

function markDirty(root) {
  if (!root.opened) return
  root.dirty = root.snapshot() !== root.original
}

function invalidateTest(root) {
  root.draftInvalidated()
}

function close(root) {
  root.part_picker.opened = false
  root.part_discardDialog.opened = false
  root.opened = false
  root.closed()
}

function openForEdit(root, provider) {
  root.mode = root.editorModes.Edit
  root.part_typeChoice.currentIndex = root.presetIndexFor(provider)
  root.part_idField.text = String(provider.id || "")
  root.part_nameField.text = String(provider.displayName || provider.id || "")
  root.part_baseUrlField.text = String(provider.baseUrl || "")
  const credential = provider.credential || ({})
  root.part_credentialKindChoice.currentIndex = Math.max(0, [Protocol.AiCredentialKinds.None, Protocol.AiCredentialKinds.Environment, Protocol.AiCredentialKinds.Systemd].indexOf(String(credential.kind || Protocol.AiCredentialKinds.None)))
  root.part_credentialNameField.text = String(credential.name || "")
  root.part_remoteConsentToggle.checked = provider.allowRemoteEndpoint === true
  root.part_modelField.text = String(provider.model || "")
  root.part_contextField.text = provider.contextLimit ? String(provider.contextLimit) : ""
  root.part_enabledToggle.checked = provider.enabled !== false
  root.original = root.snapshot()
  root.dirty = false
  root.invalidateTest()
  root.opened = true
  Qt.callLater(function() { root.part_nameField.forceActiveFocus() })
}

function openForAdd(root) {
  root.mode = root.editorModes.Add
  root.part_typeChoice.currentIndex = 0
  root.applyPreset()
  root.suggestId()
  root.part_modelField.text = ""
  root.part_contextField.text = ""
  root.part_enabledToggle.checked = true
  root.original = root.snapshot()
  root.dirty = false
  root.opened = true
  Qt.callLater(function() { root.part_typeChoice.forceActiveFocus() })
}

function suggestId(root) {
  if (root.mode !== root.editorModes.Add) return
  const preset = root.selectedPreset
  let base = preset ? String(preset.id) : root.customPresetId
  let candidate = base
  let counter = 2
  while ((root.existingIds || []).some(value => String(value).toLowerCase() === candidate.toLowerCase())
      || (root.reservedIds || []).some(value => String(value).toLowerCase() === candidate.toLowerCase())) {
    candidate = base + "-" + counter++
  }
  root.part_idField.text = candidate
}

function applyPreset(root) {
  const preset = root.selectedPreset
  if (preset) {
    if (root.mode === root.editorModes.Add || root.part_nameField.text.trim().length === 0) root.part_nameField.text = String(preset.displayName || "")
    root.part_baseUrlField.text = String(preset.baseUrl || "")
    root.part_credentialKindChoice.currentIndex = Math.max(0, [Protocol.AiCredentialKinds.None, Protocol.AiCredentialKinds.Environment, Protocol.AiCredentialKinds.Systemd].indexOf(String(preset.credentialKind || Protocol.AiCredentialKinds.None)))
    root.part_credentialNameField.text = String(preset.credentialName || "")
  } else {
    root.part_baseUrlField.text = ""
    root.part_credentialKindChoice.currentIndex = 1
    root.part_credentialNameField.text = ""
  }
  root.part_remoteConsentToggle.checked = false
  root.invalidateTest()
}

function presetIndexFor(root, provider) {
  const values = root.presets || []
  for (let index = 0; index < values.length; index++) {
    const preset = values[index]
    if (String(preset.kind) === String(provider.kind)
        && String(preset.baseUrl).replace(/\/+$/, "") === String(provider.baseUrl).replace(/\/+$/, "")) return index
  }
  for (let index = 0; index < values.length; index++) {
    if (String(values[index].kind) === Protocol.AiAdapterKinds.Ollama && String(provider.kind) === Protocol.AiAdapterKinds.Ollama) return index
  }
  return values.length
}

function snapshot(root) {
  return JSON.stringify(root.draftProvider())
}

function draftProvider(root) {
  const context = root.part_contextField.text.trim()
  const credentialKind = root.credentialKind
  return {
    id: root.part_idField.text.trim(),
    displayName: root.part_nameField.text.trim(),
    kind: root.kind,
    baseUrl: root.part_baseUrlField.text.trim().replace(/\/+$/, ""),
    model: root.part_modelField.text.trim(),
    credential: {
      kind: credentialKind,
      name: credentialKind === Protocol.AiCredentialKinds.None ? null : root.part_credentialNameField.text.trim()
    },
    allowRemoteEndpoint: root.remoteConsentNeeded && root.part_remoteConsentToggle.checked,
    enabled: root.part_enabledToggle.checked,
    contextLimit: context.length > 0 ? Number(context) : null
  }
}

function validate(root) {
  const errors = ({})
  const id = root.part_idField.text.trim()
  if (!/^[A-Za-z0-9._-]{1,64}$/.test(id)) {
    errors.id = "Use 1-64 ASCII letters, digits, dots, underscores, or hyphens."
  } else if ((root.reservedIds || []).some(value => String(value).toLowerCase() === id.toLowerCase())) {
    errors.id = "This ID is reserved for an assistant backend."
  } else if (root.mode === root.editorModes.Add
      && (root.existingIds || []).some(value => String(value).toLowerCase() === id.toLowerCase())) {
    errors.id = "A provider with this ID already exists."
  }
  const name = root.part_nameField.text.trim()
  if (name.length === 0 || name.length > 96) errors.name = "Enter a display name of up to 96 characters."
  const url = root.part_baseUrlField.text.trim()
  if (!/^https?:\/\/[^\s]+$/.test(url) || url.indexOf("?") >= 0 || url.indexOf("#") >= 0 || /\/\/[^\/]*@/.test(url)) {
    errors.baseUrl = "Enter an http(s) URL without user info, query, or fragment."
  } else if (!root.isLoopback) {
    if (!url.startsWith("https://")) errors.baseUrl = "Remote endpoints require HTTPS. Only literal loopback IPs may use plain HTTP."
    else if (!root.part_remoteConsentToggle.checked) errors.remoteConsent = "Confirm that requests may leave this machine."
  }
  if (root.credentialKind !== Protocol.AiCredentialKinds.None && !/^[A-Za-z0-9._-]{1,64}$/.test(root.part_credentialNameField.text.trim())) {
    errors.credentialName = "Enter the variable or systemd credential name (1-64 ASCII characters)."
  }
  if (root.part_modelField.text.trim().length === 0 || root.part_modelField.text.trim().length > 128) {
    errors.model = "Enter a model ID or choose one from a test."
  }
  const context = root.part_contextField.text.trim()
  if (context.length > 0 && !/^[1-9]\d{0,7}$/.test(context)) {
    errors.contextLimit = "Context limit must be a whole number up to 10000000, or empty."
  }
  return errors
}

function destinationText(root) {
  const url = root.part_baseUrlField.text.trim()
  return url.length > 0 ? url : "the endpoint"
}

function hostIsLiteralLoopback(root, url) {
  const match = /^https?:\/\/(\[[^\]]+\]|[^\/:?#]+)(:\d+)?/.exec(url)
  if (!match) return false
  const host = match[1]
  if (host === "[::1]") return true
  return /^127\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(host)
}
