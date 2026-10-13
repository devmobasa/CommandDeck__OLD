import QtQml
import "Protocol.js" as Protocol

QtObject {
  id: root

  readonly property var sectionKeys: Object.freeze({
    OtherKind: "other", SectionPrefix: "section:", KindPrefix: "kind:"
  })

  required property var rows
  // The rows last written by syncRows, as plain objects in model order. The
  // ListModel copies nested values on every get(), so reads use these, and
  // they hold the latest result even where the model row was left untouched.
  // They are trusted while their count matches the model, and are released
  // when the model empties: closing the launcher or losing the daemon clears
  // it directly, and the cached results must not outlive the list.
  property var syncedRows: []

  property Connections modelWatcher: Connections {
    target: root.rows
    function onRowsRemoved() {
      if (root.rows.count === 0) root.syncedRows = []
    }
    function onModelReset() {
      if (root.rows.count === 0) root.syncedRows = []
    }
  }

  function sectionTitle(kind) {
    switch (kind) {
      case Protocol.ResultKinds.Application: return "Applications"
      case Protocol.ResultKinds.Omarchy: return "Omarchy"
      case Protocol.ResultKinds.Window: return "Windows"
      case Protocol.ResultKinds.Workspace: return "Workspaces"
      case Protocol.ResultKinds.Tab: return "Tabs"
      case Protocol.ResultKinds.File: return "Files"
      case Protocol.ResultKinds.Configuration: return "Configuration"
      case Protocol.ResultKinds.Calculator: return "Calculator"
      case Protocol.ResultKinds.Quicklink: return "Quicklinks"
      case Protocol.ResultKinds.CustomCommand: return "Commands"
      case Protocol.ResultKinds.AiCommand: return "AI"
      case Protocol.ResultKinds.Status: return "Status"
      case Protocol.ResultKinds.Mail: return "Mail"
      default: return kind
    }
  }

  function buildRows(list) {
    const order = []
    const groups = ({})
    const titles = ({})
    for (let index = 0; index < list.length; index++) {
      const result = list[index]
      const kind = result.kind || root.sectionKeys.OtherKind
      const key = result.section && result.section.id
        ? root.sectionKeys.SectionPrefix + result.section.id
        : root.sectionKeys.KindPrefix + kind
      if (!groups[key]) {
        groups[key] = []
        order.push(key)
        titles[key] = result.section && result.section.title
          ? result.section.title
          : sectionTitle(kind)
      }
      groups[key].push(result)
    }

    const built = []
    let ordinal = 0
    for (let group = 0; group < order.length; group++) {
      const key = order[group]
      const items = groups[key]
      built.push({
        isHeader: true,
        sectionTitle: titles[key],
        sectionCount: items.length,
        ordinal: 0,
        resultId: "",
        value: ({})
      })
      for (let item = 0; item < items.length; item++) {
        ordinal++
        built.push({
          isHeader: false,
          sectionTitle: "",
          sectionCount: 0,
          ordinal: ordinal <= 9 ? ordinal : 0,
          resultId: String(items[item].id || ""),
          value: items[item]
        })
      }
    }
    return built
  }

  // A model row is rewritten only when something its delegate shows changed,
  // so an unchanged refresh repaints nothing. The daemon issues new invocation
  // references on every search; they, and any other field a row does not
  // show, reach the selection through syncedRows.
  function syncRows(next) {
    const previous = syncedRows.length === rows.count ? syncedRows : []
    const kept = Math.min(rows.count, next.length)
    for (let index = 0; index < kept; index++) {
      if (!sameShownRow(previous[index], next[index])) rows.set(index, next[index])
    }

    if (rows.count > next.length) rows.remove(next.length, rows.count - next.length)
    else if (next.length > kept) rows.append(next.slice(kept))

    syncedRows = next
  }

  function sameShownRow(previous, next) {
    if (!previous || previous.isHeader !== next.isHeader) return false
    if (next.isHeader) {
      return previous.sectionTitle === next.sectionTitle && previous.sectionCount === next.sectionCount
    }

    return previous.ordinal === next.ordinal && previous.resultId === next.resultId
      && sameShownResult(previous.value, next.value)
  }

  // The fields ResultRow and ResultIcon read. Keep this list in step with them.
  function sameShownResult(previous, next) {
    const previousSection = previous.section || ({})
    const nextSection = next.section || ({})
    const previousMetadata = previous.metadata || ({})
    const nextMetadata = next.metadata || ({})
    return previous.title === next.title && previous.subtitle === next.subtitle
      && previous.kind === next.kind && previous.sourceId === next.sourceId
      && previous.icon === next.icon && previous.isFavorite === next.isFavorite
      && previousSection.title === nextSection.title
      && previousMetadata.herdrAgentStatus === nextMetadata.herdrAgentStatus
      && sameList(previous.aliases, next.aliases, (left, right) => left === right)
      && sameList(previous.matchedRanges, next.matchedRanges,
        (left, right) => left.start === right.start && left.length === right.length)
  }

  function sameList(previous, next, same) {
    const left = previous || []
    const right = next || []
    if (left.length !== right.length) return false

    for (let index = 0; index < left.length; index++) {
      if (!same(left[index], right[index])) return false
    }
    return true
  }

  function entryAt(index) {
    if (index < 0 || index >= rows.count) return null
    return syncedRows.length === rows.count ? syncedRows[index] : rows.get(index)
  }

  function firstSelectableIndex() {
    for (let index = 0; index < rows.count; index++) {
      if (!entryAt(index).isHeader) return index
    }
    return 0
  }

  function lastSelectableIndex() {
    for (let index = rows.count - 1; index >= 0; index--) {
      if (!entryAt(index).isHeader) return index
    }
    return 0
  }

  function indexOfOrdinal(number) {
    for (let index = 0; index < rows.count; index++) {
      const row = entryAt(index)
      if (!row.isHeader && row.ordinal === number) return index
    }
    return -1
  }

  function indexOfResultId(resultId) {
    for (let index = 0; index < rows.count; index++) {
      const row = entryAt(index)
      if (!row.isHeader && row.resultId === resultId) return index
    }
    return -1
  }

  function selectedResult(index) {
    const row = entryAt(index)
    return row && !row.isHeader ? row.value : null
  }

  function rowAt(index) {
    return entryAt(index)
  }

  function moveSelection(index, delta) {
    if (rows.count === 0) return index
    const step = delta > 0 ? 1 : -1
    let nextIndex = index
    let remaining = Math.abs(delta)
    while (remaining > 0) {
      let candidate = nextIndex + step
      while (candidate >= 0 && candidate < rows.count && entryAt(candidate).isHeader) {
        candidate += step
      }
      if (candidate < 0 || candidate >= rows.count) break
      nextIndex = candidate
      remaining--
    }
    return nextIndex
  }
}
