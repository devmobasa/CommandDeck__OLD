import QtQuick
import Quickshell.Io
import "." as CommandDeck

// Separate asynchronous writers model daemon migration and later file edits.
// The harness lives in a fresh temporary directory; no user state is involved.
QtObject {
  id: spec
  required property var host
  property var steps: []
  property int step: 0
  property double deadline: 0
  property int percentageWrites: 0
  property bool frontendStarted: false
  property bool protectRecommended: false
  property bool recommendedReverted: false
  property int completedReads: 0
  property int readsBeforeRecommended: 0
  readonly property var store: preferences.displayScaleStore
  property var frontend: QtObject {
    property bool testMode: true
    property bool daemonAvailable: false
    property string coachStateDir: decodeURIComponent(Qt.resolvedUrl(".").toString().replace(/^file:\/\//, ""))
    property int displaySizePercent: 100
    property bool panelTransparencyEnabled: false
    property int panelTransparencyPercent: 20
    property int launcherWidthPercent: 100
    property int launcherHeightPercent: 100
    property bool compactLayout: false
    property bool previewPaneVisible: true
    property bool escapeClearsSearch: false
    property int keepSearchSeconds: 0
  }
  property CommandDeck.DisplayPreferences preferences: CommandDeck.DisplayPreferences {
    host: spec.frontend
  }
  property Connections changes: Connections {
    target: spec.store
    function onChanged() {
      if (spec.protectRecommended && (spec.store.legacy || spec.store.percent !== 100))
        spec.recommendedReverted = true
    }
  }
  property Connections writes: Connections {
    target: spec.store.file
    function onSaved() { spec.percentageWrites++ }
    function onLoaded() { spec.completedReads++ }
  }
  property FileView percentWriter: FileView {
    path: spec.frontend.coachStateDir + "/display-size"
    printErrors: false
    atomicWrites: true
    onSaved: {
      if (spec.frontendStarted) return
      spec.frontendStarted = true
      spec.frontend.testMode = false
      spec.preferences.load()
    }
  }
  property FileView profileWriter: FileView {
    path: spec.frontend.coachStateDir + "/display-sizing"
    printErrors: false
    atomicWrites: true
  }
  property Timer poll: Timer {
    interval: 20
    repeat: true
    onTriggered: {
      const current = spec.steps[spec.step]
      if (!current.ready() && Date.now() < spec.deadline) return
      spec.host.expect(current.ready(), current.name + (current.ready() ? "" : "; observed " + JSON.stringify({
        loaded: spec.store.loaded, legacy: spec.store.legacy, percent: spec.store.percent,
        legacyPercent: spec.store.legacyPercent, profile: spec.store.profile.text(), file: spec.store.file.text(),
        writes: spec.percentageWrites, reads: spec.completedReads,
        layout: spec.preferences.layoutStore.file.text()
      })))
      if (!current.ready()) { spec.finish(); return }

      current.run()
      spec.step++
      spec.deadline = Date.now() + 30000
      if (spec.step === spec.steps.length) spec.finish()
    }
  }

  function finish() {
    poll.stop()
    host.pendingAsyncSpecs--
  }

  function start() {
    host.pendingAsyncSpecs++
    steps = [
      { name: "frontend loads before daemon migration", ready: () => store.loaded,
        run: () => {
          host.expect(store.legacy && store.percent === 115, "an absent marker keeps the saved legacy preset")
          host.expect(percentageWrites === 0, "loading a preset does not rewrite its bytes")
          store.setPercent(115)
          host.expect(percentageWrites === 0, "an unchanged preset does not write")
          profileWriter.setText("logical-v1\n")
          frontend.daemonAvailable = true
        } },
      { name: "late daemon marker and explicit refresh select logical sizing", ready: () => !store.legacy,
        run: () => { profileWriter.setText("legacy-v1\n") } },
      { name: "asynchronous profile update restores the legacy reference",
        ready: () => store.legacy && store.legacyPercent === 115
          && store.profile.text() === "legacy-v1:115\n",
        run: () => { store.setPercent(120); store.load() } },
      { name: "a pending zoom survives refresh and persists with its reference",
        ready: () => percentageWrites > 0 && store.percent === 120 && store.legacyPercent === 115
          && store.file.text() === "120\n" && store.profile.text() === "legacy-v1:115\n",
        run: () => { store.percent = 100; store.load() } },
      { name: "a completed reload restores durable zoom without changing the reference",
        ready: () => store.percent === 120 && store.legacy && store.legacyPercent === 115,
        run: () => {
          readsBeforeRecommended = completedReads
          store.load()
          store.useRecommended()
          protectRecommended = true
        } },
      { name: "Recommended survives an in-flight read and asynchronous writes",
        ready: () => store.profile.text() === "logical-v1\n" && store.file.text() === "100\n"
          && !store.legacy && store.percent === 100 && percentageWrites >= 2
          && completedReads > readsBeforeRecommended,
        run: () => {
          host.expect(!recommendedReverted, "completed stale reads cannot undo a local Recommended choice")
          protectRecommended = false
          profileWriter.setText("future-profile\n")
        } },
      { name: "unknown profile falls back safely", ready: () => store.legacy,
        run: () => { store.setPercent(125) } },
      { name: "zoom preserves unknown profile bytes", ready: () => store.percent === 125 && percentageWrites >= 3,
        run: () => {
          host.expect(store.profile.text() === "future-profile\n", "zoom does not overwrite an unknown profile")
          store.useRecommended()
        } },
      { name: "explicit Recommended can replace an unknown profile",
        ready: () => !store.legacy && store.percent === 100 && store.profile.text() === "logical-v1\n",
        run: () => { preferences.layoutStore.setSize(123, 137) } },
      { name: "layout dimensions persist alongside the old layout choices",
        ready: () => preferences.layoutStore.file.text() === "compact=off\npreview=on\nwidth=125\nheight=135\n",
        run: () => {
          preferences.layoutStore.widthPercent = 100
          preferences.layoutStore.heightPercent = 100
          preferences.layoutStore.load()
          host.expect(frontend.launcherWidthPercent === 125 && frontend.launcherHeightPercent === 135,
            "reloading layout preferences restores the durable panel choices")
        } }
    ]
    deadline = Date.now() + 30000
    percentWriter.setText(" LARGE \n")
    poll.start()
  }
}
