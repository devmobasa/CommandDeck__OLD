import QtQuick
import Quickshell
import "." as CommandDeck

CommandDeck.IndexInspector {
  required property var host

  id: indexInspector
  parent: host.part_host
  anchors.fill: host.part_host
  opened: false
  theme: host.part_theme
  pageSize: 2
  inspection: ({
    stats: { itemCount: 8 },
    totalExclusionDiagnosticCount: 2,
    failedItemCount: 1,
    staleItemCount: 1,
    pendingItemCount: 1,
    workItems: [
      { state: "failed", root: "/safe", relativePath: "a.txt", reason: "read-failed" },
      { state: "stale", root: "/safe", relativePath: "b.txt", reason: "version" },
      { state: "pending", root: "/safe", relativePath: "c.txt", reason: "event" }
    ]
  })
}
