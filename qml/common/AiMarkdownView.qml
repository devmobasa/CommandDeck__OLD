import QtQuick
import QtQuick.Controls
import "AiMarkdown.js" as Markdown

// Renders an AI answer's bounded Markdown subset. A streamed answer only
// grows, so each update inserts newly finished blocks once and replaces only
// the unfinished tail; resetting the whole rich text on every 50 ms flush made
// main-thread cost grow with the answer. A hidden view renders when shown.
TextArea {
  id: root

  required property var theme
  property string sourceText: ""
  readonly property int maximumInlineSpan: 4096
  // Source characters the latest update rendered: newly settled blocks and
  // the tail. It stays near one block while an answer streams.
  property int lastRenderedLength: 0
  readonly property bool shown: root.visible && root.Window.visibility !== Window.Hidden

  readOnly: true
  textFormat: TextEdit.RichText
  // Words stay whole; only an unbreakable run (a long URL) splits mid-word.
  wrapMode: TextEdit.WrapAtWordBoundaryOrAnywhere
  selectByMouse: true
  color: root.theme.text
  Accessible.name: "Generated AI output"
  Accessible.description: sourceText

  onSourceTextChanged: root.refresh()
  onShownChanged: root.refresh()

  QtObject {
    id: rendered

    // The source the document shows, and its settled prefix.
    property string source: ""
    property string settledSource: ""
    // Document position where the tail starts; -1 while nothing is settled.
    property int tailStart: -1
  }

  function escapeHtml(value) { return Markdown.escapeHtml(value) }

  function renderInline(value) { return Markdown.renderInline(value) }

  function render(value) { return Markdown.render(value) }

  function refresh() {
    const source = root.sourceText
    if (!root.shown || source === rendered.source) return

    if (rendered.tailStart >= 0 && source.startsWith(rendered.settledSource)) root.renderTail(source)
    else root.renderAll(source)
    rendered.source = source
  }

  function renderAll(source) {
    const parts = Markdown.split(source)
    root.lastRenderedLength = source.length
    if (parts.settledLength === 0) {
      root.text = parts.tailHtml
      rendered.settledSource = ""
      rendered.tailStart = -1
      return
    }

    root.text = parts.settledHtml
    rendered.settledSource = source.slice(0, parts.settledLength)
    rendered.tailStart = root.length
    if (parts.tailHtml.length > 0) root.insert(rendered.tailStart, Markdown.InsertionHead + parts.tailHtml)
  }

  function renderTail(source) {
    const settledLength = rendered.settledSource.length
    const parts = Markdown.split(source.slice(settledLength))
    root.lastRenderedLength = source.length - settledLength
    root.remove(rendered.tailStart, root.length)
    if (parts.settledLength > 0) {
      root.insert(rendered.tailStart, Markdown.InsertionHead + parts.settledHtml)
      rendered.settledSource = source.slice(0, settledLength + parts.settledLength)
      rendered.tailStart = root.length
    }

    if (parts.tailHtml.length > 0) root.insert(rendered.tailStart, Markdown.InsertionHead + parts.tailHtml)
  }
}
