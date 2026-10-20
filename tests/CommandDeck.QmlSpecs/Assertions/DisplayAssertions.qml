import QtQuick
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(context) {
    const regularTokens = host.part_theme
    host.part_motionTheme.tokens = regularTokens
    host.expect(host.part_motionTheme.panelPadding === Math.round(regularTokens.panelPadding * 1.5 * 0.75)
        && host.part_motionTheme.rowPaddingX === Math.round(regularTokens.rowPaddingX * 1.5 * 0.75)
        && host.part_motionTheme.gap === Math.round(regularTokens.gap * 1.5 * 0.75),
      "compact density also scales padding and spacing from shell tokens")
    host.part_motionTheme.tokens = null
    host.part_motionTheme.densityScale = 1
    host.part_motionTheme.displayScale = 1

    // C8 - the scrim strength is a tunable, opaque-by-default floor.
    host.expect(host.part_motionTheme.scrimStrength >= 0.6,
      "the scrim defaults to a strongly opaque strength")
    host.expect(host.part_motionTheme.scrimColor.a >= host.part_motionTheme.scrimStrength - 0.001,
      "the scrim color never renders more transparent than the strength floor")
    host.part_motionTheme.scrimStrength = 0.9
    host.expect(Math.abs(host.part_motionTheme.scrimColor.a - 0.9) < 0.001,
      "the scrim strength is tunable and applied to the scrim color")
    host.part_motionTheme.scrimStrength = 0.72

    // D13 - the display-size preset composes with the screen-derived factor.
    host.expect(host.part_integratedDeck.displaySizePreset === "auto"
        && Math.abs(host.part_integratedDeck.displaySizeMultiplier - 1.0) < 0.001,
      "the display size defaults to Auto with a 1x multiplier")
    host.expect(Math.abs(host.part_integratedDeck.effectiveDisplayScale
        - host.part_integratedDeck.displayScaleFactor) < 0.001,
      "Auto feeds the screen-derived display-scale factor straight through")
    host.part_integratedDeck.applyDisplaySizePreset("large")
    host.expect(host.part_integratedDeck.displaySizePreset === "large"
        && Math.abs(host.part_integratedDeck.displaySizeMultiplier - 1.15) < 0.001,
      "selecting Large applies a 1.15 multiplier")
    host.expect(Math.abs(host.part_integratedDeck.effectiveDisplayScale
        - host.part_integratedDeck.displayScaleFactor * 1.15) < 0.001,
      "the display-size multiplier composes on top of the screen factor")
    host.part_integratedDeck.applyDisplaySizePreset("small")
    host.expect(Math.abs(host.part_integratedDeck.displaySizeMultiplier - 0.9) < 0.001,
      "selecting Small applies a 0.9 multiplier")
    host.part_integratedDeck.applyDisplaySizePreset("auto")

    // Zoom keys step through the same presets, hold at each end, and reset to Auto.
    const zoomed = []
    for (const direction of [1, 1, 1, 0, -1, -1, -1]) {
      host.part_integratedDeck.stepDisplaySize(direction)
      zoomed.push(host.part_integratedDeck.displaySizePreset)
    }
    host.expect(zoomed.join(",") === "large,xlarge,xlarge,auto,small,small,small"
        && Math.abs(host.part_integratedDeck.displaySizeMultiplier - 0.9) < 0.001
        && host.part_integratedDeck.statusText === "Display size: Small",
      "zoom steps through the display-size presets and names the result (" + zoomed.join(",") + ")")
    host.part_integratedDeck.stepDisplaySize(0)

    // D13 - the settings control offers the presets and emits the choice.
    host.expect(host.part_settings.displaySizeControl.count === 5,
      "settings offers five display-size presets")
    host.part_settings.displaySizePreset = "large"
    host.expect(host.part_settings.displaySizeControl.currentIndex === host.part_settings.displaySizeIndex("large"),
      "the settings display-size control reflects the active preset")
    host.part_settings.displaySizeChosen = ""
    host.part_settings.displaySizeControl.activated(1)
    host.expect(host.part_settings.displaySizeChosen === "small",
      "choosing a display-size preset emits it for QML-only persistence")
    host.part_settings.displaySizePreset = "auto"

    // The compact layout: a QML-only store, the host flag, and its Settings toggle.
    const layout = host.part_integratedDeck.part_layoutStore
    host.expect(!layout.parse("").compact && layout.parse("compact=on\n").compact
        && !layout.parse("compact=maybe").compact && layout.parse(" Compact = ON ").compact,
      "the layout file reads as the default layout unless compact is on")
    host.part_integratedDeck.applyCompactLayout(true)
    host.expect(host.part_integratedDeck.compactLayout && layout.serialize() === "compact=on\npreview=on\n",
      "choosing the compact layout applies it to the launcher at once")
    host.part_integratedDeck.applyCompactLayout(false)
    host.expect(!host.part_integratedDeck.compactLayout, "turning it off restores the default layout")
    host.part_settings.compactLayout = false
    host.part_settings.compactLayoutControl.click()
    host.expect(host.part_settings.compactLayoutChosen === true,
      "the Settings toggle emits the compact layout choice")
    host.part_settings.compactLayout = false
    host.expect(layout.parse("").preview && !layout.parse("preview=off").preview
        && layout.parse("preview=maybe").preview,
      "the layout file shows the preview pane unless it is off")
    const behavior = host.part_integratedDeck.part_behaviorStore
    const parsed = behavior.parse("escape=clear\nkeep=120\n")
    host.expect(parsed.escapeClears && parsed.keepSeconds === 120
        && !behavior.parse("").escapeClears && behavior.parse("").keepSeconds === 0
        && behavior.parse("keep=45").keepSeconds === 0 && !behavior.parse("escape=maybe").escapeClears,
      "the behaviour file reads only the offered choices, else closes at once and keeps nothing")
    host.part_settings.escapeClearsSearch = false
    host.part_settings.keepSearchSeconds = 30
    host.part_settings.behaviorChosen = null
    host.part_settings.escapeClearsControl.click()
    host.expect(host.part_settings.behaviorChosen !== null && host.part_settings.behaviorChosen.escapeClears === true
        && host.part_settings.behaviorChosen.keepSeconds === 30,
      "the Escape toggle emits its choice with the kept-search time")
    host.part_settings.keepSearchControl.activated(3)
    host.expect(host.part_settings.behaviorChosen.keepSeconds === 600,
      "choosing how long to keep the last search emits it")
    host.part_settings.keepSearchSeconds = 0

    // History and Omarchy state choices show the configuration's value until
    // made, then the choice; a click saves it with the other preferences.
    const settingsSnapshot = host.part_settings.snapshot
    const withChoices = Object.assign({}, settingsSnapshot, {
      preferences: Object.assign({}, settingsSnapshot.preferences, { learnFromUse: false }),
      defaults: { rememberSearches: true, learnFromUse: true, omarchyMenuState: false }
    })
    host.part_settings.snapshot = withChoices
    host.expect(host.part_settings.rememberSearchesControl.checked
        && !host.part_settings.learnFromUseControl.checked
        && !host.part_settings.omarchyMenuStateControl.checked,
      "history toggles show the configuration until a choice is made, then the choice")
    host.part_settings.changedPreferences = null
    host.part_settings.rememberSearchesControl.click()
    host.expect(host.part_settings.changedPreferences !== null
        && host.part_settings.changedPreferences.rememberSearches === false
        && host.part_settings.changedPreferences.learnFromUse === false,
      "turning Remember searches off saves the choice with the other preferences")
    host.part_settings.changedPreferences = null
    host.part_settings.omarchyMenuStateControl.click()
    host.expect(host.part_settings.changedPreferences !== null
        && host.part_settings.changedPreferences.omarchyMenuState === true,
      "turning Omarchy menu state on saves the choice")
    host.part_settings.snapshot = settingsSnapshot
    host.part_settings.previewPaneVisible = true
    host.part_settings.previewPaneControl.click()
    host.expect(host.part_settings.previewPaneChosen === false,
      "the Settings toggle emits hiding the preview pane")
    host.part_settings.previewPaneVisible = true

    // Panel transparency: theme, live launcher color, and settings controls.
    host.expect(!host.part_motionTheme.panelTransparencyEnabled
        && Math.abs(host.part_motionTheme.panelBackground.a - 1) < 0.001
        && Math.abs(host.part_motionTheme.overlayScrimColor.a - host.part_motionTheme.scrimColor.a) < 0.001,
      "panel transparency off forces an opaque panel and keeps the C8 scrim")
    host.part_motionTheme.panelTransparencyEnabled = true
    host.part_motionTheme.panelTransparency = 0.5
    host.expect(Math.abs(host.part_motionTheme.panelBackground.a - 0.5) < 0.001
        && host.part_motionTheme.panelBackground.r === host.part_motionTheme.background.r,
      "enabling transparency applies the amount to the panel alpha")
    host.expect(Math.abs(host.part_motionTheme.overlayScrimColor.a
        - host.part_motionTheme.scrimColor.a * 0.5) < 0.001,
      "enabling transparency scales the overlay scrim so wallpaper can show through")
    host.part_motionTheme.panelTransparency = 0.95
    host.expect(Math.abs(host.part_motionTheme.panelBackground.a - 0.2) < 0.001,
      "panel transparency clamps so the launcher cannot disappear")
    host.part_motionTheme.panelTransparencyEnabled = false
    host.part_motionTheme.panelTransparency = 0

    host.expect(!host.part_integratedDeck.panelTransparencyEnabled
        && host.part_integratedDeck.panelTransparencyPercent === 20,
      "the launcher defaults to a solid panel at 20 percent standby amount")
    host.part_integratedDeck.applyPanelTransparency(true, 40)
    host.expect(host.part_integratedDeck.panelTransparencyEnabled
        && host.part_integratedDeck.panelTransparencyPercent === 40
        && Math.abs(host.part_integratedDeck.appliedPanelBackground.a - 0.6) < 0.001,
      "applying transparency updates the live panel color")
    host.part_integratedDeck.applyPanelTransparency(false, 20)
    host.expect(!host.part_integratedDeck.panelTransparencyEnabled
        && Math.abs(host.part_integratedDeck.appliedPanelBackground.a - 1) < 0.001,
      "turning transparency off restores an opaque panel")

    host.expect(host.part_settings.panelTransparencyControl.text === "Transparent background",
      "settings offers a transparency enable toggle")
    host.expect(!host.part_settings.panelTransparencyAmountControl.enabled,
      "the transparency slider stays disabled while the toggle is off")
    host.part_settings.panelTransparencyChosenEnabled = false
    host.part_settings.panelTransparencyChosenPercent = -1
    host.part_settings.panelTransparencyControl.click()
    host.expect(host.part_settings.panelTransparencyChosenEnabled === true
        && host.part_settings.panelTransparencyChosenPercent === 20,
      "enabling transparency emits the toggle for QML-only persistence")
    host.part_settings.panelTransparencyEnabled = true
    host.expect(host.part_settings.panelTransparencyAmountControl.enabled,
      "the transparency slider enables once the toggle is on")
    host.part_settings.panelTransparencyChosenPercent = -1
    host.part_settings.panelTransparencyAmountControl.value = 45
    host.part_settings.panelTransparencyAmountControl.moved()
    host.expect(host.part_settings.panelTransparencyChosenPercent === 45,
      "moving the transparency slider emits the amount for QML-only persistence")
    host.part_settings.panelTransparencyChosenPercent = -1
    host.part_settings.panelTransparencyAmountControl.value = 21
    host.part_settings.panelTransparencyAmountControl.sliderControl.moved()
    host.expect(host.part_settings.panelTransparencyChosenPercent === 21,
      "the inner slider moved signal forwards through the wrapper")
    host.part_settings.panelTransparencyEnabled = false
    host.part_settings.panelTransparencyPercent = 20

    // The QML-only colour preference and the themes that follow it.
    const colorStore = host.part_colorThemeStoreSpec
    host.expect(colorStore.filePath === "/home/example/.local/state/commanddeck/color-theme",
      "the colour store targets a file under $XDG_STATE_HOME/commanddeck")
    colorStore.load()
    host.expect(colorStore.source === "omarchy" && !colorStore.overridesHost
        && colorStore.colors.accent === "#8ab4f8",
      "a missing colour file follows the Omarchy theme with the default palette")
    colorStore.set({ source: "commanddeck", colors: { accent: "#F5C26B", text: "not a color" } })
    host.expect(colorStore.overridesHost && colorStore.colors.accent === "#f5c26b"
        && colorStore.colors.text === "#f1f1f1",
      "the colour store records the override and valid colours only")
    colorStore.resetColors()
    host.expect(colorStore.overridesHost && colorStore.colors.accent === "#8ab4f8",
      "resetting restores the palette but keeps the chosen theme")

    const deckColors = host.part_integratedDeck.part_colorThemeStore
    deckColors.set({ source: "commanddeck", colors: { accent: "#a3d977", background: "#1c1d22" } })
    host.expect(host.part_integratedDeck.part_appTheme.overrideHostColors
        && Qt.colorEqual(host.part_integratedDeck.part_appTheme.accent, "#a3d977")
        && Qt.colorEqual(host.part_integratedDeck.part_appTheme.popupBackground, "#1c1d22")
        && Qt.colorEqual(host.part_integratedDeck.part_settingsTheme.accent, "#a3d977"),
      "the launcher and Settings themes apply the stored CommandDeck palette")
    deckColors.set({ source: "omarchy", colors: { accent: "#8ab4f8", background: "#0e0e11" } })
    host.expect(!host.part_integratedDeck.part_appTheme.overrideHostColors,
      "choosing the Omarchy theme again stops overriding its colours")

    host.expect(host.part_pane.renderedDetailCount === 0, "detail pane renders an empty field list")
    host.expect(!host.part_pane.visible, "detail pane hides without fields, body, or preview")

    host.part_pane.result = ({
      title: "One field",
      subtitle: "Structured metadata",
      details: [{ label: "Path", value: "/tmp/test.txt", monospace: true }]
    })

    Object.assign(context, { regularTokens })
  }
}
