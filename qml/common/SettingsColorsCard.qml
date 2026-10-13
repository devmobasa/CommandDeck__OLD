pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "UiConstants.js" as Ui
import "ColorThemeFormat.js" as Format

// Settings → Customization → Colors. Follow the active Omarchy theme, or
// override it with CommandDeck's own palette and tune that palette's accent,
// background, and text. Changes apply immediately and persist without the
// daemon; fonts and sizes keep following Omarchy either way.
SettingsCard {
  id: card

  required property var host
  readonly property bool usingDeck: !card.host.hostThemeAvailable
    || card.host.colorThemeSource === Ui.ColorThemeSource.CommandDeck
  readonly property var colors: card.host.deckColors || Format.defaultColors()
  readonly property bool readable: Format.readable(card.colors)
  // Below 3:1 against the background, focus rings use the text colour.
  readonly property bool accentVisible: Format.accentVisible(card.colors)
  readonly property bool customized: !Format.sameColors(card.colors, Format.defaultColors())
  readonly property alias sourceControl: sourceChoice
  readonly property alias accentControl: accentRow
  readonly property alias backgroundControl: backgroundRow
  readonly property alias textControl: textRow
  readonly property alias resetControl: resetButton
  readonly property alias accentWarningControl: accentWarning
  readonly property var sourceOptions: [
    { id: Ui.ColorThemeSource.Omarchy, title: "Omarchy theme",
      description: "Follow the active Omarchy theme's colors." },
    { id: Ui.ColorThemeSource.CommandDeck, title: "CommandDeck theme",
      description: "Use CommandDeck's own palette, adjusted below." }
  ]

  function sourceIndex() {
    return card.usingDeck ? 1 : 0
  }

  function changeColor(role, value) {
    const colors = {}
    colors[role] = value
    card.host.colorThemeChanged({ colors: colors })
  }

  searchQuery: card.host.part_settingsSearch.text
  searchTerms: "customization colors colours theme omarchy commanddeck palette accent background text override"
  theme: card.host.theme
  title: "Colors"
  description: card.host.hostThemeAvailable
    ? "Follow the active Omarchy theme, or override it with CommandDeck's own palette. Applies immediately."
    : "The Omarchy theme is unavailable here, so CommandDeck's palette is in use. Applies immediately."

  SettingsRow {
    theme: card.host.theme
    label: "Theme"
    help: "Fonts and sizes still follow Omarchy."
    divider: card.usingDeck

    SettingsChoice {
      id: sourceChoice
      theme: card.host.theme
      Layout.fillWidth: true
      enabled: card.host.hostThemeAvailable
      options: card.sourceOptions
      Accessible.name: "Launcher colors"
      Accessible.description: "Follow the Omarchy theme or use CommandDeck's own palette"
      Component.onCompleted: currentIndex = card.sourceIndex()
      Connections {
        target: card
        function onUsingDeckChanged() { sourceChoice.currentIndex = card.sourceIndex() }
      }
      onActivated: index => card.host.colorThemeChanged({ source: card.sourceOptions[index].id })
    }
  }

  SettingsColorRow {
    id: accentRow
    visible: card.usingDeck
    theme: card.host.theme
    label: "Accent"
    help: "Highlights, matches, and focus rings."
    roleName: "Accent"
    presets: Ui.DeckColorPresets.Accent
    value: card.colors[Ui.DeckColorRole.Accent]
    onPicked: value => card.changeColor(Ui.DeckColorRole.Accent, value)
  }

  SettingsColorRow {
    id: backgroundRow
    visible: card.usingDeck
    theme: card.host.theme
    label: "Background"
    help: "The launcher panel, dialogs, and pop-ups."
    roleName: "Background"
    presets: Ui.DeckColorPresets.Background
    value: card.colors[Ui.DeckColorRole.Background]
    onPicked: value => card.changeColor(Ui.DeckColorRole.Background, value)
  }

  SettingsColorRow {
    id: textRow
    visible: card.usingDeck
    theme: card.host.theme
    label: "Text"
    help: "Titles and labels; secondary text, lines, and selection derive from it."
    roleName: "Text"
    presets: Ui.DeckColorPresets.Text
    value: card.colors[Ui.DeckColorRole.Text]
    divider: false
    onPicked: value => card.changeColor(Ui.DeckColorRole.Text, value)
  }

  RowLayout {
    visible: card.usingDeck
    Layout.fillWidth: true
    spacing: Math.round(8 * card.host.theme.scale)

    ColumnLayout {
      Layout.fillWidth: true
      spacing: Math.round(4 * card.host.theme.scale)

      Text {
        Layout.fillWidth: true
        visible: !card.readable
        text: "Text and background are too similar to read comfortably."
        color: card.host.theme.urgent
        wrapMode: Text.Wrap
        font.family: card.host.theme.fontFamily
        font.pixelSize: card.host.theme.fontSmall
        Accessible.role: Accessible.StaticText
        Accessible.name: text
      }

      Text {
        id: accentWarning
        Layout.fillWidth: true
        visible: !card.accentVisible
        text: "The accent is too close to the background to show keyboard focus, so focus rings use the text color."
        color: card.host.theme.urgent
        wrapMode: Text.Wrap
        font.family: card.host.theme.fontFamily
        font.pixelSize: card.host.theme.fontSmall
        Accessible.role: Accessible.StaticText
        Accessible.name: text
      }
    }

    DeckButton {
      id: resetButton
      theme: card.host.theme
      text: "Reset colors"
      enabled: card.customized
      Accessible.name: "Reset CommandDeck colors"
      Accessible.description: "Restores the default accent, background, and text"
      onClicked: card.host.colorThemeResetRequested()
    }
  }
}
