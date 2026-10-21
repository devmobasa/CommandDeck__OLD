import QtQuick
import QtTest
import "../../qml/common/DisplaySizing.js" as Sizing

TestCase {
  name: "DisplaySizing"
  readonly property var screens: [
    [1920, 1080, 1], [2560, 1664, 1.6], [1920, 1200, 1.25],
    [2880, 1800, 2], [3840, 2160, 2], [2560, 1600, 1.5], [3200, 2000, 2],
    [2560, 1440, 1], [3840, 2160, 1.5], [5120, 2880, 2],
    [3840, 2160, 1], [3840, 2160, 1.666667], [2304, 1296, 1], [3440, 1440, 1], [5120, 1440, 1],
    [1280, 720, 1], [1080, 1920, 1], [720, 1280, 1], [600, 800, 1], [320, 240, 1]
  ]

  function test_screen_matrix() {
    for (const screen of screens) {
      for (const fontScale of [0.8, 1, 1.25, 1.5, 2]) {
        for (const settings of [false, true]) {
          let previous = null
          for (let percent = 75; percent <= 150; percent += 5) {
            const input = { width: screen[0] / screen[2], height: screen[1] / screen[2],
              percent: percent, fontScale: fontScale, bodyPixels: 12 * fontScale, settings: settings }
            const size = Sizing.calculate(input)
            verify(Number.isFinite(size.effective) && size.effective > 0)
            verify(size.panelWidth > 0 && size.panelWidth <= size.availableWidth)
            verify(size.panelHeight > 0 && size.panelHeight <= size.availableHeight)
            verify(12 * fontScale * size.textScale >= Math.min(12 * fontScale * size.requested, 11) - 0.001)
            if (previous) {
              verify(size.textScale >= previous.textScale - 0.001, "zoom must not shrink text")
              // A responsive layout can change its footprint; its metrics must
              // keep growing or plateau at the screen's fit limit.
              verify(size.effective >= previous.effective - 0.001, "zoom must not shrink controls")
            }
            previous = size
          }
        }
      }
    }
  }

  function test_recommended_typography() {
    for (const screen of screens.slice(0, 16)) {
      const size = Sizing.calculate({ width: screen[0] / screen[2], height: screen[1] / screen[2], percent: 100 })
      const body = Math.round(12 * size.textScale)
      verify(body >= 12 && body <= 15, "Recommended body text stays between 12 and 15 logical pixels")
    }
    compare(Sizing.calculate({ width: 1920, height: 1080, percent: 100 }).panelWidth, 900)
    compare(Sizing.calculate({ width: 5120, height: 1080, percent: 100 }).automatic, 1)
  }

  function test_maximum_fitting_percent_matrix() {
    for (const screen of screens) {
      let previous = 150
      for (const fontScale of [0.8, 1, 1.25, 1.5, 2]) {
        const input = { width: screen[0] / screen[2], height: screen[1] / screen[2],
          fontScale: fontScale, bodyPixels: 12 * fontScale, settings: true }
        const maximum = Sizing.maximumFittingPercent(input)
        verify(maximum === 0 || (maximum >= 75 && maximum <= 150 && maximum % 5 === 0))
        verify(maximum <= previous, "larger fonts cannot raise the fitting maximum")

        const launcher = Object.assign({}, input, { settings: false })
        for (let percent = 75; percent <= 150; percent += 5) {
          const size = Sizing.calculate(Object.assign({}, launcher, { percent: percent }))
          const fits = !size.limited && !size.scroll
            && size.contentWidth <= size.availableWidth && size.contentHeight <= size.availableHeight
          if (percent === maximum) verify(fits, "the announced maximum must fit without scrolling")
          if (percent > maximum) verify(!fits, "no step above the maximum can fit")
        }
        compare(input.settings, true, "the launcher calculation must not mutate the input")
        previous = maximum
      }
    }
  }

  function test_fitting_maximum_excludes_limited_and_scrolling_data() {
    return [
      { tag: "limited", width: 1280, height: 720, fontScale: 2, maximum: 0 },
      { tag: "scrolling", width: 600, height: 800, fontScale: 1, maximum: 80 }
    ]
  }

  function test_fitting_maximum_excludes_limited_and_scrolling(data) {
    const input = { width: data.width, height: data.height, fontScale: data.fontScale,
      bodyPixels: 12 * data.fontScale }
    compare(Sizing.maximumFittingPercent(input), data.maximum)
    const size = Sizing.calculate(Object.assign({}, input, { percent: 90 }))
    verify(size.limited || size.scroll)
    const description = Sizing.fitDescription(size, data.maximum)
    if (data.maximum === 0) {
      verify(description.indexOf("no percentage fits here") >= 0)
      verify(description.indexOf("max 75%") < 0)
    } else {
      verify(size.scroll && size.contentWidth > size.availableWidth)
      verify(description.indexOf("scroll to view") >= 0)
    }
  }

  function test_insets_use_height_before_scaling_down() {
    for (const screen of screens) {
      const height = screen[1] / screen[2]
      const standard = Math.min(height / 3, Math.max(54, Math.round(height * 0.12)))
      const floor = Math.max(24, Math.round(height * 0.04))
      for (const fontScale of [0.8, 1, 1.25, 1.5, 2]) {
        for (const settings of [false, true]) {
          for (let percent = 75; percent <= 150; percent += 5) {
            const size = Sizing.calculate({ width: screen[0] / screen[2], height: height,
              fontScale: fontScale, bodyPixels: 12 * fontScale, percent: percent, settings: settings })
            const requestedHeight = (size.narrow ? 420 : settings ? 700 : 600) * fontScale * size.requested
            const standardHeight = height - standard - size.bottomInset
            if (requestedHeight <= standardHeight) {
              compare(size.topInset, standard, "fitting content keeps its standard position")
            } else {
              verify(size.topInset >= floor && size.topInset < standard)
            }
            verify(size.panelHeight + size.topInset + size.bottomInset <= height + 0.001)
          }
        }
      }
    }
  }

  function test_legacy_baseline_parity_data() {
    return [
      { tag: "auto", percent: 100 }, { tag: "small", percent: 90 },
      { tag: "default", percent: 100 }, { tag: "large", percent: 115 },
      { tag: "xlarge", percent: 130 }
    ]
  }

  function test_legacy_baseline_parity(data) {
    // Independent a1b1ec8 CommandDeck/Theme/CommandDeckWindow formulas.
    for (const screen of screens) {
      for (const fontScale of [0.8, 1, 14 / 12, 1.25, 1.5, 2]) {
        for (const settings of [false, true]) {
          const width = screen[0] / screen[2]
          const height = screen[1] / screen[2]
          const modeHeight = screen[0] === 2304 ? 2160 : screen[1]
          const automatic = Math.max(1, Math.min(2, 1 + (modeHeight - 1080) / 1080 * 0.5))
          const displayScale = automatic * (data.percent / 100)
          const density = settings ? 0.75 : 1
          const scale = fontScale * displayScale * density
          const top = Math.max(54, Math.round(height * 0.12))
          const bottom = Math.max(40, Math.round(height * 0.05))
          const paletteWidth = Math.round(900 * scale)
          const paletteHeight = Math.round(600 * scale)
          const size = Sizing.calculate({ width: width, height: height, legacyHeight: modeHeight,
            fontScale: fontScale, bodyPixels: 12 * fontScale, legacy: true,
            legacyPercent: data.percent, percent: data.percent, settings: settings })

          for (const base of [10, 11, 12, 13, 14, 16]) {
            const pixels = Math.round(base * fontScale)
            compare(Math.round(pixels * size.textScale),
              Math.round(pixels * ((1 + (displayScale - 1) * 0.5) * density)))
          }
          const actualScale = fontScale * (size.displayScale === undefined ? size.effective : size.displayScale)
            * (size.densityScale === undefined ? 1 : size.densityScale)
          compare(Math.round(46 * actualScale), Math.round(46 * scale))
          compare(size.panelWidth, Math.min(settings ? Math.round(paletteWidth * 1.3) : paletteWidth, width - 80))
          compare(size.panelHeight, Math.min(settings ? Math.round(paletteHeight * 1.35) : paletteHeight, height - top - bottom))
          compare(size.topInset, top)
          compare(size.bottomInset, bottom)
          compare(size.contentWidth, size.panelWidth, "legacy content keeps the clamped panel layout")
          compare(size.contentHeight, size.panelHeight)
          verify(!size.narrow && !size.scroll)
        }
      }
    }
  }

  function test_fitting_narrow_layout_keeps_standard_geometry() {
    const size = Sizing.calculate({ width: 1280, height: 720, fontScale: 0.8,
      bodyPixels: 9.6, percent: 150, settings: true })
    verify(size.narrow && !size.limited && !size.scroll)
    compare(size.fit, 1)
    compare(size.textScale, 1.5)
    compare(size.topInset, 86)
    compare(size.bottomInset, 40)
    compare(size.availableHeight, 594)
    compare(size.panelWidth, 768)
    compare(size.panelHeight, 504)
  }

  function test_invalid_and_unconfigured_geometry() {
    for (const width of [0, -1, NaN, Infinity, undefined]) {
      const size = Sizing.calculate({ width: width, height: 0, fontScale: NaN, percent: NaN })
      compare(size.panelWidth, 900)
      compare(size.panelHeight, 600)
    }
  }

  function test_legacy_profile_preserves_reference_and_applies_full_zoom() {
    const initial = Sizing.calculate({ width: 2304, height: 1296, legacyHeight: 2160,
      legacy: true, legacyPercent: 100, percent: 100 })
    compare(initial.effective, 1.5)
    compare(initial.textScale, 1.25)
    const zoom = Sizing.calculate({ width: 2304, height: 1296, legacyHeight: 2160,
      legacy: true, legacyPercent: 100, percent: 150 })
    compare(zoom.effective / 1.5, 1.5)
    compare(zoom.textScale / 1.25, 1.5)
  }
}
