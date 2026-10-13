import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// Rendered-layout regressions measured with the production theme tokens:
// labels stay inside their controls and floating surfaces stay legible.
Item {
  id: root
  width: 900
  height: 600
  readonly property var teachingFilters: [
    { id: "projects", token: "proj:", label: "Projects" },
    { id: "herdr", token: "herdr:", label: "Herdr" }
  ]

  CommandDeck.Theme { id: theme; loadShellTokens: false }

  // Bright content beneath the floating surfaces makes any bleed-through visible.
  Rectangle { anchors.fill: parent; color: "white" }

  CommandDeck.NoResultsView {
    id: noResults
    width: root.width
    height: 300
    theme: theme
    query: "zzqxv"
    filterDescriptors: [{ id: "files", token: "file:", label: "Files" }]
  }

  CommandDeck.ActionPanel {
    id: actionPanel
    theme: theme
    x: 480
    y: 40
    width: 360
    height: 280
  }

  SignalSpy { id: dismissSpy; target: actionPanel; signalName: "dismissed" }

  CommandDeck.HelpSheet {
    id: helpSheet
    anchors.fill: parent
    theme: theme
  }

  CommandDeck.FilterPalette {
    id: filterPalette
    anchors.fill: parent
    theme: theme
    filterDescriptors: [{ id: "files", token: "file:", label: "Files" }]
  }

  CommandDeck.AliasDialog {
    id: aliasDialog
    anchors.fill: parent
    theme: theme
  }

  CommandDeck.ActionInputDialog {
    id: inputDialog
    anchors.fill: parent
    theme: theme
    action: ({ title: "Create branch", inputs: [{ name: "branch", defaultValue: "" }] })
  }

  CommandDeck.QueryHistorySurface {
    id: queryHistory
    anchors.fill: parent
    theme: theme
    snapshot: ({ enabled: true, retentionDays: 90, maximumEntries: 1000, entries: [] })
  }

  CommandDeck.EmptyStateView {
    id: emptyState
    width: root.width
    height: 400
    visible: false
    theme: theme
    filterDescriptors: root.teachingFilters
  }

  TestCase {
    name: "SurfaceLayout"
    when: windowShown

    function textItems(item, found) {
      for (let index = 0; index < item.children.length; index++) {
        const child = item.children[index]
        if (!child.visible) continue
        if (child.text !== undefined && child.contentWidth !== undefined) found.push(child)
        textItems(child, found)
      }
      return found
    }

    // Every visible label keeps a horizontal inset inside its control.
    function verifyLabelsInside(control, minimumInset) {
      const labels = textItems(control, [])
      verify(labels.length > 0, "the control renders a label")
      for (const label of labels) {
        const origin = label.mapToItem(control, 0, 0)
        verify(origin.x >= minimumInset,
          "\"" + label.text + "\" starts inside " + control.Accessible.name)
        verify(origin.x + label.contentWidth <= control.width - minimumInset,
          "\"" + label.text + "\" ends inside " + control.Accessible.name)
      }
    }

    function init() {
      actionPanel.result = null
      aliasDialog.opened = false
      queryHistory.opened = false
      inputDialog.opened = false
      helpSheet.opened = false
      filterPalette.opened = false
    }

    // A rendered pixel of the surface's padding matches the opaque panel colour
    // rather than a blend with the white content beneath it.
    function verifyOpaqueAt(x, y, surfaceName) {
      const pixel = grabImage(root).pixel(x, y)
      const expected = Qt.rgba(theme.background.r, theme.background.g, theme.background.b, 1)
      const tolerance = 1.5 / 255
      verify(Math.abs(pixel.r - expected.r) <= tolerance
          && Math.abs(pixel.g - expected.g) <= tolerance
          && Math.abs(pixel.b - expected.b) <= tolerance,
        surfaceName + " hides the content beneath it (rendered " + pixel + ", expected " + expected + ")")
    }

    function test_floating_surfaces_are_opaque() {
      verify(theme.background.a < 1, "the fallback palette is translucent")

      actionPanel.result = { id: "app:firefox", title: "Firefox", actions: [{ id: "launch", title: "Launch" }] }
      verify(waitForRendering(actionPanel))
      verifyOpaqueAt(actionPanel.x + 5, actionPanel.y + actionPanel.height / 2, "the action panel")
      actionPanel.result = null

      helpSheet.opened = true
      verify(waitForRendering(helpSheet))
      const helpWidth = Math.min(Math.round(760 * theme.scale), root.width - 48)
      verifyOpaqueAt((root.width - helpWidth) / 2 + 5, root.height / 2, "the help sheet")
      helpSheet.opened = false

      filterPalette.opened = true
      verify(waitForRendering(filterPalette))
      const paletteWidth = Math.min(Math.round(560 * theme.scale), root.width - 48)
      verifyOpaqueAt((root.width - paletteWidth) / 2 + 5, root.height / 2, "the filter palette")
    }

    function test_action_filter_matches_launcher_inputs() {
      actionPanel.openFor({ id: "app:firefox", title: "Firefox", actions: [{ id: "launch", title: "Launch" }] })
      const filter = actionPanel.filterControl
      tryVerify(function() { return filter.activeFocus })
      compare(filter.font.family, theme.fontFamily)
      verify(Qt.colorEqual(filter.background.color, theme.surfaceStrong), "the field uses the launcher surface")
      verify(Qt.colorEqual(filter.background.border.color, theme.accent), "the focused field shows the accent ring")

      keyClick(Qt.Key_B)
      compare(filter.text, "b")
      compare(actionPanel.visibleActionCount, 0)
      keyClick(Qt.Key_Backspace)
      compare(actionPanel.visibleActionCount, 1)
      dismissSpy.clear()
      keyClick(Qt.Key_Escape)
      compare(dismissSpy.count, 1)
    }

    function renderedTexts(item) {
      return textItems(item, []).map(label => String(label.text))
    }

    function occurrences(values, wanted) {
      return values.filter(value => value === wanted).length
    }

    function test_filter_palette_teaches_without_repeating_tokens() {
      filterPalette.filterDescriptors = root.teachingFilters
      filterPalette.opened = true
      tryVerify(function() { return renderedTexts(filterPalette).indexOf("Projects") >= 0 })
      const texts = renderedTexts(filterPalette)
      compare(occurrences(texts, "herdr:"), 1, "a filter without an example shows its token once")
      verify(texts.indexOf("proj: dotfiles") >= 0, "built-in filters show a runnable example")
      verify(texts.indexOf("= 100 km to miles") >= 0, "the calculator keeps its example")
      verify(texts.indexOf("Use") >= 0 && texts.indexOf("Close") >= 0,
        "the legend names what Enter and Esc do")
      filterPalette.filterDescriptors = [{ id: "files", token: "file:", label: "Files" }]
    }

    function test_empty_state_chips_do_not_repeat_tokens() {
      emptyState.visible = true
      tryVerify(function() { return renderedTexts(emptyState).indexOf("proj: dotfiles") >= 0 })
      const texts = renderedTexts(emptyState)
      verify(texts.indexOf("herdr:") < 0, "a chip without an example has no second line")
      verify(texts.indexOf("herdr:  Herdr") >= 0, "the chip still names its token")
      emptyState.visible = false
    }

    function verifyLauncherInput(field, label) {
      compare(field.font.family, theme.fontFamily, label + " uses the launcher font")
      verify(Qt.colorEqual(field.background.color, theme.surfaceStrong), label + " uses the launcher surface")
    }

    function test_dialog_inputs_match_launcher_inputs() {
      aliasDialog.openFor({ id: "app:firefox", title: "Firefox", aliases: [] })
      const alias = aliasDialog.inputControl
      tryVerify(function() { return alias.activeFocus })
      verifyLauncherInput(alias, "the alias field")
      keyClick(Qt.Key_F)
      compare(alias.text, "f")
      aliasDialog.opened = false

      inputDialog.opened = true
      const input = inputDialog.rowAt(0).input
      tryVerify(function() { return input.activeFocus })
      verifyLauncherInput(input, "the action input")
      keyClick(Qt.Key_M)
      compare(input.text, "m")
    }

    function test_full_palette_surfaces_keep_the_frame_and_hide_results() {
      queryHistory.opened = true
      verify(waitForRendering(queryHistory))
      const image = grabImage(root)
      verify(Qt.colorEqual(image.pixel(1, 1), "white"), "the corner falls outside the rounded frame")
      verifyOpaqueAt(4, root.height / 2, "query history")
    }

    function test_no_results_buttons_fit_their_labels() {
      tryVerify(function() { return noResults.assistantControl.width > 0 })
      verifyLabelsInside(noResults.assistantControl, 4)
      verifyLabelsInside(noResults.filtersControl, 4)
    }
  }
}
