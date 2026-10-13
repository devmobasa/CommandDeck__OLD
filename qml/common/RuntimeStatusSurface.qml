import QtQuick
import "Protocol.js" as Protocol
import QtQuick.Layouts

Rectangle {
  id: root

  required property var theme
  property bool daemonAvailable: true
  property var status: null
  property bool reconciliationPending: false
  property string reconciliationWarning: ""
  signal inspectorRequested()

  readonly property var issues: collectIssues()
  readonly property int issueCount: issues.reduce((total, issue) => total + (issue.count || 1), 0)
  readonly property bool issueCountExact: !status || !status.inspection
    || status.inspection.workItemCountsExact !== false
  readonly property string summary: !daemonAvailable ? "Daemon offline"
    : reconciliationPending ? "Recovery in progress"
    : (issueCountExact ? "" : "At least ") + issueCount
      + (issues.some(issue => issue.service) ? " service issue" : " index issue")
      + (issueCount === 1 ? "" : "s")
  readonly property string details: issues.map(value => {
    const suffix = value.remediation ? " " + value.remediation : ""
    return value.title + (value.detail ? ": " + value.detail : "") + suffix
  }).join("\n")
  readonly property alias bannerText: statusText.text
  readonly property int maximumBannerHeight: Math.round(36 * root.theme.scale)

  function collectIssues() {
    const values = []
    if (!root.daemonAvailable) {
      values.push({
        title: "Daemon offline",
        detail: "Search and actions are temporarily unavailable.",
        remediation: "CommandDeck will reconnect automatically."
      })
    }
    if (root.reconciliationPending) {
      values.push({
        title: "Recovery in progress",
        detail: root.reconciliationWarning,
        remediation: "Wait for preferences and results to refresh."
      })
    }

    const indexing = root.status ? root.status.indexing : null
    const inspection = root.status ? root.status.inspection : null
    if (inspection && (inspection.failedItemCount > 0
        || inspection.staleItemCount > 0 || inspection.pendingItemCount > 0)) {
      const bound = inspection.workItemCountsExact === false ? "≥" : ""
      values.push({
        count: inspection.failedItemCount + inspection.staleItemCount + inspection.pendingItemCount,
        title: bound + inspection.failedItemCount + " failed · "
          + bound + inspection.staleItemCount + " stale · "
          + bound + inspection.pendingItemCount + " pending",
        detail: "Index work needs inspection.",
        remediation: "Open the index inspector for safe path and reason details."
      })
    }
    if (indexing) {
      if (indexing.reconciliationRequired === true) {
        values.push({
          title: "Index reconciliation required",
          detail: indexing.lastError || "The extractor or catalog generation changed.",
          remediation: "Run commanddeck reindex."
        })
      } else if (indexing.lastError) {
        values.push({
          title: "Index watcher degraded",
          detail: indexing.lastError,
          remediation: "Check indexed roots and daemon logs."
        })
      }
      const roots = indexing.roots || []
      const unwatched = (indexing.unwatchedRoots || []).filter(path =>
        !roots.some(value => value.path === path && (value.paused || (value.watched === false && value.lastError))))
      if (unwatched.length > 0) {
        values.push({
          count: unwatched.length,
          title: unwatched.length + " unwatched index root" + (unwatched.length === 1 ? "" : "s"),
          detail: unwatched.join(", "),
          remediation: "Check path availability and permissions."
        })
      }
      for (let rootIndex = 0; rootIndex < roots.length; rootIndex++) {
        const indexRoot = roots[rootIndex]
        if (indexRoot.paused === true) {
          values.push({
            title: "Index root paused",
            detail: indexRoot.path + " · " + indexRoot.pendingEventCount + " pending",
            remediation: "Resume this root from commanddeck index resume --root " + indexRoot.id + "."
          })
        } else if (indexRoot.watched === false && indexRoot.lastError) {
          values.push({
            title: "Index root unavailable",
            detail: indexRoot.path + ": " + indexRoot.lastError,
            remediation: indexRoot.remediation || "Check path availability and inotify limits."
          })
        }
      }
      const maintenance = indexing.maintenance
      if (maintenance && maintenance.lastError) {
        values.push({
          title: "Index maintenance failed",
          detail: maintenance.lastError,
          remediation: "Run commanddeck catalog check and inspect daemon logs."
        })
      }
    }

    const providers = root.status ? root.status.providers || [] : []
    for (let index = 0; index < providers.length; index++) {
      const provider = providers[index]
      if (provider.state !== Protocol.RuntimeHealthStates.Unavailable && provider.state !== Protocol.RuntimeHealthStates.Degraded) continue
      values.push({
        service: true,
        title: provider.label + " " + provider.state,
        detail: provider.detail || "",
        remediation: provider.remediation || "Check provider configuration."
      })
    }
    return values
  }

  visible: issues.length > 0
  implicitHeight: visible ? root.maximumBannerHeight : 0
  clip: true
  radius: root.theme.radiusSmall
  color: root.activeFocus ? root.theme.surfaceStrong : "transparent"
  border.width: root.activeFocus ? 1 : 0
  border.color: root.theme.focusRing
  Accessible.role: Accessible.Button
  Accessible.name: summary + ", Inspect"
  Accessible.description: details
  Accessible.onPressAction: root.inspectorRequested()
  activeFocusOnTab: visible

  Keys.onPressed: event => {
    if (event.modifiers === Qt.NoModifier
        && (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space)) {
      root.inspectorRequested()
      event.accepted = true
    }
  }

  TapHandler { onTapped: root.inspectorRequested() }
  HoverHandler { id: hover }
  ThemedToolTip {
    theme: root.theme
    visible: hover.hovered
    delay: 700
    text: "Inspect index and service issues"
  }

  RowLayout {
    anchors.fill: parent
    anchors.margins: Math.round(6 * root.theme.scale)
    Text {
      id: statusText
      Layout.fillWidth: true
      text: root.summary
      textFormat: Text.PlainText
      elide: Text.ElideRight
      color: root.daemonAvailable ? root.theme.textMuted : root.theme.urgent
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
    }
    Text {
      text: "Inspect →"
      color: root.theme.accent
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
    }
  }
}
