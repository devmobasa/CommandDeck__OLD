pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

RowLayout {
  id: root

  required property var theme
  property string primaryActionTitle: ""
  property int actionCount: 0
  property bool actionsEnabled: true
  signal prefixesRequested(var invoker)
  signal helpRequested(var invoker)
  readonly property alias prefixesControl: prefixesButton
  readonly property alias helpControl: helpButton
  readonly property real discoveryWidth: prefixesButton.implicitWidth + helpButton.implicitWidth + root.spacing * 2
  property var directActions: []
  signal shortcutRequested(string actionId)
  readonly property alias firstShortcutControl: firstShortcut
  readonly property alias secondShortcutControl: secondShortcut
  property Item previousTabStop: null
  property Item nextTabStop: null
  property Item shortcutTarget: null
  property var shortcutHandler: null
  property real availableWidth: 100000
  Keys.forwardTo: shortcutTarget ? [shortcutTarget] : []
  readonly property alias primaryControl: primaryButton
  readonly property alias actionsControl: actionsButton
  readonly property Item firstControl: primaryButton.visible && primaryButton.enabled ? primaryButton
    : firstShortcut.visible && firstShortcut.enabled ? firstShortcut
    : secondShortcut.visible && secondShortcut.enabled ? secondShortcut
    : actionsButton.visible && actionsButton.enabled ? actionsButton : prefixesButton
  readonly property Item lastControl: helpButton
  signal primaryRequested()
  signal actionsRequested()

  spacing: Math.round(8 * root.theme.scale)
  Accessible.role: Accessible.Grouping
  Accessible.name: "Launcher actions and help"

  component FooterButton: Button {
    id: button
    required property var theme
    property string keyLabel
    property string keyDescription
    hoverEnabled: true
    focusPolicy: Qt.StrongFocus
    implicitWidth: labelRow.implicitWidth + leftPadding + rightPadding
    implicitHeight: Math.max(Math.round(30 * theme.scale), labelRow.implicitHeight + 8)
    leftPadding: Math.round(8 * theme.scale)
    rightPadding: leftPadding
    Accessible.name: text
    Accessible.description: keyDescription
    Keys.onPressed: event => {
      const enter = event.key === Qt.Key_Return || event.key === Qt.Key_Enter
      if (event.key === Qt.Key_F1
          || (event.key === Qt.Key_Slash && event.modifiers === Qt.ControlModifier)) {
        root.helpRequested(button)
        event.accepted = true
      } else if (root.shortcutHandler && root.shortcutHandler(event.key, event.modifiers)) {
        event.accepted = true
      } else if (enter && event.modifiers === Qt.NoModifier) {
        clicked()
        event.accepted = true
      }
    }

    background: Rectangle {
      radius: button.theme.radiusSmall
      color: button.down || button.hovered || button.activeFocus
        ? button.theme.surfaceStrong : "transparent"
      border.width: button.activeFocus ? 1 : 0
      border.color: button.theme.focusRing
    }
    contentItem: RowLayout {
      id: labelRow
      spacing: Math.round(6 * button.theme.scale)
      Text {
        textFormat: Text.PlainText
        text: button.text
        color: button.enabled ? button.theme.text : button.theme.textFaint
        font.family: button.theme.fontFamily
        font.pixelSize: button.theme.fontSmall
      }
      KeyCap {
        theme: button.theme
        label: button.keyLabel
      }
    }
  }

  FooterButton {
    id: primaryButton
    theme: root.theme
    text: root.primaryActionTitle
    keyLabel: "↵"
    keyDescription: "Enter runs the selected result's primary action"
    enabled: root.actionsEnabled
    visible: text.length > 0 && root.availableWidth >= implicitWidth + root.discoveryWidth
      + (actionsButton.visible ? actionsButton.implicitWidth + root.spacing : 0)
    KeyNavigation.backtab: root.previousTabStop
    KeyNavigation.tab: firstShortcut.visible ? firstShortcut
      : actionsButton.visible ? actionsButton : prefixesButton
    onClicked: root.primaryRequested()
  }

  FooterButton {
    id: firstShortcut
    theme: root.theme
    enabled: root.actionsEnabled
    visible: root.directActions.length > 0 && root.availableWidth >= primaryButton.implicitWidth
      + firstShortcut.implicitWidth + actionsButton.implicitWidth + root.spacing * 3 + root.discoveryWidth
    text: root.directActions.length > 0 ? root.directActions[0].title : ""
    keyLabel: root.directActions.length > 0 ? root.directActions[0].keyLabel : ""
    keyDescription: keyLabel + " runs " + text
    KeyNavigation.backtab: primaryButton.visible ? primaryButton : root.previousTabStop
    KeyNavigation.tab: secondShortcut.visible ? secondShortcut
      : actionsButton.visible ? actionsButton : prefixesButton
    onClicked: root.shortcutRequested(root.directActions[0].id)
  }

  FooterButton {
    id: secondShortcut
    theme: root.theme
    visible: root.directActions.length > 1 && firstShortcut.visible
      && root.availableWidth >= primaryButton.implicitWidth + firstShortcut.implicitWidth
        + secondShortcut.implicitWidth + actionsButton.implicitWidth + root.spacing * 4 + root.discoveryWidth
    enabled: root.actionsEnabled
    text: root.directActions.length > 1 ? root.directActions[1].title : ""
    keyLabel: root.directActions.length > 1 ? root.directActions[1].keyLabel : ""
    keyDescription: keyLabel + " runs " + text
    KeyNavigation.backtab: firstShortcut
    KeyNavigation.tab: actionsButton.visible ? actionsButton : prefixesButton
    onClicked: root.shortcutRequested(root.directActions[1].id)
  }

  FooterButton {
    id: actionsButton
    theme: root.theme
    text: "Actions"
    keyLabel: "→"
    keyDescription: "Right arrow at the end of the query, or Control K, opens actions"
    enabled: root.actionsEnabled
    visible: root.actionCount > 1 && root.availableWidth >= implicitWidth + root.discoveryWidth
    KeyNavigation.backtab: secondShortcut.visible ? secondShortcut
      : firstShortcut.visible ? firstShortcut
      : primaryButton.visible ? primaryButton : root.previousTabStop
    KeyNavigation.tab: prefixesButton
    onClicked: root.actionsRequested()
  }

  FooterButton {
    id: prefixesButton
    theme: root.theme
    text: root.availableWidth >= Math.round(460 * root.theme.scale) ? "Browse prefixes" : "Prefixes"
    keyLabel: "?"
    keyDescription: "Type ? in an empty search to browse prefixes"
    Accessible.name: "Browse prefixes"
    KeyNavigation.backtab: actionsButton.visible && actionsButton.enabled ? actionsButton
      : secondShortcut.visible && secondShortcut.enabled ? secondShortcut
      : firstShortcut.visible && firstShortcut.enabled ? firstShortcut
      : primaryButton.visible && primaryButton.enabled ? primaryButton : root.previousTabStop
    KeyNavigation.tab: helpButton
    KeyNavigation.right: helpButton
    onClicked: root.prefixesRequested(prefixesButton)
  }

  FooterButton {
    id: helpButton
    theme: root.theme
    text: "Help"
    keyLabel: "F1"
    keyDescription: "F1 or Control Slash opens keyboard and prefix help"
    KeyNavigation.backtab: prefixesButton
    KeyNavigation.left: prefixesButton
    KeyNavigation.tab: root.nextTabStop
    onClicked: root.helpRequested(helpButton)
  }
}
