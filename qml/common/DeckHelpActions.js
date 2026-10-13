.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function announceStatus(root, value) {
  if (!value || value.length === 0) return
  try {
    if (typeof Accessible.announce === "function") Accessible.announce(value)
  } catch (error) {
    // Qt versions before Accessible announcements still expose the live
    // status text below, so absence of this API must not break the palette.
  }
}

function resultStatus(root, list) {
  if (!list || list.length === 0) return "No results"
  const executable = list.filter(result => result.actions && result.actions.length > 0)
  if (executable.length === 1 && executable[0].providerId === Protocol.ProviderIds.Fallback) {
    return "Web fallback available"
  }
  return list.length === 1 ? "1 result" : list.length + " results"
}

function maybeHandleFilterSentinel(root) {
  if (!root.opened || root.filterPaletteOpen) return
  if (root.part_navigation.activeFilters.length > 0) return
  if (root.part_navigation.queryText === "?") {
    root.part_navigation.queryText = ""
    root.queryInputRequested("")
    root.openFilterPalette()
  }
}

function closeFilterPalette(root, restoreFocus) {
  if (!root.filterPaletteOpen) return
  if (restoreFocus === false) root.filterFocusTarget = ""
  root.filterPaletteOpen = false
  if (!root.filterFocusTarget) root.focusRevision++
}

function openFilterPalette(root, invoker) {
  root.filterFocusTarget = invoker === Ui.FocusReturnControl.FooterPrefixes ? invoker : ""
  root.filterPaletteOpen = true
  root.actionPanelOpen = false
}

function insertFilterToken(root, token) {
  const value = String(token || "")
  if (value.length === 0) return
  root.filterPaletteOpen = false
  root.part_navigation.selectedIndex = 0
  root.queryInputRequested(value)
  root.requestSearch()
}

function askAssistant(root, carryQuery) {
  if (!root.refuseWhenAiOff()) return
  const carried = carryQuery === undefined
    ? root.part_navigation.queryText
    : String(carryQuery)
  const value = carried.trim().length > 0 ? "ai: " + carried : Ui.QueryPrefix.Ai
  root.filterPaletteOpen = false
  root.part_navigation.selectedIndex = 0
  root.queryInputRequested(value)
  root.requestSearch()
}

function resolveStateDir(root) {
  let base = ""
  try {
    const xdg = root.desktop.env("XDG_STATE_HOME")
    if (xdg && String(xdg).length > 0) {
      base = String(xdg)
    } else {
      const home = root.desktop.env("HOME")
      if (home && String(home).length > 0) base = String(home) + "/.local/state"
    }
  } catch (error) {
    base = ""
  }
  return base.length > 0 ? base + "/commanddeck" : ""
}

function closeHelp(root) {
  root.helpOpen = false
  root.indexInspectorOpen = false
  if (!root.helpFocusTarget) root.focusRevision++
}

function toggleHelp(root, invoker) {
  if (root.helpOpen) {
    root.closeHelp()
    return
  }
  root.helpFocusTarget = invoker === Ui.FocusReturnControl.FooterHelp ? invoker : ""
  root.helpOpen = true
  root.actionPanelOpen = false
}

function showRankingExplanation(root, result) {
  root.actionPanelOpen = false
  root.dialogTitle = "Why this result?"
  root.detailContent = root.rankingExplanationText(result)
  root.detailDialogRequested = true
}

function rankingExplanationText(root, result) {
  if (!root.hasRankingExplanation(result)) return "No ranking details are available."
  const ranking = result.ranking
  const lines = [
    String(result.title || "Result"),
    "Source: " + String(result.sourceId || "unknown"),
    "Final score: " + Number(ranking.finalScore || 0).toFixed(4),
    "",
    "Score contributions:"
  ]
  if (ranking.placement) {
    lines.splice(3, 0, "Placement: " + ranking.placement.tier + " — " + ranking.placement.reason)
  }
  const contributions = ranking.contributions || []
  for (let index = 0; index < contributions.length; index++) {
    const contribution = contributions[index]
    const value = Number(contribution.value || 0)
    lines.push("- " + String(contribution.label || contribution.kind || "Policy")
      + ": " + (value >= 0 ? "+" : "") + value.toFixed(4))
  }
  const matches = ranking.matches || []
  if (matches.length > 0) {
    lines.push("", "Matched fields:")
    for (let matchIndex = 0; matchIndex < matches.length; matchIndex++) {
      const match = matches[matchIndex]
      lines.push("- " + String(match.field || "field") + ": "
        + String(match.value || ""))
    }
  }
  const policies = ranking.policies || []
  if (policies.length > 0) {
    lines.push("", "Index policy:")
    for (let policyIndex = 0; policyIndex < policies.length; policyIndex++) {
      const policy = policies[policyIndex]
      const state = policy.included ? "included" : "excluded"
      const rule = policy.rule ? " (" + String(policy.rule) + ")" : ""
      lines.push("- " + String(policy.field || "field") + ": " + state
        + " — " + String(policy.reason || "policy") + rule)
    }
  }
  return lines.join("\n")
}

function hasRankingExplanation(root, result) {
  return result && result.ranking
    && ((result.ranking.contributions && result.ranking.contributions.length > 0)
      || (result.ranking.matches && result.ranking.matches.length > 0)
      || (result.ranking.policies && result.ranking.policies.length > 0))
}

function reservedAliasValues(root) {
  const values = [Ui.ReservedShortcut.Home, Ui.ReservedShortcut.Settings]
  for (let index = 0; index < root.filterDescriptors.length; index++) {
    const descriptor = root.filterDescriptors[index]
    const tokens = [descriptor.token].concat(descriptor.aliases || [])
    for (let tokenIndex = 0; tokenIndex < tokens.length; tokenIndex++) {
      values.push(String(tokens[tokenIndex]).replace(/:$/, "").toLowerCase())
    }
  }
  return values
}
