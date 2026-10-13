import QtQuick
import QtQuick.Controls

// A Button that Enter and Return press under every Qt platform theme. Qt's
// generic theme maps only Space to buttons; the GTK theme that Omarchy sets
// also maps Enter. A key handled here never reaches the theme's own press, so
// the button fires once, and a held key does not repeat it. Instances must not
// add their own Return or Enter handler, because both would run.
Button {
  id: root

  function pressFromKeyboard(event) {
    if (!event.isAutoRepeat) root.click()
    event.accepted = true
  }

  Keys.onReturnPressed: event => root.pressFromKeyboard(event)
  Keys.onEnterPressed: event => root.pressFromKeyboard(event)
}
