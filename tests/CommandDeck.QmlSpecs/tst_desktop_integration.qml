import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

Item {
  id: scene
  width: 480
  height: 320

  CommandDeck.DesktopIntegration {
    id: genericDesktop
  }

  CommandDeck.DesktopIntegration {
    id: capableDesktop
    integrationId: "test"
    capabilities: CommandDeck.DesktopCapabilities {
      globalShortcuts: true
      shortcutInventory: true
      windows: true
      windowPreviews: true
      workspaces: true
      workspaceLayouts: true
    }
  }

  TestCase {
    name: "DesktopIntegration"
    when: windowShown

    function test_generic_adapter_fails_closed() {
      compare(genericDesktop.integrationId, "generic")
      compare(genericDesktop.screens.length, 0)
      verify(!genericDesktop.capabilities.globalShortcuts)
      verify(!genericDesktop.capabilities.shortcutInventory)
      verify(!genericDesktop.capabilities.windows)
      verify(!genericDesktop.capabilities.windowPreviews)
      verify(!genericDesktop.capabilities.workspaces)
      verify(!genericDesktop.capabilities.workspaceLayouts)
      verify(!genericDesktop.capabilities.workspaceSnapshots)
    }

    function test_host_adapter_opts_into_each_supported_capability() {
      compare(capableDesktop.integrationId, "test")
      verify(capableDesktop.capabilities.globalShortcuts)
      verify(capableDesktop.capabilities.shortcutInventory)
      verify(capableDesktop.capabilities.windows)
      verify(capableDesktop.capabilities.windowPreviews)
      verify(capableDesktop.capabilities.workspaces)
      verify(capableDesktop.capabilities.workspaceLayouts)
      verify(!capableDesktop.capabilities.workspaceSnapshots)
    }
  }
}
