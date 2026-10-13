.pragma library

// Daemon management errors carry their category as a "category: summary"
// prefix (AI provider settings, assistant preferences). A plain message has
// no category, and its whole text is the summary.
function split(text) {
  const value = String(text || "")
  const separator = value.indexOf(": ")
  if (separator > 0 && separator <= 32 && /^[a-z-]+$/.test(value.slice(0, separator))) {
    return { category: value.slice(0, separator), summary: value.slice(separator + 2) }
  }
  return { category: "", summary: value }
}
