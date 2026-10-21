import QtQuick

// The launcher's QML-only display preferences: size, transparency, layout,
// closing behaviour and colours. Each lives in a small file under
// $XDG_STATE_HOME/commanddeck/ and applies immediately, without the daemon.
// The host mirrors all but the colours into its own state; its themes read the
// colour store directly.
QtObject {
  id: preferences

  required property var host

  readonly property Connections availability: Connections {
    target: preferences.host
    function onDaemonAvailableChanged() {
      if (preferences.host.daemonAvailable && preferences.displayScaleStore.active)
        preferences.displayScaleStore.load()
    }
  }

  readonly property DisplayScaleStore displayScaleStore: DisplayScaleStore {
    active: !preferences.host.testMode
    stateDir: preferences.host.coachStateDir
    onChanged: preferences.host.displaySizePercent = preferences.displayScaleStore.percent
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
      preferences.host.launcherWidthPercent = preferences.layoutStore.widthPercent
      preferences.host.launcherHeightPercent = preferences.layoutStore.heightPercent
      preferences.host.compactLayout = preferences.layoutStore.compact
      preferences.host.previewPaneVisible = preferences.layoutStore.preview
    }
  }

  readonly property LauncherBehaviorStore behaviorStore: LauncherBehaviorStore {
    active: !preferences.host.testMode
    stateDir: preferences.host.coachStateDir
    onChanged: {
      preferences.host.escapeClearsSearch = preferences.behaviorStore.escapeClears
      preferences.host.keepSearchSeconds = preferences.behaviorStore.keepSeconds
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
    preferences.behaviorStore.load()
    preferences.colorThemeStore.load()
  }
}
