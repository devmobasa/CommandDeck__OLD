import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

Item {
  id: root
  width: 700
  height: 600
  property int requestedIndex: -1
  property int openCount: 0

  CommandDeck.Theme { id: theme; loadShellTokens: false }
  ListModel { id: rows }
  CommandDeck.SearchNavigationController { id: navigation; rows: rows }
  CommandDeck.CommandDeckSearchField {
    id: search
    width: parent.width
    theme: theme
    controller: navigation
    surfaceVisible: true
    additionalTabStop: preview.firstControl
    precedingTabStop: preview.lastControl
  }
  CommandDeck.FilePreview {
    id: preview
    y: 80
    width: parent.width
    theme: theme
    typingTarget: search.inputControl
    previousTabStop: search.settingsControl
    nextTabStop: search.inputControl
    onMatchRequested: index => root.requestedIndex = index
    onOpenRequested: root.openCount++
  }

  TestCase {
    name: "FilePreviewEventPaths"
    when: windowShown

    function init() {
      root.requestedIndex = -1
      root.openCount = 0
      preview.loading = false
      preview.preview = { text: "before\nneedle\nafter", startLine: 299, matchLine: 300,
        matchIndex: 0, matchCount: 2, openResultId: "preview:cached" }
      navigation.clear()
      navigation.focusRevision++
      tryCompare(search.inputControl, "activeFocus", true)
      verify(waitForRendering(root))
    }

    function test_mouse_navigation_keeps_search_ready() {
      const next = findChild(preview, "nextPreviewMatch")
      mouseClick(next)
      compare(root.requestedIndex, 1)
      verify(search.inputControl.activeFocus)
      keyClick(Qt.Key_N)
      compare(navigation.queryText, "n")
      mouseClick(findChild(preview, "openPreviewMatch"))
      compare(root.openCount, 1)
      keyClick(Qt.Key_E)
      compare(navigation.queryText, "ne")
      verify(search.inputControl.activeFocus)
    }

    function test_keyboard_navigation_survives_async_replacement() {
      keyClick(Qt.Key_Tab)
      verify(search.settingsControl.activeFocus)
      keyClick(Qt.Key_Tab)
      verify(preview.firstControl.activeFocus)
      keyClick(Qt.Key_Right)
      const next = findChild(preview, "nextPreviewMatch")
      verify(next.activeFocus)
      keyClick(Qt.Key_Return)
      compare(root.requestedIndex, 1)
      preview.loading = true
      verify(next.activeFocus)
      preview.preview = { text: "other needle", startLine: 450, matchLine: 450,
        matchIndex: 1, matchCount: 2, openResultId: "preview:replacement" }
      preview.loading = false
      verify(next.activeFocus)
      keyClick(Qt.Key_Tab)
      verify(preview.lastControl.activeFocus)
      keyClick(Qt.Key_Space)
      compare(root.openCount, 1)
      keyClick(Qt.Key_Tab)
      verify(search.inputControl.activeFocus)
      keyClick(Qt.Key_Backtab)
      verify(preview.lastControl.activeFocus)
    }

    function test_image_preview_loads_and_keeps_typing_ready() {
      const source = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAASElEQVR4nGNwi7r0fyAxw6gDRkNgNARGQ4BQCFAKKC6IiDGEEr2jDhgNgdEQGA2B0RAYGiEwoJXRaLN8NARGQ2A0BNxo3D0HAMBAGv92W3CjAAAAAElFTkSuQmCC"
      preview.preview = { imageSource: source, imageWidth: 32, imageHeight: 32, language: "png", text: "" }
      const image = findChild(preview, "filePreviewImage")
      tryCompare(image, "status", Image.Ready)
      verify(waitForRendering(image))
      verify(image.paintedWidth > 0 && image.paintedHeight > 0)
      compare(image.fillMode, Image.PreserveAspectFit)
      compare(preview.firstControl, null)
      mouseClick(image)
      keyClick(Qt.Key_P)
      compare(navigation.queryText, "p")
      verify(search.inputControl.activeFocus)
      preview.loading = true
      tryCompare(image, "source", "")
      preview.preview = { text: "next text file", language: "text" }
      preview.loading = false
      verify(!preview.isImage)
      keyClick(Qt.Key_N)
      compare(navigation.queryText, "pn")
      preview.preview = { imageSource: source, imageWidth: 32, imageHeight: 32, language: "png", text: "" }
      tryCompare(image, "status", Image.Ready)
      verify(search.inputControl.activeFocus)
    }

    function test_image_preview_rejects_path_sources_and_reports_decode_errors() {
      preview.preview = { imageSource: "file:///private.png", imageWidth: 32, imageHeight: 32 }
      const image = findChild(preview, "filePreviewImage")
      compare(image.source, "")
      verify(findChild(preview, "imagePreviewError").visible)
      ignoreWarning(/.*Error decoding.*Unsupported image format/)
      preview.preview = { imageSource: "data:image/png;base64,bad", imageWidth: 32, imageHeight: 32 }
      tryCompare(image, "status", Image.Error)
      verify(findChild(preview, "imagePreviewError").visible)
      keyClick(Qt.Key_E)
      compare(navigation.queryText, "e")
      verify(search.inputControl.activeFocus)
    }
  }
}
