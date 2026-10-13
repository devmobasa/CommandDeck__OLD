.pragma library
.import "Protocol.js" as Protocol

// Wording shared by the clipboard browser rows, preview, empty states, and
// specs. Functions are pure: callers pass the clock so ages stay testable.

var dayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]
var monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]

function kindOf(entry) {
  return entry && entry.presentation && entry.presentation.kind ? String(entry.presentation.kind) : Protocol.ClipboardPresentationKinds.Text
}

function kindName(kind) {
  if (kind === Protocol.ClipboardPresentationKinds.Links) return "Link"
  if (kind === Protocol.ClipboardPresentationKinds.Images) return "Image"
  if (kind === Protocol.ClipboardPresentationKinds.Files) return "File"
  return "Text"
}

function kindGlyph(kind) {
  if (kind === Protocol.ClipboardPresentationKinds.Links) return "↗"
  if (kind === Protocol.ClipboardPresentationKinds.Images) return "▣"
  if (kind === Protocol.ClipboardPresentationKinds.Files) return "▤"
  return "¶"
}

function calendarDaysBetween(earlier, later) {
  const start = new Date(earlier.getFullYear(), earlier.getMonth(), earlier.getDate())
  const end = new Date(later.getFullYear(), later.getMonth(), later.getDate())
  return Math.round((end.getTime() - start.getTime()) / 86400000)
}

function shortDate(date, now) {
  const text = monthNames[date.getMonth()] + " " + date.getDate()
  return date.getFullYear() === now.getFullYear() ? text : text + ", " + date.getFullYear()
}

// Compact age for list rows: "now", "4m", "3h", "Yesterday", a weekday within
// the past week, then a short date.
function compactAge(copiedSeconds, nowSeconds) {
  const age = Math.max(0, nowSeconds - copiedSeconds)
  if (age < 60) return "now"
  if (age < 3600) return Math.floor(age / 60) + "m"
  if (age < 12 * 3600) return Math.floor(age / 3600) + "h"

  const copied = new Date(copiedSeconds * 1000)
  const now = new Date(nowSeconds * 1000)
  const days = calendarDaysBetween(copied, now)
  if (days <= 0) return Math.floor(age / 3600) + "h"
  if (days === 1) return "Yesterday"
  if (days < 7) return dayNames[copied.getDay()]
  return shortDate(copied, now)
}

// Readable copy time for the preview header, in the locale's short time format.
function copiedLabel(copiedSeconds, nowSeconds) {
  const copied = new Date(copiedSeconds * 1000)
  const now = new Date(nowSeconds * 1000)
  const days = calendarDaysBetween(copied, now)
  const time = copied.toLocaleTimeString(Qt.locale(), 1)

  if (days <= 0) return "Today, " + time
  if (days === 1) return "Yesterday, " + time
  if (days < 7) return dayNames[copied.getDay()] + " " + shortDate(copied, now) + ", " + time
  return shortDate(copied, now) + ", " + time
}

function fullTimestamp(copiedSeconds) {
  return new Date(copiedSeconds * 1000).toLocaleString(Qt.locale(), 0)
}

function entryCountLabel(count, hasMore) {
  if (hasMore) return count + "+ entries"
  return count === 1 ? "1 entry" : count + " entries"
}

// "active" while the watcher records copies, "idle" when capture is off or
// paused, "attention" when the user must review, and "warning" otherwise.
function captureTone(status) {
  if (!status) return "idle"
  if (status.indexOf("Capturing") === 0) return "active"
  if (status === "Disabled" || status === "Paused") return "idle"
  if (status === "MigrationReviewRequired") return "attention"
  return "warning"
}

function captureLabel(status) {
  if (!status) return "Checking capture…"
  if (status === "Disabled") return "Capture off"
  if (status === "Paused") return "Capture paused"
  if (status === "MigrationReviewRequired") return "Review required"
  return status
}
