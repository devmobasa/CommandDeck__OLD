.pragma library
.import "UiConstants.js" as Ui

// Pure rules for the QML-only colour preference: which theme colours the
// launcher and the CommandDeck palette's accent, background, and text. A
// preference is { source, colors: { accent, background, text } } with colours
// as lower-case #rrggbb; anything else degrades to the defaults.

var hexColor = /^#[0-9a-f]{6}$/
var roles = [Ui.DeckColorRole.Accent, Ui.DeckColorRole.Background, Ui.DeckColorRole.Text]
// WCAG AA for body text; below it the launcher warns that text is hard to read.
var minimumReadableContrast = 4.5
// WCAG non-text contrast; below it the accent cannot mark keyboard focus.
var minimumFocusContrast = 3

function defaultColors() {
  const colors = {}
  colors[Ui.DeckColorRole.Accent] = Ui.DeckColors.Accent
  colors[Ui.DeckColorRole.Background] = Ui.DeckColors.Background
  colors[Ui.DeckColorRole.Text] = Ui.DeckColors.Text
  return colors
}

function defaults() {
  return { source: Ui.ColorThemeSource.Omarchy, colors: defaultColors() }
}

// "#8AB4F8", "8ab4f8", and " #8ab4f8 " all read as "#8ab4f8"; anything else is
// not a colour and returns the fallback.
function normalizeColor(value, fallback) {
  const text = String(value === null || value === undefined ? "" : value).trim().toLowerCase()
  const candidate = text.charAt(0) === "#" ? text : "#" + text
  return hexColor.test(candidate) ? candidate : fallback
}

function normalizeSource(value) {
  return value === Ui.ColorThemeSource.CommandDeck ? Ui.ColorThemeSource.CommandDeck : Ui.ColorThemeSource.Omarchy
}

// Applies the recognised parts of a partial change; invalid colours keep the
// current value.
function merge(current, change) {
  const base = current || defaults()
  const values = change || {}
  const colors = {}
  const changedColors = values.colors || {}
  for (const role of roles) {
    colors[role] = normalizeColor(changedColors[role], normalizeColor(base.colors[role], defaultColors()[role]))
  }

  return {
    source: normalizeSource(values.source !== undefined ? values.source : base.source),
    colors: colors
  }
}

function sameColors(left, right) {
  return roles.every(role => left[role] === right[role])
}

function equal(left, right) {
  return left.source === right.source && sameColors(left.colors, right.colors)
}

function parse(text) {
  const raw = String(text === null || text === undefined ? "" : text).trim()
  if (raw.length === 0) return defaults()

  try {
    return merge(defaults(), JSON.parse(raw))
  } catch (error) {
    return defaults()
  }
}

function serialize(preference) {
  return JSON.stringify({ source: preference.source, colors: preference.colors }) + "\n"
}

// Channels from 0 to 1, as QML colours carry them.
function channelLuminance(red, green, blue) {
  const linear = [red, green, blue].map(channel => channel <= 0.03928
    ? channel / 12.92
    : Math.pow((channel + 0.055) / 1.055, 2.4))
  return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2]
}

function relativeLuminance(hex) {
  const value = normalizeColor(hex, Ui.DeckColors.Background)
  const channels = [1, 3, 5].map(offset => parseInt(value.substr(offset, 2), 16) / 255)
  return channelLuminance(channels[0], channels[1], channels[2])
}

function luminanceRatio(first, second) {
  return (Math.max(first, second) + 0.05) / (Math.min(first, second) + 0.05)
}

function contrastRatio(foreground, background) {
  return luminanceRatio(relativeLuminance(foreground), relativeLuminance(background))
}

// The same ratio for resolved theme colours; alpha is ignored.
function colorContrast(first, second) {
  return luminanceRatio(channelLuminance(first.r, first.g, first.b),
    channelLuminance(second.r, second.g, second.b))
}

function readable(colors) {
  return contrastRatio(colors[Ui.DeckColorRole.Text], colors[Ui.DeckColorRole.Background]) >= minimumReadableContrast
}

function accentVisible(colors) {
  return contrastRatio(colors[Ui.DeckColorRole.Accent], colors[Ui.DeckColorRole.Background]) >= minimumFocusContrast
}

// Focus rings use the accent. One too close to the background would hide
// keyboard focus, so rings then take whichever of accent and text stands out
// more; highlights keep the accent either way.
function focusColor(accent, text, background) {
  const accentContrast = colorContrast(accent, background)
  if (accentContrast >= minimumFocusContrast) return accent

  return colorContrast(text, background) > accentContrast ? text : accent
}
