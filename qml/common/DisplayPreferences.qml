import QtQuick

// The launcher's QML-only display preferences: size, transparency, layout and
// colours. Each lives in a small file under $XDG_STATE_HOME/commanddeck/ and
// applies immediately, without the daemon. The host mirrors size, transparency
// and layout into its own state; its themes read the colour store directly.
QtObject {
  id: preferences

  required property var host

  readonly property DisplayScaleStore displayScaleStore: DisplayScaleStore {
    active: !preferences.host.testMode
    stateDir: preferences.host.coachStateDir
    onChanged: preferences.host.displaySizePreset = preferences.displayScaleStore.preset
  }

  readonly property TransparencyStore transparencyStore: TransparencyStore {
    active: !preferences.host.testMode
    stateDir: preferences.host.coachStateDir
    onChanged: {
      preferences.host.panelTransparencyEnabled = preferences.transparencyStore.enabled
      preferences.host.panelTransparencyPercent = preferences.transparencyStore.percent
    }
  }

  readonly property LayoutStore layoutStore: LayoutStore {
    active: !preferences.host.testMode
    stateDir: preferences.host.coachStateDir
    onChanged: {
      preferences.host.compactLayout = preferences.layoutStore.compact
      preferences.host.previewPaneVisible = preferences.layoutStore.preview
    }
  }

  readonly property ColorThemeStore colorThemeStore: ColorThemeStore {
    active: !preferences.host.testMode
    stateDir: preferences.host.coachStateDir
  }

  function load() {
    preferences.displayScaleStore.load()
    preferences.transparencyStore.load()
    preferences.layoutStore.load()
    preferences.colorThemeStore.load()
  }
}
