import QtQml
import "ActionMeaning.js" as ActionMeaning
import "Protocol.js" as Protocol

QtObject {
  id: invocation
  property int requestId: 0
  property var pendingConfirmationResult: null
  property var pendingConfirmationAction: null
  property bool actionInputOpen: false
  property var actionInputResult: null
  property var actionInputAction: null
  property bool actionInputConfirmed: false
  property bool refreshAfterAction: false
  property bool herdrEditPending: false
  // The kind of the running action: a copy that closes the launcher leaves
  // its message on screen, since nothing else shows that it happened.
  property string actionKind: ""
  readonly property bool busy: requestId > 0
  readonly property bool collecting: actionInputOpen || !!pendingConfirmationAction

  function requestConfirmation(result, action) {
    if (busy || collecting) return false
    pendingConfirmationResult = result
    pendingConfirmationAction = action
    return true
  }

  function takeConfirmation() {
    const selection = { result: pendingConfirmationResult, action: pendingConfirmationAction }
    pendingConfirmationResult = null
    pendingConfirmationAction = null
    return selection
  }

  function requestInput(result, action, confirmed) {
    if (busy || collecting) return false
    actionInputResult = result
    actionInputAction = action
    actionInputConfirmed = confirmed
    actionInputOpen = true
    return true
  }

  function takeInput() {
    const selection = { result: actionInputResult, action: actionInputAction,
      confirmed: actionInputConfirmed }
    cancelInput()
    return selection
  }

  function cancelInput() {
    actionInputOpen = false
    actionInputResult = null
    actionInputAction = null
    actionInputConfirmed = false
  }

  function retarget(next) {
    const target = pendingConfirmationResult || actionInputResult
    const action = pendingConfirmationAction || actionInputAction
    if (!target || !action) return true
    const fresh = next && next.id === target.id
      ? (next.actions || []).find(value => value.id === action.id) : null
    if (!ActionMeaning.same(action, fresh)) {
      takeConfirmation()
      cancelInput()
      return false
    }
    if (pendingConfirmationAction) {
      pendingConfirmationResult = next
      pendingConfirmationAction = fresh
    } else {
      actionInputResult = next
      // Equivalent inputs keep their delegates, entered values, and focus.
    }
    return true
  }

  function begin(nextRequestId, refresh, herdrEdit, kind) {
    if (busy || collecting || nextRequestId <= 0) return false
    requestId = nextRequestId
    refreshAfterAction = refresh
    herdrEditPending = herdrEdit
    actionKind = kind || ""
    return true
  }

  function settle(completedRequestId) {
    if (completedRequestId !== requestId || requestId === 0) return false
    requestId = 0
    refreshAfterAction = false
    herdrEditPending = false
    actionKind = ""
    return true
  }

  function complete(completedRequestId, outcome, hideAfterAction) {
    if (completedRequestId !== requestId || requestId === 0) return null

    const refresh = !!outcome && outcome.succeeded && refreshAfterAction
    const output = !!outcome && !!outcome.detailMarkdown
    const dismiss = !!outcome && outcome.succeeded && !refresh && !output && hideAfterAction
    const copied = actionKind === Protocol.ActionKinds.CopyText || actionKind === Protocol.ActionKinds.SnippetCopy
    settle(completedRequestId)

    const status = outcome && outcome.message
      ? outcome.message : (outcome && outcome.succeeded ? "Done" : "Action failed")
    return {
      status: status,
      refresh: refresh,
      output: output,
      dismiss: dismiss,
      hud: dismiss && copied ? status : ""
    }
  }

  function clear() {
    requestId = 0
    refreshAfterAction = false
    herdrEditPending = false
    actionKind = ""
    pendingConfirmationResult = null
    pendingConfirmationAction = null
    cancelInput()
  }
}
