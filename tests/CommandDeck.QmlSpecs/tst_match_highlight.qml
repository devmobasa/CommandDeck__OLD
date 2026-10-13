import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// The detail pane's matched fields highlight the daemon's match ranges: each
// covered character once, never one outside a range, with the text escaped.
// Result titles use the same highlighter (ResultListAssertions under qs).
TestCase {
  name: "MatchHighlight"
  when: windowShown
  width: 480
  height: 240

  CommandDeck.Theme { id: theme; loadShellTokens: false }

  CommandDeck.MatchedFieldList {
    id: fields
    width: 460
    theme: theme
  }

  // Marks each highlighted run with brackets, whatever the accent colour.
  function runs(markup) {
    return markup.replace(/<font color="#[0-9a-f]{6}"><b>/g, "[").replace(/<\/b><\/font>/g, "]")
  }

  function fieldMarkup(value, ranges) {
    fields.matches = [{ field: "path", value: value, ranges: ranges }]
    return runs(fields.renderedMarkupAt(0))
  }

  function test_ranges_highlight_each_covered_character_once_data() {
    return [
      { tag: "overlapping", ranges: [{ start: 0, length: 5 }, { start: 3, length: 2 }], expected: "[abcde]fgh" },
      { tag: "overlap extends", ranges: [{ start: 1, length: 3 }, { start: 2, length: 4 }], expected: "a[bcdef]gh" },
      { tag: "contained", ranges: [{ start: 1, length: 6 }, { start: 2, length: 1 }], expected: "a[bcdefg]h" },
      { tag: "adjacent", ranges: [{ start: 0, length: 2 }, { start: 2, length: 2 }], expected: "[abcd]efgh" },
      { tag: "separate and unsorted", ranges: [{ start: 6, length: 1 }, { start: 1, length: 1 }], expected: "a[b]cdef[g]h" },
      { tag: "past the end", ranges: [{ start: 6, length: 10 }], expected: "abcdef[gh]" },
      { tag: "empty and outside", ranges: [{ start: 3, length: 0 }, { start: 12, length: 2 }], expected: "abcdefgh" },
      { tag: "none", ranges: [], expected: "abcdefgh" }
    ]
  }

  function test_ranges_highlight_each_covered_character_once(data) {
    compare(fieldMarkup("abcdefgh", data.ranges), data.expected)
  }

  function test_markup_in_matched_text_stays_literal() {
    compare(fieldMarkup("x<y>&z", [{ start: 1, length: 3 }]), "x[&lt;y&gt;]&amp;z")
  }
}
