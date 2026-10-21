import QtQuick
import QtTest
import "../../qml/common" as Deck

DeadlineTestCase {
  name: "SearchRetention"
  Component { id: factory; Deck.SearchRetentionController { keepMilliseconds: 30000 } }
  property var retention: null

  function init() {
    retention = createTemporaryObject(factory, this)
    verify(retention !== null)
    retention.edited()
  }

  function snapshot(query) {
    return { query: query, revision: retention.currentRevision }
  }

  function test_each_pending_action_survives_other_completions() {
    const alpha = snapshot("alpha")
    retention.begin(1)
    retention.retainedSearch = alpha
    retention.edited()
    const beta = snapshot("beta")
    retention.begin(2)

    retention.complete(1, true)
    compare(retention.retainedSearch, null)
    verify(!retention.consumed)

    retention.retainedSearch = beta
    retention.edited()
    retention.restored(beta.revision)
    retention.complete(2, true)
    compare(retention.retainedSearch, null)
    verify(retention.consumed)

    retention.edited()
    retention.retainedSearch = snapshot("gamma")
    retention.complete(1, true)
    retention.complete(2, true)
    verify(!retention.consumed, "a result is consumed once")
    compare(retention.retainedSearch.query, "gamma")
  }

  function test_edits_after_restore_never_reuse_a_revision() {
    const alpha = snapshot("alpha")
    retention.begin(1)
    retention.retainedSearch = alpha
    retention.edited()
    const beta = snapshot("beta")
    retention.begin(2)
    retention.restored(alpha.revision)
    retention.retainedSearch = null
    retention.edited()
    retention.retainedSearch = snapshot("beta")

    retention.complete(1, true)
    retention.complete(2, true)
    compare(retention.retainedSearch.query, "beta")
    verify(!retention.consumed, "same query or filters after editing belong to a new search")
    verify(retention.currentRevision > beta.revision)
  }

  function test_failure_preserves_search_and_retires_association() {
    retention.begin(1)
    retention.retainedSearch = snapshot("alpha")
    retention.complete(1, false)
    retention.complete(1, true)
    compare(retention.retainedSearch.query, "alpha")
    verify(!retention.consumed)
    compare(retention.pending.length, 0)
  }

  function test_slow_success_consumes_current_search() {
    const closed = createTemporaryObject(factory, this)
    closed.edited()
    closed.begin(2)
    retention.begin(1)
    wait(119000)
    closed.retainedSearch = { query: "alpha", revision: closed.currentRevision }
    closed.edited()
    wait(2000)

    retention.complete(1, true)
    closed.complete(2, true)
    verify(retention.consumed, "success after two minutes still consumes the current search")
    compare(closed.retainedSearch, null, "success also invalidates a search kept shortly before two minutes")
  }

  function test_unreachable_revision_is_pruned_on_edit() {
    retention.begin(1)
    retention.edited()
    compare(retention.pending.length, 0)
    retention.begin(2)

    retention.complete(1, true)
    verify(!retention.consumed)
    retention.complete(2, true)
    verify(retention.consumed)
    compare(retention.pending.length, 0)
  }

  function test_kept_revision_survives_restore() {
    retention.begin(1)
    const kept = snapshot("alpha")
    retention.retainedSearch = kept
    retention.closed()
    retention.edited()
    retention.restored(kept.revision)
    retention.retainedSearch = null

    retention.complete(1, true)
    verify(retention.consumed)
  }

  function test_dropping_or_replacing_kept_search_prunes_association_data() {
    return [{ tag: "dropped", replace: false }, { tag: "replaced", replace: true }]
  }

  function test_dropping_or_replacing_kept_search_prunes_association(data) {
    retention.begin(1)
    retention.retainedSearch = snapshot("alpha")
    retention.edited()
    retention.begin(2)
    retention.retainedSearch = data.replace ? snapshot("beta") : null
    compare(retention.pending.length, 1)

    retention.complete(1, true)
    verify(!retention.consumed)
    retention.complete(2, true)
    verify(retention.consumed)
  }

  function test_kept_expiry_prunes_closed_revision_data() {
    return [{ tag: "closed", closed: true }, { tag: "current", closed: false }]
  }

  function test_kept_expiry_prunes_closed_revision(data) {
    retention.keepMilliseconds = 20
    retention.begin(1)
    retention.retainedSearch = snapshot("alpha")
    if (data.closed) retention.closed()
    waitForValue(retention, "retainedSearch", null)
    compare(retention.pending.length, data.closed ? 0 : 1)

    retention.complete(1, true)
    compare(retention.consumed, !data.closed)
  }
}
