import QtQml

QtObject {
  id: retention
  property int revisionSequence: 0
  property int currentRevision: 0
  property bool consumed: false
  property var retainedSearch: null
  property var pending: []
  property int keepMilliseconds: 0
  // QML timers use elapsed time, so wall-clock changes cannot alter retention.
  property Timer expiry: Timer {
    interval: Math.max(1, retention.keepMilliseconds)
    onTriggered: retention.retainedSearch = null
  }

  onRetainedSearchChanged: {
    expiry.stop()
    if (retainedSearch && keepMilliseconds > 0) expiry.start()
    prune()
  }
  onKeepMillisecondsChanged: {
    if (keepMilliseconds <= 0) retainedSearch = null
  }

  function edited() {
    currentRevision = ++revisionSequence
    consumed = false
    prune()
  }

  function restored(revision) {
    currentRevision = revision
    consumed = false
    prune()
  }

  function closed() {
    currentRevision = 0
    consumed = false
    prune()
  }

  function begin(requestId) {
    pending = pending.concat([{ requestId: requestId, revision: currentRevision }])
  }

  function complete(requestId, succeeded) {
    const record = pending.find(value => value.requestId === requestId)
    if (!record) return

    pending = pending.filter(value => value.requestId !== requestId)
    if (!succeeded) return

    if (record.revision === currentRevision) consumed = true
    if (retainedSearch && retainedSearch.revision === record.revision) retainedSearch = null
  }

  function prune() {
    // Reachable revisions outlive any backend deadline, including slow actions.
    pending = pending.filter(value => value.revision === currentRevision
      || (retainedSearch && value.revision === retainedSearch.revision))
  }
}
