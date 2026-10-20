import QtQuick

// Colours and Omarchy tokens are shared; geometry and typography belong to
// the window presenting them. Tokens already contain the shell's font scaling.
Theme {
  required property var source
  required property var sizing

  loadShellTokens: false
  tokens: source.tokens
  displayScale: sizing.effective
  textScale: sizing.textScale
  reduceMotion: source.reduceMotion
  scrimStrength: source.scrimStrength
  panelTransparencyEnabled: source.panelTransparencyEnabled
  panelTransparency: source.panelTransparency
  overrideHostColors: source.overrideHostColors
  deckColors: source.deckColors
}
