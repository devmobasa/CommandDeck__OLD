import "AiProviderDraftActions.js" as AiProviderDraftActions
import "FocusTrap.js" as FocusTrap
import QtQuick
import "Protocol.js" as Protocol

// Modal editor for one direct AI provider definition.
//
// Add opens on the provider type; arrows choose and Enter continues to the
// display name. Edit opens on the display name. Test / fetch models is the
// only control that talks to the network, through the daemon, and only after
// the draft validates locally. Save stores the definition without any network
// traffic; Save and use also selects the provider/model pair. Escape closes
// the model chooser first, then asks to discard a dirty draft (Cancel focused),
// then cancels. Focus is trapped while open.
FocusScope {
  id: root
  readonly property alias part_typeChoice: editorScroll.part_typeChoice
  readonly property alias part_nameField: editorScroll.part_nameField
  readonly property alias part_baseUrlField: editorScroll.part_baseUrlField
  readonly property alias part_credentialKindChoice: editorScroll.part_credentialKindChoice
  readonly property alias part_credentialNameField: editorScroll.part_credentialNameField
  readonly property alias part_remoteConsentToggle: editorScroll.part_remoteConsentToggle
  readonly property alias part_testButton: editorScroll.part_testButton
  readonly property alias part_testCancelButton: editorScroll.part_testCancelButton
  readonly property alias part_modelField: editorScroll.part_modelField
  readonly property alias part_chooseModelButton: editorScroll.part_chooseModelButton
  readonly property alias part_contextField: editorScroll.part_contextField
  readonly property alias part_enabledToggle: editorScroll.part_enabledToggle
  readonly property alias part_idField: editorScroll.part_idField
  readonly property alias part_errorSummary: editorScroll.part_errorSummary
  readonly property alias part_testStatus: editorScroll.part_testStatus
  readonly property alias part_cancelButton: editorScroll.part_cancelButton
  readonly property alias part_saveButton: editorScroll.part_saveButton
  readonly property alias part_saveUseButton: editorScroll.part_saveUseButton
  readonly property alias part_discardDialog: discardDialog
  readonly property alias part_picker: picker

  readonly property var editorModes: Object.freeze({ Add: "add", Edit: "edit" })
  readonly property string customPresetId: "custom"
  // Discovery validates a full draft but does not send a generation request.
  readonly property string discoveryModelPlaceholder: "pending"


  required property var theme
  property bool opened: false
  property string mode: root.editorModes.Add
  property var presets: []
  property var reservedIds: []
  property var existingIds: []
  property bool directAiEnabled: false
  property bool busy: false
  property bool testBusy: false
  property var testResult: null
  property string errorText: ""
  readonly property bool settingsTabFence: true

  // Draft state. Field errors are recomputed on every change; the daemon stays
  // the authority and a rejected save keeps the draft with its summary.
  property string draftId: ""
  property string original: ""
  property bool dirty: false
  property int lastTestedDraftRevision: -1

  readonly property alias typeControl: root.part_typeChoice
  readonly property alias nameControl: root.part_nameField
  readonly property alias baseUrlControl: root.part_baseUrlField
  readonly property alias credentialKindControl: root.part_credentialKindChoice
  readonly property alias credentialNameControl: root.part_credentialNameField
  readonly property alias remoteConsentControl: root.part_remoteConsentToggle
  readonly property alias testControl: root.part_testButton
  readonly property alias testCancelControl: root.part_testCancelButton
  readonly property alias modelControl: root.part_modelField
  readonly property alias chooseModelControl: root.part_chooseModelButton
  readonly property alias modelPicker: picker
  readonly property alias contextControl: root.part_contextField
  readonly property alias enabledControl: root.part_enabledToggle
  readonly property alias idControl: root.part_idField
  readonly property alias cancelControl: root.part_cancelButton
  readonly property alias saveControl: root.part_saveButton
  readonly property alias saveUseControl: root.part_saveUseButton
  readonly property alias discardConfirmation: discardDialog
  readonly property alias errorControl: root.part_errorSummary
  readonly property alias testStatusControl: root.part_testStatus
  readonly property var editorFlickable: editorScroll.contentItem

  signal saveRequested(var provider, bool create, bool selectAfterSave)
  signal testRequested(var provider)
  signal testCancelRequested()
  signal draftInvalidated()
  signal closed()

  readonly property var typeOptions: {
    const values = (root.presets || []).map(preset => ({
      id: String(preset.id),
      title: String(preset.displayName || preset.id),
      description: String(preset.description || "")
    }))
    values.push({
      id: root.customPresetId,
      title: "OpenAI-compatible (custom URL)",
      description: "Chat Completions adapter only, not universal OpenAI API compatibility."
    })
    return values
  }

  readonly property var selectedPreset: {
    const option = root.part_typeChoice.currentOption
    if (!option || option.id === root.customPresetId) return null
    const values = root.presets || []
    for (let index = 0; index < values.length; index++) {
      if (String(values[index].id) === String(option.id)) return values[index]
    }
    return null
  }

  readonly property string kind: root.selectedPreset ? String(root.selectedPreset.kind) : Protocol.AiAdapterKinds.OpenAiChatCompletions
  readonly property bool isLoopback: root.hostIsLiteralLoopback(root.part_baseUrlField.text.trim())
  readonly property bool remoteConsentNeeded: root.part_baseUrlField.text.trim().length > 0 && !root.isLoopback
  readonly property string credentialKind: [Protocol.AiCredentialKinds.None, Protocol.AiCredentialKinds.Environment, Protocol.AiCredentialKinds.Systemd][Math.max(0, root.part_credentialKindChoice.currentIndex)]
  readonly property var fieldErrors: root.validate()
  readonly property bool valid: Object.keys(root.fieldErrors).length === 0
  readonly property var testModels: root.testResult && root.testResult.models ? root.testResult.models : []
  readonly property string testSummary: {
    if (root.testBusy) return "Testing " + root.destinationText() + "…"
    const result = root.testResult
    if (!result) return root.directAiEnabled
      ? "Test sends no prompt. It asks " + root.destinationText() + " for its model list and may send the configured credential."
      : "Direct AI is off, so Test is unavailable. You can still save a manual model ID."
    switch (String(result.outcome || "")) {
      case Protocol.AiProviderTestOutcomes.Models: return (result.models || []).length + " model(s) found. Choose one or keep a manual ID."
      case Protocol.AiProviderTestOutcomes.Empty: return root.kind === Protocol.AiAdapterKinds.Ollama
        ? "Connected, but Ollama has no models installed. Install one in Ollama, then test again."
        : "Connected, but no models were listed. Enter a model ID manually."
      case Protocol.AiProviderTestOutcomes.Unsupported: return "This endpoint does not list models. Enter a model ID manually."
      default: return "Test failed: " + (result.error ? String(result.error.summary || result.error.category || "") : "unknown error")
    }
  }

  visible: opened
  focus: opened
  Accessible.role: Accessible.Dialog
  Accessible.name: root.mode === root.editorModes.Add ? "Add AI provider" : "Edit AI provider"

  function hostIsLiteralLoopback(url) { return AiProviderDraftActions.hostIsLiteralLoopback(root, url) }

  function destinationText() { return AiProviderDraftActions.destinationText(root) }

  function validate() { return AiProviderDraftActions.validate(root) }

  function draftProvider() { return AiProviderDraftActions.draftProvider(root) }

  function snapshot() { return AiProviderDraftActions.snapshot(root) }

  function presetIndexFor(provider) { return AiProviderDraftActions.presetIndexFor(root, provider) }

  function applyPreset() { return AiProviderDraftActions.applyPreset(root) }

  function suggestId() { return AiProviderDraftActions.suggestId(root) }

  function openForAdd() { return AiProviderDraftActions.openForAdd(root) }

  function openForEdit(provider) { return AiProviderDraftActions.openForEdit(root, provider) }

  function close() { return AiProviderDraftActions.close(root) }

  // The owner clears testResult in response; assigning it here would break
  // the binding that delivers later results.
  function invalidateTest() { return AiProviderDraftActions.invalidateTest(root) }

  function markDirty() { return AiProviderDraftActions.markDirty(root) }

  function requestCancel() { return AiProviderDraftActions.requestCancel(root) }

  function submit(selectAfterSave) { return AiProviderDraftActions.submit(root, selectAfterSave) }

  function focusFirstInvalid() { return AiProviderDraftActions.focusFirstInvalid(root) }

  function runTest() { return AiProviderDraftActions.runTest(root) }

  function focusTargets() {
    const candidates = [root.part_typeChoice, root.part_nameField, root.part_baseUrlField, root.part_credentialKindChoice, root.part_credentialNameField,
      root.part_remoteConsentToggle, root.part_testButton, root.part_testCancelButton, root.part_modelField, root.part_chooseModelButton, root.part_contextField,
      root.part_enabledToggle, root.part_idField, root.part_cancelButton, root.part_saveButton, root.part_saveUseButton]
    return candidates.filter(item => item && item.visible && item.enabled)
  }

  function cycleFocus(forward) {
    FocusTrap.cycleTargets(root.focusTargets(), forward)
  }

  function ensureVisible(item) {
    if (!item || !root.opened) return
    const flickable = root.editorFlickable
    if (!flickable || !flickable.contentItem) return
    let current = item
    while (current && current !== flickable.contentItem && current !== root) current = current.parent
    if (current !== flickable.contentItem) return
    const top = item.mapToItem(flickable.contentItem, 0, 0).y
    const bottom = top + item.height
    const margin = Math.round(12 * root.theme.scale)
    if (top < flickable.contentY + margin) {
      flickable.contentY = Math.max(0, top - margin)
    } else if (bottom > flickable.contentY + flickable.height - margin) {
      flickable.contentY = Math.max(0, bottom - flickable.height + margin)
    }
  }

  property Connections focusWatcher: Connections {
    target: root.Window.window
    function onActiveFocusItemChanged() {
      root.ensureVisible(root.Window.window ? root.Window.window.activeFocusItem : null)
    }
  }

  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => {
    if (picker.opened || discardDialog.opened) return
    if (event.key === Qt.Key_Escape) {
      root.requestCancel()
      event.accepted = true
    } else if (event.key === Qt.Key_Tab || event.key === Qt.Key_Backtab) {
      root.cycleFocus(event.key === Qt.Key_Tab && !(event.modifiers & Qt.ShiftModifier))
      event.accepted = true
    } else if (event.key === Qt.Key_F && event.modifiers === Qt.ControlModifier) {
      // Keep the enclosing Settings search from stealing the editor's focus.
      if (root.testModels.length > 0) picker.opened = true
      event.accepted = true
    }
  }

  Rectangle {
    anchors.fill: parent
    color: root.theme.scrim

    MouseArea {
      anchors.fill: parent
      acceptedButtons: Qt.AllButtons
      hoverEnabled: true
    }
  }

  Rectangle {
    id: sheet
    width: Math.min(Math.round(680 * root.theme.scale), parent.width - Math.round(32 * root.theme.scale))
    height: Math.min(editorScroll.contentHeight + Math.round(48 * root.theme.scale), parent.height - Math.round(32 * root.theme.scale))
    anchors.centerIn: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.theme.border

    AiProviderForm {
      id: editorScroll
      host: root
    }
  }

  AiModelPicker {
    id: picker
    anchors.fill: parent
    z: 5
    theme: root.theme
    models: root.testModels
    currentModelId: root.part_modelField.text.trim()
    onSelected: modelId => {
      root.part_modelField.text = modelId
      picker.opened = false
      Qt.callLater(function() { root.part_modelField.forceActiveFocus() })
    }
    onDismissed: {
      picker.opened = false
      Qt.callLater(function() { root.part_chooseModelButton.visible ? root.part_chooseModelButton.forceActiveFocus() : root.part_modelField.forceActiveFocus() })
    }
  }

  ConfirmationSurface {
    id: discardDialog
    anchors.fill: parent
    z: 6
    theme: root.theme
    prompt: "Discard changes to this provider?"
    confirmLabel: "Discard"
    destructive: true
    onCanceled: {
      discardDialog.opened = false
      Qt.callLater(function() { root.part_cancelButton.forceActiveFocus() })
    }
    onAccepted: root.close()
  }
}
