.pragma library

function positive(value, fallback) {
  return Number.isFinite(value) && value > 0 ? value : fallback
}

function fitDescription(sizing, maximum, location) {
  return (sizing.limited ? "; limited to fit (max " + maximum + "% " + (location || "here") + ")" : "")
    + (sizing.narrow ? "; narrow layout" : "")
    + (sizing.scroll ? "; scroll to view" : "")
}

// Settings has a different footprint; the percentage always controls the launcher.
function maximumFittingPercent(input) {
  for (let percent = 150; percent >= 75; percent -= 5) {
    if (!calculate(Object.assign({}, input, { percent: percent, settings: false })).limited) return percent
  }
  return 75
}

// All geometry is in the presenting window's logical pixels. Mode pixels are
// accepted only by the versioned compatibility profile, never by Recommended.
function calculate(input) {
  const width = positive(input.width, 1920)
  const height = positive(input.height, 1080)
  const fontScale = positive(input.fontScale, 1)
  const user = Math.max(75, Math.min(150, positive(input.percent, 100))) / 100
  const body = positive(input.bodyPixels, 12)
  let top = Math.min(height / 3, Math.max(54, Math.round(height * 0.12)))
  const bottom = Math.min(height / 3, Math.max(40, Math.round(height * 0.05)))
  const availableWidth = Math.max(1, width - Math.min(80, width / 4))
  const legacy = input.legacy === true
  const automatic = legacy
    ? Math.max(1, Math.min(2, 1 + (positive(input.legacyHeight, height) - 1080) / 2160))
    : Math.max(1, Math.min(1.25, Math.sqrt(Math.min(width / 1920, height / 1080))))
  const density = legacy && input.settings ? 0.75 : 1
  const referenceUser = positive(input.legacyPercent, input.percent || 100) / 100
  const requested = automatic * user * density
  // A legacy reference preserves the original appearance; further zoom is full
  // strength for fonts as well as layout, including after a restart.
  const requestedText = legacy
    ? (1 + (automatic * referenceUser - 1) * 0.5) * user / referenceUser * density
    : requested
  let baseWidth = input.settings ? (legacy ? 1170 : 1050) : 900
  let baseHeight = input.settings ? (legacy ? 810 : 700) : 600
  const minimumText = Math.min(body * requestedText, 11)
  const standardHeight = Math.max(1, height - top - bottom)
  const standardFit = Math.min(1, availableWidth / (baseWidth * fontScale * requested),
    standardHeight / (baseHeight * fontScale * requested))
  const narrow = body * requestedText * standardFit < minimumText - 0.001
  if (narrow) {
    baseWidth = 640
    baseHeight = 420
  }

  // Keep the responsive layout, then spend space above it before shrinking
  // its text and controls. Layouts that already fit keep their standard inset.
  const topFloor = Math.min(top, Math.max(24, Math.round(height * 0.04)))
  top = Math.min(top, Math.max(topFloor, height - bottom - baseHeight * fontScale * requested))
  const availableHeight = Math.max(1, height - top - bottom)

  let fit = Math.min(1, availableWidth / (baseWidth * fontScale * requested),
    availableHeight / (baseHeight * fontScale * requested))

  const scroll = body * requestedText * fit < minimumText - 0.001
  if (scroll) fit = minimumText / (body * requestedText)
  const effective = requested * fit
  const contentWidth = Math.max(1, Math.floor(baseWidth * fontScale * effective))
  const contentHeight = Math.max(1, Math.floor(baseHeight * fontScale * effective))
  return {
    automatic: automatic, requested: requested, effective: effective,
    textScale: requestedText * fit, fit: fit, limited: fit < 0.9999,
    narrow: narrow, scroll: scroll, topInset: top, bottomInset: bottom,
    availableWidth: availableWidth, availableHeight: availableHeight,
    contentWidth: contentWidth, contentHeight: contentHeight,
    panelWidth: Math.min(contentWidth, Math.floor(availableWidth)),
    panelHeight: Math.min(contentHeight, Math.floor(availableHeight))
  }
}
