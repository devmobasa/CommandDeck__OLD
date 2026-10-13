.pragma library

// The daemon's match ranges over a text, as StyledText markup: the text is
// escaped and each matched run is bold in the accent colour. Ranges may come
// unsorted, overlapping, touching or past the end of the text; every covered
// character is highlighted once and no other character is.

function escapeMarkup(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
}

// StyledText only understands `<font color="#rrggbb">`, so drop any alpha
// channel Qt puts in front of the color.
function markupColor(value) {
  const text = value.toString()
  return text.length === 9 ? "#" + text.substring(3) : text
}

// Sorted, merged runs of covered characters, clamped to the text.
function matchedRuns(textLength, ranges) {
  const ordered = (ranges || [])
    .map(range => ({ start: Number(range.start), end: Number(range.start) + Number(range.length) }))
    .filter(range => Number.isFinite(range.start) && Number.isFinite(range.end))
    .sort((left, right) => left.start - right.start)
  const runs = []

  for (const range of ordered) {
    const start = Math.max(0, range.start)
    const end = Math.min(textLength, range.end)
    if (end <= start) continue

    const last = runs.length > 0 ? runs[runs.length - 1] : null
    if (last && start <= last.end) last.end = Math.max(last.end, end)
    else runs.push({ start: start, end: end })
  }

  return runs
}

function highlighted(value, ranges, accent) {
  const text = value === undefined || value === null ? "" : String(value)
  const open = "<font color=\"" + markupColor(accent) + "\"><b>"
  let output = ""
  let cursor = 0

  for (const run of matchedRuns(text.length, ranges)) {
    output += escapeMarkup(text.substring(cursor, run.start))
      + open + escapeMarkup(text.substring(run.start, run.end)) + "</b></font>"
    cursor = run.end
  }

  return output + escapeMarkup(text.substring(cursor))
}
