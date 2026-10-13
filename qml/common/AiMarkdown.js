.pragma library

// The bounded Markdown subset of AI answers: escaped text with bold and
// inline code, and fenced code. Links, images, and raw HTML stay literal.
// Every source line is its own paragraph and every fenced block one <pre>,
// so a streamed answer can insert finished blocks once and replace only its
// unfinished tail.

var EmptyLine = "<p style=\"-qt-paragraph-type:empty; margin:0\"><br/></p>"

// Rich text inserted at the end of a paragraph merges its first paragraph
// into that one. Leading with an empty paragraph makes that merge add
// nothing, so the inserted blocks keep their own formats.
var InsertionHead = EmptyLine

function escapeHtml(value) {
  return String(value || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/\"/g, "&quot;")
}

function renderInline(value) {
  let rendered = escapeHtml(value)
  // The bounded subset is intentionally small: emphasis and inline code.
  // Links, images, raw HTML, and nested constructs remain literal text.
  rendered = rendered.replace(
    /`([^`\n]{1,4096})`/g, "<code>$1</code>")
  rendered = rendered.replace(
    /\*\*([^*\n]{1,4096})\*\*/g, "<b>$1</b>")
  return rendered
}

function isBlank(line) {
  return line.trim().length === 0
}

function paragraph(line) {
  return isBlank(line) ? EmptyLine : "<p style=\"margin:0\">" + renderInline(line) + "</p>"
}

function codeBlock(lines) {
  return lines.length === 0 ? "" : "<pre>" + lines.map(escapeHtml).join("\n") + "</pre>"
}

// Splits text that starts at a line outside a fence into settled blocks and
// the tail. Finished blocks settle up to the last one that ends in visible
// text; blank lines and blocks after it stay in the tail with the unfinished
// line or open fence. Removing a tail after an empty paragraph would delete
// that paragraph and keep the tail's format, so a settled part never ends
// with one.
function split(text) {
  const blocks = []
  let settled = 0
  let index = 0
  let code = null

  for (;;) {
    const newline = text.indexOf("\n", index)
    if (newline < 0) break

    const line = text.slice(index, newline)
    index = newline + 1
    if (line.startsWith("```")) {
      if (code === null) {
        code = []
        continue
      }

      blocks.push({ html: codeBlock(code), end: index })
      if (code.length > 0 && !isBlank(code[code.length - 1])) settled = blocks.length
      code = null
    } else if (code !== null) {
      code.push(line)
    } else {
      blocks.push({ html: paragraph(line), end: index })
      if (!isBlank(line)) settled = blocks.length
    }
  }

  const rest = text.slice(index)
  let open = ""
  if (code !== null) {
    open = codeBlock(rest.length > 0 && !rest.startsWith("```") ? code.concat([rest]) : code)
  } else if (!isBlank(rest) && !rest.startsWith("```")) {
    open = paragraph(rest)
  }

  return {
    settledHtml: blocks.slice(0, settled).map(block => block.html).join(""),
    settledLength: settled > 0 ? blocks[settled - 1].end : 0,
    tailHtml: blocks.slice(settled).map(block => block.html).join("") + open
  }
}

function render(text) {
  const parts = split(String(text || ""))
  return parts.settledHtml + parts.tailHtml
}
