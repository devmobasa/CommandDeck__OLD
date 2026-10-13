.pragma library
.import "Protocol.js" as Protocol

function stringValue(value) {
  return String(value === undefined || value === null ? "" : value)
}

function noteRecent(ids, workspaceId, maximum) {
  const key = stringValue(workspaceId).trim()
  const result = key.length > 0 ? [key] : []
  const values = ids || []
  const limit = Math.max(1, Number(maximum) || 100)
  for (let index = 0; index < values.length && result.length < limit; index++) {
    const candidate = stringValue(values[index]).trim()
    if (candidate.length > 0 && result.indexOf(candidate) < 0) result.push(candidate)
  }
  return result
}

function workspaceId(result) {
  if (!result || result.kind !== Protocol.ResultKinds.Workspace || result.providerId !== Protocol.ProviderIds.Hyprland
      || result.sourceId !== Protocol.SourceIds.WindowsWorkspaces || !result.metadata) return ""
  return stringValue(result.metadata.workspaceId).trim()
}

function resultScore(result) {
  const value = Number(result ? result.score : 0)
  return isFinite(value) ? value : 0
}

function workspaceNumber(result) {
  if (!result || !result.metadata) return Number.MAX_SAFE_INTEGER
  const name = stringValue(result.metadata.workspaceName).trim()
  if (!/^[1-9][0-9]*$/.test(name)) return Number.MAX_SAFE_INTEGER
  const number = Number(name)
  return Number.isSafeInteger(number) ? number : Number.MAX_SAFE_INTEGER
}

function order(results, recentIds) {
  const values = (results || []).slice()
  const recent = recentIds || []
  const workspaces = []
  for (let index = 0; index < values.length; index++) {
    const id = workspaceId(values[index])
    if (id.length > 0) workspaces.push({ result: values[index], index: index, id: id })
  }

  workspaces.sort(function(left, right) {
    let leftRecent = recent.indexOf(left.id)
    let rightRecent = recent.indexOf(right.id)
    if (leftRecent < 0) leftRecent = recent.length
    if (rightRecent < 0) rightRecent = recent.length
    if (leftRecent !== rightRecent) return leftRecent - rightRecent

    const scoreDelta = resultScore(right.result) - resultScore(left.result)
    if (scoreDelta !== 0) return scoreDelta

    const numberDelta = workspaceNumber(left.result) - workspaceNumber(right.result)
    return numberDelta || left.index - right.index
  })

  let workspaceIndex = 0
  return values.map(function(result) {
    return workspaceId(result).length > 0 ? workspaces[workspaceIndex++].result : result
  })
}
