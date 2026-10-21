import QtQuick
import QtTest
import "../../qml/common/DisplaySizing.js" as Sizing

TestCase {
  name: "DisplaySizing"
  readonly property var screens: [
    [1920, 1080, 1], [2560, 1664, 1.6], [1920, 1200, 1.25],
    [2880, 1800, 2], [3840, 2160, 2], [2560, 1600, 1.5], [3200, 2000, 2],
    [2560, 1440, 1], [3840, 2160, 1.5], [5120, 2880, 2],
    [3840, 2160, 1], [3840, 2160, 1.666667], [3440, 1440, 1], [5120, 1440, 1],
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
        verify(maximum >= 75 && maximum <= 150 && maximum % 5 === 0)
        verify(maximum <= previous, "larger fonts cannot raise the fitting maximum")

        const launcher = Object.assign({}, input, { percent: maximum, settings: false })
        verify(!Sizing.calculate(launcher).limited || maximum === 75,
          "the maximum must fit unless even the minimum is limited")
        if (maximum < 150) {
          verify(Sizing.calculate(Object.assign({}, launcher, { percent: maximum + 5 })).limited,
            "the next grid step must exceed the fitting maximum")
        }
        compare(input.settings, true, "the launcher calculation must not mutate the input")
        previous = maximum
      }
    }
  }

  function test_maximum_falls_back_to_minimum_when_nothing_fits() {
    const input = { width: 320, height: 240, fontScale: 2, bodyPixels: 24, percent: 75 }
    verify(Sizing.calculate(input).limited)
    compare(Sizing.maximumFittingPercent(input), 75)
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

  function test_owner_legacy_workspace() {
    const input = { width: 2304, height: 1296, fontScale: 14 / 12, bodyPixels: 14,
      legacy: true, legacyPercent: 120, legacyHeight: 2160 }
    compare(Sizing.maximumFittingPercent(input), 110)
    const normal = Sizing.calculate(Object.assign({}, input, { percent: 100 }))
    compare(normal.topInset, 156)
    compare(normal.bottomInset, 65)
    compare(normal.availableHeight, 1075)
    compare(normal.panelWidth, 1575)
    compare(normal.panelHeight, 1050)
    compare(normal.fit, 1)
    compare(normal.textScale, 7 / 6)

    const intermediate = Sizing.calculate(Object.assign({}, input, { percent: 105 }))
    verify(!intermediate.limited)
    fuzzyCompare(intermediate.topInset, 128.5, 0.001)
    fuzzyCompare(intermediate.availableHeight, 1102.5, 0.001)
    const fitting = Sizing.calculate(Object.assign({}, input, { percent: 110 }))
    verify(!fitting.limited)
    fuzzyCompare(fitting.topInset, 76, 0.001)
    fuzzyCompare(fitting.availableHeight, 1155, 0.001)
    const limited = Sizing.calculate(Object.assign({}, input, { percent: 115 }))
    verify(limited.limited)
    compare(limited.topInset, 52)
    compare(limited.bottomInset, 65)
    compare(limited.availableHeight, 1179)
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
    const zoom = Sizing.calculate({ width: 3840, height: 2160, legacyHeight: 2160,
      legacy: true, legacyPercent: 100, percent: 150 })
    compare(zoom.effective / 1.5, 1.5)
    compare(zoom.textScale / 1.25, 1.5)
  }
}
