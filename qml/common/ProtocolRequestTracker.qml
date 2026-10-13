import QtQml

QtObject {
  property int sequence: 0
  property int aiRequestId: 0
  property int previewRequestId: 0
  property int bootstrapRequestId: 0
  property int preferencesRequestId: 0
  property int statusRequestId: 0

  function clearInteractionRequests() {
    aiRequestId = 0
    previewRequestId = 0
  }

  function clearAllRequests() {
    clearInteractionRequests()
    bootstrapRequestId = 0
    preferencesRequestId = 0
    statusRequestId = 0
  }
}
