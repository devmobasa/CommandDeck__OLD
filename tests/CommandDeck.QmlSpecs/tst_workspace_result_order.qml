import QtQuick
import QtTest
import "../../qml/common/WorkspaceResultOrder.js" as WorkspaceOrder
import "../../qml/common/Protocol.js" as Protocol

TestCase {
  name: "WorkspaceResultOrder"

  function workspace(id, score) {
    return {
      id: "workspace:" + id,
      kind: "workspace",
      providerId: Protocol.ProviderIds.Hyprland,
      sourceId: "windows-workspaces",
      title: "Workspace " + id,
      score: score,
      metadata: { workspaceId: String(id), workspaceName: String(id) }
    }
  }

  function test_recent_use_then_score_then_numeric_name() {
    const status = { id: "status", kind: "status", title: "Status", score: 1 }
    const results = [workspace(10, 0.1), status, workspace(2, 0.1),
      workspace(3, 0.2), workspace(1, 0.2)]

    compare(WorkspaceOrder.order(results, []).map(value => value.id).join("|"),
      "workspace:1|status|workspace:3|workspace:2|workspace:10")
    compare(WorkspaceOrder.order(results, ["10", "3"]).map(value => value.id).join("|"),
      "workspace:10|status|workspace:3|workspace:1|workspace:2")
  }

  function test_recent_history_is_bounded_and_unique() {
    compare(WorkspaceOrder.noteRecent(["2", "1", "2"], 3, 3).join("|"), "3|2|1")
    compare(WorkspaceOrder.noteRecent(["2", "1"], 2, 3).join("|"), "2|1")
  }

  function test_other_workspace_providers_keep_their_positions() {
    const other = workspace(8, 0)
    other.providerId = "extension"
    other.sourceId = "extension-workspaces"
    const results = [workspace(2, 0), other, workspace(1, 0)]

    compare(WorkspaceOrder.order(results, ["1"]).map(value => value.id).join("|"),
      "workspace:1|workspace:8|workspace:2")
  }
}
