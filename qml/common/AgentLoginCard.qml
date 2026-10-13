pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol

Rectangle {
  required property var host
  readonly property alias part_deviceCodeButton: deviceCodeButton
  readonly property alias part_browserLoginButton: browserLoginButton
  readonly property alias part_apiKeyButton: apiKeyButton
  readonly property alias part_loginCodeField: loginCodeField
  readonly property alias part_loginCodeSubmitButton: loginCodeSubmitButton
  readonly property alias part_copyCodeButton: copyCodeButton
  readonly property alias part_copyUrlButton: copyUrlButton
  readonly property alias part_openLoginButton: openLoginButton
  readonly property alias part_loginCancelButton: loginCancelButton
  readonly property alias part_backendSettingsButton: backendSettingsButton

  id: loginCard
  Layout.fillWidth: true
  Layout.preferredHeight: loginColumn.implicitHeight + loginCard.host.theme.gap * 2
  Layout.minimumHeight: Layout.preferredHeight
  visible: loginCard.host.loginCardVisible
  radius: loginCard.host.theme.radiusSmall
  color: loginCard.host.theme.surface
  border.width: 1
  border.color: loginCard.host.theme.separator

  ColumnLayout {
    id: loginColumn
    anchors.fill: parent
    anchors.margins: loginCard.host.theme.gap
    spacing: loginCard.host.theme.gap

    Text {
      objectName: "agentLoginMessage"
      Layout.fillWidth: true
      textFormat: Text.PlainText
      text: loginCard.host.controller.loginStarting
        ? "Starting sign-in…"
        : loginCard.host.login && loginCard.host.login.message
        ? String(loginCard.host.login.message)
        : "Sign in to " + loginCard.host.backendDisplayName + " to start a conversation."
      color: loginCard.host.theme.text
      wrapMode: Text.Wrap
      font.family: loginCard.host.theme.fontFamily
      font.pixelSize: loginCard.host.theme.fontBody
    }

    Text {
      id: loginError
      objectName: "agentLoginError"
      Layout.fillWidth: true
      visible: loginCard.host.controller.loginError.length > 0
      textFormat: Text.PlainText
      text: loginCard.host.controller.loginError
      color: loginCard.host.theme.urgent
      wrapMode: Text.Wrap
      font.family: loginCard.host.theme.fontFamily
      font.pixelSize: loginCard.host.theme.fontBody
      Accessible.role: Accessible.StaticText
      Accessible.name: "Sign-in error: " + text
    }

    Text {
      objectName: "agentLoginActionStatus"
      Layout.fillWidth: true
      visible: loginCard.host.controller.loginActionStatus.length > 0
      textFormat: Text.PlainText
      text: loginCard.host.controller.loginActionStatus
      color: loginCard.host.theme.textMuted
      wrapMode: Text.Wrap
      font.family: loginCard.host.theme.fontFamily
      font.pixelSize: loginCard.host.theme.fontBody
      Accessible.role: Accessible.StaticText
      Accessible.name: text
    }

    Text {
      Layout.fillWidth: true
      visible: loginCard.host.codexPathWarning.length > 0
      textFormat: Text.PlainText
      text: loginCard.host.codexPathWarning
      color: loginCard.host.theme.textMuted
      wrapMode: Text.Wrap
      font.family: loginCard.host.theme.fontFamily
      font.pixelSize: loginCard.host.theme.fontBody
      Accessible.role: Accessible.StaticText
      Accessible.name: text
    }

    Text {
      Layout.fillWidth: true
      visible: loginCard.host.codexBinaryError
      text: "Choose another assistant backend in AI Settings. To use Codex, set agent.codexBinaryPath to a direct executable."
      color: loginCard.host.theme.textMuted
      wrapMode: Text.Wrap
      font.family: loginCard.host.theme.fontFamily
      font.pixelSize: loginCard.host.theme.fontBody
      Accessible.role: Accessible.StaticText
      Accessible.name: text
    }

    AgentButton {
      id: backendSettingsButton
      theme: loginCard.host.theme
      objectName: "agentChooseBackend"
      visible: loginCard.host.backend && String(loginCard.host.backend.id || "") === Protocol.AgentBackendIds.Codex
        && (loginCard.host.codexBinaryError || loginCard.host.codexPathWarning.length > 0)
      text: "Choose Backend"
      Accessible.name: "Choose another assistant backend in AI Settings"
      onClicked: loginCard.host.backendSettingsRequested()
      Keys.onTabPressed: loginCard.host.moveFocus(1)
      Keys.onBacktabPressed: loginCard.host.moveFocus(-1)
    }

    Text {
      id: deviceCodeLabel
      objectName: "agentDeviceCode"
      Layout.fillWidth: true
      visible: loginCard.host.login !== null && loginCard.host.login !== undefined
        && String(loginCard.host.login.userCode || "").length > 0
      textFormat: Text.PlainText
      text: loginCard.host.login ? String(loginCard.host.login.userCode || "") : ""
      color: loginCard.host.theme.accent
      font.family: "monospace"
      font.pixelSize: loginCard.host.theme.fontHeading
      font.weight: Font.DemiBold
      Accessible.role: Accessible.StaticText
      Accessible.name: "Device code " + text
    }

    Text {
      id: verificationUrlLabel
      Layout.fillWidth: true
      visible: loginCard.host.login !== null && loginCard.host.login !== undefined
        && String(loginCard.host.login.verificationUrl || "").length > 0
      textFormat: Text.PlainText
      text: loginCard.host.login ? String(loginCard.host.login.verificationUrl || "") : ""
      color: loginCard.host.theme.textMuted
      wrapMode: Text.WrapAnywhere
      font.family: "monospace"
      font.pixelSize: loginCard.host.theme.fontSmall
      Accessible.role: Accessible.StaticText
      Accessible.name: "Verification address " + text
    }

    Flow {
      Layout.fillWidth: true
      spacing: loginCard.host.theme.gap

      AgentButton {
        id: deviceCodeButton
        theme: loginCard.host.theme
        kind: Ui.ButtonVariant.Primary
        objectName: "agentLoginDeviceCode"
        visible: loginCard.host.loginMethods.indexOf(Protocol.AgentLoginMethods.DeviceCode) >= 0 && !loginCard.host.loginPending
        enabled: !loginCard.host.controller.loginStarting
        text: "Sign In With Device Code"
        Accessible.name: "Start device code sign in"
        onClicked: loginCard.host.loginRequested(Protocol.AgentLoginMethods.DeviceCode)
        Keys.onTabPressed: loginCard.host.moveFocus(1)
        Keys.onBacktabPressed: loginCard.host.moveFocus(-1)
      }
      AgentButton {
        id: browserLoginButton
        theme: loginCard.host.theme
        kind: Ui.ButtonVariant.Primary
        objectName: "agentLoginBrowser"
        visible: loginCard.host.loginMethods.indexOf(Protocol.AgentLoginMethods.Browser) >= 0 && !loginCard.host.loginPending
        enabled: !loginCard.host.controller.loginStarting
        text: "Sign In With Browser"
        Accessible.name: "Start browser sign in"
        Accessible.description: "Opens the backend's own sign-in page in a sandboxed helper"
        onClicked: loginCard.host.loginRequested(Protocol.AgentLoginMethods.Browser)
        Keys.onTabPressed: loginCard.host.moveFocus(1)
        Keys.onBacktabPressed: loginCard.host.moveFocus(-1)
      }
      AgentButton {
        id: apiKeyButton
        theme: loginCard.host.theme
        objectName: "agentLoginApiKey"
        visible: loginCard.host.loginMethods.indexOf(Protocol.AgentLoginMethods.ApiKey) >= 0 && !loginCard.host.loginPending
        enabled: !loginCard.host.controller.loginStarting
        text: "Use API Key"
        Accessible.name: "Sign in with an API key"
        onClicked: loginCard.host.loginRequested(Protocol.AgentLoginMethods.ApiKey)
        Keys.onTabPressed: loginCard.host.moveFocus(1)
        Keys.onBacktabPressed: loginCard.host.moveFocus(-1)
      }
      AgentField {
        id: loginCodeField
        theme: loginCard.host.theme
        objectName: "agentLoginCode"
        visible: loginCard.host.loginCodeNeeded
        placeholderText: "Paste the code from the browser"
        Accessible.name: "Sign-in code"
        Accessible.description: "The one-time code the sign-in page shows; Enter submits it"
        onVisibleChanged: if (visible) Qt.callLater(function() { loginCodeField.forceActiveFocus() })
        onAccepted: {
          if (text.trim().length > 0) loginCard.host.loginCodeSubmitted(text.trim())
        }
        Keys.onTabPressed: loginCard.host.moveFocus(1)
        Keys.onBacktabPressed: loginCard.host.moveFocus(-1)
      }
      AgentButton {
        id: loginCodeSubmitButton
        theme: loginCard.host.theme
        kind: Ui.ButtonVariant.Primary
        objectName: "agentLoginCodeSubmit"
        visible: loginCard.host.loginCodeNeeded
        enabled: loginCodeField.text.trim().length > 0
        text: "Submit Code"
        Accessible.name: "Submit the sign-in code"
        onClicked: loginCard.host.loginCodeSubmitted(loginCodeField.text.trim())
        Keys.onTabPressed: loginCard.host.moveFocus(1)
        Keys.onBacktabPressed: loginCard.host.moveFocus(-1)
      }
      AgentButton {
        id: copyCodeButton
        theme: loginCard.host.theme
        objectName: "agentCopyCode"
        text: "Copy Code"
        visible: deviceCodeLabel.visible
        enabled: deviceCodeLabel.visible
        Accessible.name: "Copy the device code"
        onClicked: loginCard.host.copyCodeRequested()
        Keys.onTabPressed: loginCard.host.moveFocus(1)
        Keys.onBacktabPressed: loginCard.host.moveFocus(-1)
      }
      AgentButton {
        id: copyUrlButton
        theme: loginCard.host.theme
        objectName: "agentCopyUrl"
        text: "Copy URL"
        visible: verificationUrlLabel.visible
        enabled: verificationUrlLabel.visible
        Accessible.name: "Copy the verification address"
        onClicked: loginCard.host.copyUrlRequested()
        Keys.onTabPressed: loginCard.host.moveFocus(1)
        Keys.onBacktabPressed: loginCard.host.moveFocus(-1)
      }
      AgentButton {
        id: openLoginButton
        theme: loginCard.host.theme
        objectName: "agentOpenLoginPage"
        text: "Open Login Page"
        visible: verificationUrlLabel.visible
        enabled: verificationUrlLabel.visible
        Accessible.name: "Open the verification address"
        onClicked: loginCard.host.openLoginPageRequested()
        Keys.onTabPressed: loginCard.host.moveFocus(1)
        Keys.onBacktabPressed: loginCard.host.moveFocus(-1)
      }
      AgentButton {
        id: loginCancelButton
        theme: loginCard.host.theme
        objectName: "agentLoginCancel"
        text: "Cancel"
        visible: loginCard.host.loginPending
        Accessible.name: "Cancel the pending sign in"
        onClicked: loginCard.host.loginCancelRequested()
        Keys.onTabPressed: loginCard.host.moveFocus(1)
        Keys.onBacktabPressed: loginCard.host.moveFocus(-1)
      }
    }
  }
}
