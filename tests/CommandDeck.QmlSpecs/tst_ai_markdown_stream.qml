import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// A streamed AI answer ends as the same document as rendering the whole
// answer at once, while each flush renders only new blocks and the tail.
Item {
  id: scene
  width: 700
  height: 900

  CommandDeck.Theme { id: theme; loadShellTokens: false }
  CommandDeck.AiMarkdownView { id: streamed; width: 600; theme: theme }
  CommandDeck.AiMarkdownView { id: oneShot; width: 600; theme: theme }
  CommandDeck.AiMarkdownView { id: hidden; width: 600; theme: theme; visible: false }

  TestCase {
    name: "AiMarkdownStream"
    when: windowShown

    readonly property var answers: [
      "**Plan** first `code` line\n<img src='file:///secret'> [link](https://example.test) & \"quotes\"\n\n"
        + "- item one\n- item **two**\n  - nested item\n1. numbered\n2. second `x`\n"
        + "```sh\necho  \"a <b>\" && ls\n   \n  indented line\n```\nafter  two  spaces\n   \n```\nopen at end\nmore",
      "```js\nlet a = 1\n```\n```\nb\n```\ntext\n",
      "intro\n```\n```\n```py\nx = 1\n\n```\n\n\nend",
      "text\n```\ncode line",
      "   \n\n  \nword",
      "a\nb\n\n",
      "1. first\n2. second\n\n```\n- not a list\n```\n- last item"
    ]
    readonly property var chunkings: [[1], [3], [7, 2, 11], [64], [100000]]

    function split(text, sizes) {
      const pieces = []
      let at = 0
      for (let index = 0; at < text.length; index++) {
        const size = sizes[index % sizes.length]
        pieces.push(text.slice(at, at + size))
        at += size
      }
      return pieces
    }

    function stream(view, pieces) {
      view.sourceText = ""
      let source = ""
      let most = 0
      for (const piece of pieces) {
        source += piece
        view.sourceText = source
        most = Math.max(most, view.lastRenderedLength)
      }
      return most
    }

    function body(html) {
      const start = html.indexOf("<body")
      return html.slice(html.indexOf(">", start) + 1)
    }

    function oneShotBody(text) {
      oneShot.sourceText = ""
      oneShot.text = oneShot.render(text)
      return body(oneShot.text)
    }

    function streamedLine(index) {
      return (index % 12 === 0 ? "\n\n**Step " + index + "** " : "")
        + "Some `code` and words about the answer number " + index + ". "
    }

    function test_streamed_answer_matches_one_shot_render() {
      for (let answer = 0; answer < answers.length; answer++) {
        const expected = oneShotBody(answers[answer])
        for (const sizes of chunkings) {
          stream(streamed, split(answers[answer], sizes))
          compare(body(streamed.text), expected,
            "answer " + answer + " streamed in pieces of " + sizes.join("/") + " renders like a one-shot render")
        }
      }
    }

    function test_flush_renders_new_blocks_and_tail_only() {
      const pieces = []
      for (let index = 0; index < 400; index++) pieces.push(streamedLine(index))
      const answer = pieces.join("")

      const started = Date.now()
      const most = stream(streamed, pieces)
      console.log("Streamed " + answer.length + " characters in " + pieces.length + " flushes: "
        + (Date.now() - started) + " ms, at most " + most + " characters per flush")

      verify(answer.length > 20000, "the streamed answer is long")
      verify(most > 0 && most < 1000,
        "a flush renders one paragraph and new text, not the whole answer (at most " + most + ")")
      compare(body(streamed.text), oneShotBody(answer), "the long streamed answer renders like a one-shot render")
    }

    function test_hidden_view_renders_when_shown() {
      const answer = answers[0]
      hidden.visible = false
      stream(hidden, split(answer, [5]))
      compare(hidden.length, 0, "a hidden view renders nothing while its answer streams")
      compare(hidden.lastRenderedLength, 0, "a hidden view spends no render work")

      hidden.visible = true
      compare(body(hidden.text), oneShotBody(answer), "showing the view renders the whole answer once")
      compare(hidden.lastRenderedLength, answer.length, "the first render when shown covers the answer")
      hidden.visible = false
    }

    function test_replaced_answer_renders_from_scratch() {
      streamed.sourceText = "first answer\n```\ncode\n```\nmore"
      streamed.sourceText = "second answer\n- item"
      compare(body(streamed.text), oneShotBody("second answer\n- item"), "a replaced answer shows only the new text")
      streamed.sourceText = ""
      compare(streamed.length, 0, "a cleared answer leaves the view empty")
    }
  }
}
