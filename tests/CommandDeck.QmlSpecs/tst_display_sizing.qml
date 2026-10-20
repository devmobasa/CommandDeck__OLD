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
