.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

// What the shared confirmation asks, its confirm label, and whether confirming
// destroys data or state. An invoked action names its target so the prompt
// says which window, unit, or item it acts on.
function confirmationContent(root) {
  if (root.resetConfirmation) {
    return { prompt: "Reset launcher preferences, bookmarks, and aliases?", confirmLabel: "Reset", destructive: true }
  }
  if (root.savedSearchDeleteTarget) {
    return {
      prompt: "Delete saved search “" + root.savedSearchDeleteTarget.name + "”?",
      confirmLabel: "Delete",
      destructive: true
    }
  }
  if (root.aiTemplateDeleteTarget) {
    return {
      prompt: "Delete AI template “" + root.aiTemplateDeleteTarget.name + "”?",
      confirmLabel: "Delete",
      destructive: true
    }
  }

  const action = root.part_invocationController.pendingConfirmationAction
  if (!action || !action.title) return { prompt: "Run this action?", confirmLabel: "Run", destructive: false }

  const result = root.part_invocationController.pendingConfirmationResult
  const target = result && result.title ? " “" + result.title + "”" : ""
  return { prompt: action.title + target + "?", confirmLabel: action.title, destructive: action.isDestructive === true }
}

function cancelConfirmation(root) {
  root.resetConfirmation = false
  root.savedSearchDeleteTarget = null
  root.aiTemplateDeleteTarget = null
  root.part_invocationController.takeConfirmation()
  root.confirmationOpen = false
  root.focusRevision++
}

function acceptConfirmation(root) {
  if (root.resetConfirmation) {
    root.resetConfirmation = false
    root.confirmationOpen = false
    root.enqueuePersonalization(Protocol.Operations.CustomizationsReset, {
      confirmed: true
    }, { kind: Ui.MutationKind.Reset })
    return
  }
  if (root.savedSearchDeleteTarget) {
    const target = root.savedSearchDeleteTarget
    root.savedSearchDeleteTarget = null
    root.confirmationOpen = false
    root.enqueuePersonalization(Protocol.Operations.SavedSearchDelete, {
      savedSearchId: target.id,
      confirmed: true
    }, { kind: Ui.MutationKind.SavedSearch, id: target.id })
    return
  }
  if (root.aiTemplateDeleteTarget) {
    const target = root.aiTemplateDeleteTarget
    root.aiTemplateDeleteTarget = null
    root.confirmationOpen = false
    root.deleteAiTemplate(target)
    return
  }
  const selection = root.part_invocationController.takeConfirmation()
  root.confirmationOpen = false
  if (selection.result && selection.action) root.invokeAction(selection.result, selection.action, true)
}
