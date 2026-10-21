import QtQuick
import "Protocol.js" as Protocol
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(){
    // FilterCatalog - the shared examples map derived from descriptors.
    host.expect(host.part_catalogSpec.exampleFor("svc:") === "svc: nginx"
        && host.part_catalogSpec.exampleFor("APP:") === "app: firefox",
      "filter catalog maps curated examples by canonical token")
    host.expect(host.part_catalogSpec.exampleFor("nope:") === "nope:",
      "an unmapped token falls back to the token itself")
    host.expect(host.part_catalogSpec.entries(host.slice2Filters).length === 4
        && host.part_catalogSpec.suggestionToken(host.slice2Filters) === "file:"
        && host.part_catalogSpec.isAssistantToken("codex:"),
      "filter catalog derives entries and prefers file: for guidance")
    host.expect(host.part_catalogSpec.chipEntries(host.slice2Filters, true).length === 4
        && host.part_catalogSpec.chipEntries(host.slice2Filters, true)[0].token === "="
        && host.part_catalogSpec.paletteEntries(host.slice2Filters, true).length === 6
        && host.part_catalogSpec.paletteEntries(host.slice2Filters, true)[1].token === ">"
        && host.part_catalogSpec.exampleFor(">") === "> htop",
      "the grid excludes assistant tokens while the palette lists every filter and Run")
    host.expect(host.part_helpSheet.shortcutListed(">"), "the help sheet explains the > Run prefix")

    // A2 - teaching empty-state chips.
    host.expect(host.part_emptyStateSpec.renderedChipCount === 4
        && host.part_emptyStateSpec.chipTokenAt(0) === "=",
      "empty-state lists filter chips built from descriptors, calculator first")
    let svcIndex = -1
    for (let index = 0; index < host.part_emptyStateSpec.renderedChipCount; index++) {
      if (host.part_emptyStateSpec.chipTokenAt(index) === "svc:") svcIndex = index
    }
    host.part_emptyStateSpec.insertedToken = ""
    host.part_emptyStateSpec.activateChip(svcIndex)
    host.expect(svcIndex > 0 && host.part_emptyStateSpec.insertedToken === "svc:",
      "activating an empty-state chip inserts its token")
    host.expect(host.part_emptyStateSpec.footerText.indexOf("F1") >= 0
        && host.part_emptyStateSpec.footerText.indexOf("?") >= 0,
      "empty-state footer points at F1 and the ? filter palette")
    host.part_emptyStateSpec.assistantRoutes = 0
    host.part_emptyStateSpec.assistantControl.activate()
    host.expect(host.part_emptyStateSpec.assistantRoutes === 1,
      "the pinned assistant chip routes to the assistant (D12)")
    host.part_emptyStateSpec.filtersOpens = 0
    host.part_emptyStateSpec.filtersControl.activate()
    host.expect(host.part_emptyStateSpec.filtersOpens === 1,
      "the empty-state browse-filters affordance opens the palette")

    // C9 - guiding no-results with the query and an ai: route.
    host.expect(host.part_noResultsSpec.messageText.indexOf("totallyunfindable") >= 0
        && host.part_noResultsSpec.messageText.indexOf("ai:") >= 0
        && host.part_noResultsSpec.messageText.indexOf("file:") >= 0,
      "no-results guidance names the query, a filter, and the ai: assistant")
    host.part_noResultsSpec.assistantRoutes = 0
    host.part_noResultsSpec.activateAssistant()
    host.expect(host.part_noResultsSpec.assistantRoutes === 1,
      "the no-results ai: route is activatable")

    // A3 - the ? filter palette navigation and insertion.
    host.expect(host.part_filterPaletteSpec.renderedCount === 6
        && host.part_filterPaletteSpec.entryTokenAt(0) === "="
        && host.part_filterPaletteSpec.entryTokenAt(1) === ">",
      "the filter palette lists every prefix (calculator, then Run)")
    host.part_filterPaletteSpec.selectedIndex = 0
    host.part_filterPaletteSpec.moveSelection(2)
    host.expect(host.part_filterPaletteSpec.selectedIndex === 2
        && host.part_filterPaletteSpec.selectedToken() === "app:",
      "the filter palette moves the highlight with the arrows")
    host.part_filterPaletteSpec.insertedToken = ""
    host.part_filterPaletteSpec.activateSelected()
    host.expect(host.part_filterPaletteSpec.insertedToken === "app:",
      "Enter on the filter palette inserts the highlighted token")
    host.part_filterPaletteSpec.moveSelection(-20)
    host.expect(host.part_filterPaletteSpec.selectedIndex === 0,
      "the filter palette clamps navigation at the first entry")
    host.part_filterPaletteSpec.moveSelection(50)
    host.expect(host.part_filterPaletteSpec.selectedIndex === 5
        && host.part_filterPaletteSpec.selectedToken() === "ai:",
      "the filter palette clamps navigation at the last entry and includes ai:")

    // C10 - the once-only first-run coach and its durable store.
    host.part_coachSpec.opened = true
    host.part_coachSpec.seen = false
    host.part_coachSpec.suppressed = false
    host.expect(host.part_coachSpec.shouldShow,
      "the first-run coach shows on an idle empty query")
    host.part_coachSpec.suppressed = true
    host.expect(!host.part_coachSpec.shouldShow,
      "the coach hides while a query is present or a surface is open")
    host.part_coachSpec.suppressed = false
    host.part_coachSpec.dismissals = 0
    host.part_coachSpec.dismiss()
    host.expect(host.part_coachSpec.dismissals === 1,
      "the coach emits a dismissal on Got it / any key / any click")
    host.expect(host.part_coachStoreSpec.markerPath
        === "/home/example/.local/state/commanddeck/first-run-coach.seen",
      "the coach store targets a marker under $XDG_STATE_HOME/commanddeck")
    host.part_coachStoreSpec.load()
    host.expect(!host.part_coachStoreSpec.seen,
      "a missing marker leaves the coach unseen")
    host.part_coachStoreSpec.markSeen()
    host.part_coachSpec.seen = host.part_coachStoreSpec.seen
    host.expect(host.part_coachStoreSpec.seen && !host.part_coachSpec.shouldShow,
      "once the seen flag persists the coach stays hidden")

    // The AI master switch store and the deck-side gate it drives.
    host.expect(host.part_aiFeaturesStoreSpec.filePath
        === "/home/example/.local/state/commanddeck/ai-features",
      "the AI features store targets a file under $XDG_STATE_HOME/commanddeck")
    host.part_aiFeaturesStoreSpec.load()
    host.expect(host.part_aiFeaturesStoreSpec.enabled,
      "a missing ai-features file keeps AI features on")
    host.part_aiFeaturesStoreSpec.set(false)
    host.expect(!host.part_aiFeaturesStoreSpec.enabled,
      "the AI features store records the switch being turned off")
    host.expect(host.part_aiFeaturesStoreSpec.parse("off") === false
        && host.part_aiFeaturesStoreSpec.parse("nonsense") === true,
      "only an explicit off disables AI features")
    host.part_integratedDeck.aiFeaturesEnabled = false
    host.part_integratedDeck.routedInputs = []
    host.part_integratedDeck.statusText = ""
    host.part_integratedDeck.askAssistant("hello")
    host.expect(host.part_integratedDeck.routedInputs.length === 0
        && host.part_integratedDeck.statusText === host.part_integratedDeck.aiOffStatusText
        && !host.part_integratedDeck.assistantModeActive,
      "with AI features off the assistant route is refused with a status line")
    host.part_integratedDeck.openAgentSurface()
    host.expect(!host.part_integratedDeck.agentOpen,
      "with AI features off the assistant surface does not open")
    host.part_integratedDeck.aiFeaturesEnabled = true
    host.part_integratedDeck.statusText = ""

    // D13 - the QML-only display-size store (mirrors the coach store).
    host.expect(host.part_displayStoreSpec.filePath
        === "/home/example/.local/state/commanddeck/display-size",
      "the display-size store targets a file under $XDG_STATE_HOME/commanddeck")
    host.part_displayStoreSpec.load()
    host.expect(host.part_displayStoreSpec.percent === 100,
      "loading with display-size I/O disabled defaults to 100 percent")
    host.part_displayStoreSpec.setPercent(115)
    host.expect(host.part_displayStoreSpec.percent === 115,
      "the display-size store records the chosen percentage")
    const sizes = [
      ["large", 115], ["xlarge", 130], ["small", 90], ["auto", 100], ["default", 100],
      ["117", 115], ["400", 150], ["10", 75], ["nonsense", 100], [" 120 \n", 120],
      [" LARGE \n", 115], ["127", 125], ["128", 130], ["75", 75], ["150", 150],
      ["+115", 115], ["-10", 75], ["00115", 115], ["", 100], [null, 100], [undefined, 100],
      ["115.5", 100], ["115percent", 100], ["1e2", 100], ["0x64", 100],
      ["Infinity", 100], ["NaN", 100], ["constructor", 100], ["__proto__", 100]
    ]
    for (const [value, expected] of sizes) {
      host.expect(host.part_displayStoreSpec.normalize(value) === expected,
        "display-size file value " + JSON.stringify(value) + " normalizes to " + expected)
    }

    host.expect(host.part_transparencyStoreSpec.filePath
        === "/home/example/.local/state/commanddeck/panel-transparency",
      "the transparency store targets a file under $XDG_STATE_HOME/commanddeck")
    host.part_transparencyStoreSpec.load()
    host.expect(!host.part_transparencyStoreSpec.enabled
        && host.part_transparencyStoreSpec.percent === 20,
      "a missing transparency file defaults to off at 20 percent")
    host.part_transparencyStoreSpec.set(true, 45)
    host.expect(host.part_transparencyStoreSpec.enabled && host.part_transparencyStoreSpec.percent === 45,
      "the transparency store records the toggle and amount")
    host.part_transparencyStoreSpec.set(true, 99)
    host.expect(host.part_transparencyStoreSpec.percent === 80,
      "the transparency store caps the amount so the panel stays readable")
    host.part_transparencyStoreSpec.set(false, -4)
    host.expect(!host.part_transparencyStoreSpec.enabled && host.part_transparencyStoreSpec.percent === 0,
      "the transparency store clamps a negative amount to 0")

    // B7 - the per-kind cue derives a quiet label from sourceId then kind.
    host.expect(host.part_kindCueRow.kindCue === "Service" && host.part_kindCueRow.hasKindCue,
      "a systemd-units row exposes a Service kind cue")
    host.part_kindCueRow.result = ({
      title: "Brave", subtitle: "Web browser",
      sourceId: "applications", kind: "application",
      actions: [{ id: "launch", title: "Launch" }]
    })
    host.expect(host.part_kindCueRow.kindCue === "App"
        && host.part_kindCueRow.cueTint("app") !== host.part_kindCueRow.cueTint("file"),
      "an application row shows an App cue with a kind-specific tint")
    host.part_kindCueRow.result = ({
      title: "notes.md", subtitle: "~/notes.md",
      sourceId: "files", kind: "file",
      actions: [{ id: "open", title: "Open" }]
    })
    host.expect(host.part_kindCueRow.kindCue === "File"
        && host.part_kindCueRow.accessibilityName.indexOf("File") >= 0,
      "a file row shows a File cue and announces the kind accessibly")

    // B5 - the inline "N actions" affordance is bound to selection + count.
    host.part_kindCueRow.selected = false
    host.part_kindCueRow.actionCount = 3
    host.expect(!host.part_kindCueRow.showActionHint,
      "the action affordance stays hidden on an unselected row")
    host.part_kindCueRow.selected = true
    host.expect(host.part_kindCueRow.showActionHint && host.part_kindCueRow.actionHintText === "3 actions",
      "the highlighted row surfaces the action affordance when count > 1")
    host.part_kindCueRow.actionCount = 1
    host.expect(!host.part_kindCueRow.showActionHint,
      "the action affordance hides when a row has no secondary actions")
    host.part_kindCueRow.selected = false
    host.part_kindCueRow.actionCount = 0

    host.part_kindCueRow.result = ({
      title: "Browser", subtitle: "Web browser", kind: "application",
      aliases: ["web", "browse"], matchedRanges: [{ start: 0, length: 3 }]
    })
    host.expect(host.part_kindCueRow.compact && host.part_kindCueRow.showAlias
        && host.part_kindCueRow.primaryAlias === "web"
        && host.part_kindCueRow.accessibilityName.indexOf("browse") >= 0,
      "simple app rows are compact and expose aliases without losing accessible context")
    host.expect(host.part_kindCueRow.implicitHeight < host.part_theme.rowHeight
        && host.part_kindCueRow.titleControl.text.indexOf("<b>Bro</b>") >= 0,
      "compact rows use less height and preserve match highlighting")
    host.part_kindCueRow.width = 280
    host.expect(!host.part_kindCueRow.showAlias,
      "narrow rows reserve space for the title instead of alias badges")
    host.part_kindCueRow.width = 460
    host.part_kindCueRow.result = ({
      title: "Zed", subtitle: "A high-performance, multiplayer code editor.",
      kind: "application"
    })
    host.part_kindCueRow.titleControl.forceLayout()
    host.expect(!host.part_kindCueRow.subtitleControl.truncated
        && host.part_kindCueRow.subtitleControl.elide === Text.ElideRight
        && host.part_kindCueRow.subtitleControl.x
          > host.part_kindCueRow.titleControl.x + host.part_kindCueRow.titleControl.implicitWidth,
      "a short app title leaves its description the room to read whole, without overlap")
    host.part_kindCueRow.result = ({ title: "example.png", subtitle: "example.png", kind: "file" })
    host.expect(host.part_kindCueRow.subtitleControl.text.length === 0
        && host.part_kindCueRow.accessibilityName.indexOf("example.png, example.png") < 0,
      "a subtitle that repeats the title is neither drawn nor announced twice")
    // Herdr rows cue by source first, so a space (workspace kind) and a tab both
    // read as Herdr; a bare tab kind from any source still lands in the family.
    host.part_kindCueRow.result = ({
      title: "tests", subtitle: "In CommandDeck · Tab 2 · 2 panes",
      sourceId: "herdr", kind: "tab",
      metadata: { herdrAgentStatus: "blocked" },
      actions: [{ id: "switch-tab", title: "Switch Herdr Tab" }]
    })
    host.expect(host.part_kindCueRow.kindCue === "Herdr" && host.part_kindCueRow.hasKindCue
        && host.part_kindCueRow.accessibilityName.indexOf("Herdr") >= 0
        && host.part_kindCueRow.subtitleControl.text.indexOf("In CommandDeck") === 0,
      "a Herdr tab row shows a Herdr cue, announces it, and keeps its parent space subtitle")
    host.expect(host.part_kindCueRow.statusBadgeText === "Needs attention" && host.part_kindCueRow.accessibilityName.indexOf("Needs attention") >= 0,
      "blocked Herdr tabs show and announce a Needs attention badge")
    for (const state of ["working", "idle", "unknown", ""]) {
      host.part_kindCueRow.result = { title: "tests", sourceId: "herdr", kind: "tab", metadata: { herdrAgentStatus: state } }
      host.expect(host.part_kindCueRow.statusBadgeText === (state === "working" ? "Working" : state === "idle" ? "Idle" : ""),
        "Herdr badges distinguish working and idle without treating unknown as healthy")
    }
    host.part_kindCueRow.result = ({
      title: "CommandDeck", subtitle: "Space 1 · 2 tabs · 5 panes · focused",
      sourceId: "herdr", kind: "workspace",
      actions: [{ id: "switch-space", title: "Switch Herdr Space" }]
    })
    host.expect(host.part_kindCueRow.kindCue === "Herdr"
        && host.part_kindCueRow.cueTint("herdr") === host.part_kindCueRow.cueTint("window"),
      "a Herdr space row is a Herdr row, not a Hyprland window row, with the focus-family tint")
    host.expect(host.part_resultIcon.genericNames("tab")[0] === "utilities-terminal"
        && host.part_resultIcon.glyph("tab") === "⧉"
        && host.part_resultIcon.tint("tab") === host.part_resultIcon.tint("workspace"),
      "tab results fall back to a terminal icon, a distinct glyph, and the focus tint")
    host.expect(host.part_resultIcon.candidates("\udb80\udde6", "omarchy").join(",")
        === "\udb80\udde6,omarchy,applications-system,system-run",
      "an Omarchy row whose menu icon is a font glyph falls back to the Omarchy logo first")
    host.part_kindCueRow.result = ({ title: "Right", subtitle: "Style › Menu Bar › Position", kind: "omarchy" })
    host.expect(!host.part_kindCueRow.compact && host.part_kindCueRow.implicitHeight >= host.part_theme.rowHeight,
      "Omarchy commands retain the second line for their menu path")
    host.part_kindCueRow.result = ({ title: "report.md", subtitle: "/home/user/projects/reports/report.md", kind: "file" })
    host.expect(!host.part_kindCueRow.compact && host.part_kindCueRow.subtitleControl.text.indexOf("/home/user") === 0,
      "file results retain full path context in their second line")

    host.expect(host.part_kindCueRow.toolTipControl.parent === host.part_kindCueRow
        && host.part_kindCueRow.toolTipControl.text === "report.md\n/home/user/projects/reports/report.md",
      "result tooltip is anchored to its own row and carries full file context")
    host.part_kindCueRow.result = ({ title: "<b>Signal</b>", subtitle: "Private Messenger", kind: "application", aliases: ["chat"] })
    host.expect(host.part_kindCueRow.toolTipControl.text === "<b>Signal</b>\nPrivate Messenger\nAliases: chat"
        && host.part_kindCueRow.toolTipControl.contentItem.textFormat === Text.PlainText
        && host.part_kindCueRow.toolTipControl.contentItem.color === host.part_theme.text,
      "result tooltip uses the theme and treats result titles and aliases as literal text")
    host.part_kindCueRow.result = ({ title: "example.png", subtitle: "example.png", kind: "file" })
    host.expect(host.part_kindCueRow.toolTipControl.text === "example.png",
      "a reused row never retains the previous result's tooltip text")

    host.expect(host.part_footerHintBar.primaryControl.text === "Open"
        && host.part_footerHintBar.actionsControl.text === "Actions",
      "footer exposes named primary and secondary action buttons")
    host.part_footerHintBar.actionCount = 2
    // The standalone footer is hidden; visibility checks run in real-event tests.
    host.part_footerHintBar.primaryActionTitle = "Launch"
    host.expect(host.part_footerHintBar.primaryControl.text === "Launch",
      "footer primary button follows the selected action title")
    host.part_footerHintBar.actionCount = 1
    host.part_footerHintBar.primaryActionTitle = "Open"

    // Integrated decisions and the assistant route, driven headlessly.
    host.part_integratedDeck.opened = true
    host.part_integratedDeck.navigationController.activeFilters = []
    host.part_integratedDeck.navigationController.queryText = ""
    host.part_integratedDeck.requestSearch()
    const emptySearch = host.part_integratedDeck.sentRequests.filter(
      request => request.operation === Protocol.Operations.Search).slice(-1)[0]
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done, requestId: emptySearch.requestId })
    host.expect(host.part_integratedDeck.showEmptyState && !host.part_integratedDeck.showNoResults
        && !host.part_integratedDeck.hasResults,
      "an empty query with no home content shows the teaching empty-state")

    host.part_integratedDeck.navigationController.queryText = "zzzzzznomatch"
    host.part_integratedDeck.requestSearch()
    const noHitSearch = host.part_integratedDeck.sentRequests.filter(
      request => request.operation === Protocol.Operations.Search).slice(-1)[0]
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done, requestId: noHitSearch.requestId })
    host.expect(host.part_integratedDeck.showNoResults && !host.part_integratedDeck.showEmptyState,
      "a non-empty query with no matches shows the guiding no-results state")

    host.part_integratedDeck.routedInputs = []
    host.part_integratedDeck.insertFilterToken("svc:")
    host.expect(host.part_integratedDeck.routedInputs.slice(-1)[0] === "svc:",
      "inserting a filter token drops the query into that filter")

    host.part_integratedDeck.routedInputs = []
    host.part_integratedDeck.navigationController.queryText = "fix my monitors"
    host.part_integratedDeck.askAssistant()
    const routed = host.part_integratedDeck.routedInputs.slice(-1)[0]
    host.expect(routed.indexOf("ai:") === 0 && routed.indexOf("fix my monitors") >= 0,
      "ask-assistant reuses the ai: path carrying the current text")

    host.part_integratedDeck.navigationController.activeFilters = []
    host.part_integratedDeck.navigationController.queryText = "?"
    host.expect(host.part_integratedDeck.filterPaletteOpen
        && host.part_integratedDeck.navigationController.queryText === "",
      "typing ? as the whole query opens the filter palette and clears the sentinel")
    host.part_integratedDeck.closeFilterPalette()
    host.expect(!host.part_integratedDeck.filterPaletteOpen,
      "closing the filter palette returns control to the launcher")

    // Feature A intent prewarm: entering assistant mode fires exactly one
    // agent.prewarm, debounced per entry into assistant mode, not per keystroke.
    host.part_integratedDeck.filterDescriptors = host.slice2Filters
    host.part_integratedDeck.navigationController.activeFilters = []
    host.part_integratedDeck.navigationController.queryText = ""
    host.part_integratedDeck.agentTestController.resetConversation()
    host.part_integratedDeck.agentTestController.enabled = true
    host.part_integratedDeck.sentRequests = []
    host.expect(!host.part_integratedDeck.assistantModeActive
        && host.part_integratedDeck.sentRequests.filter(
            request => request.operation === Protocol.Operations.AgentPrewarm).length === 0,
      "no intent prewarm fires while the query is not an assistant token")

    host.part_integratedDeck.navigationController.activeFilters = [{ id: "ai", excluded: false }]
    host.part_integratedDeck.navigationController.queryText = "fix"
    host.expect(host.part_integratedDeck.assistantModeActive
        && host.part_integratedDeck.assistantPrewarmArmed
        && host.part_integratedDeck.sentRequests.filter(
            request => request.operation === Protocol.Operations.AgentPrewarm).length === 1,
      "entering assistant mode fires exactly one intent prewarm")

    host.part_integratedDeck.navigationController.queryText = "fix my"
    host.part_integratedDeck.navigationController.queryText = "fix my monitors"
    host.expect(host.part_integratedDeck.sentRequests.filter(
        request => request.operation === Protocol.Operations.AgentPrewarm).length === 1,
      "typing more inside assistant mode does not re-fire the debounced prewarm")

    host.part_integratedDeck.navigationController.activeFilters = []
    host.part_integratedDeck.navigationController.queryText = ""
    host.expect(!host.part_integratedDeck.assistantModeActive
        && !host.part_integratedDeck.assistantPrewarmArmed,
      "leaving assistant mode re-arms the prewarm latch")
    host.part_integratedDeck.navigationController.activeFilters = [{ id: "ai", excluded: false }]
    host.expect(host.part_integratedDeck.sentRequests.filter(
        request => request.operation === Protocol.Operations.AgentPrewarm).length === 2,
      "re-entering assistant mode fires the intent prewarm again")

    host.part_integratedDeck.navigationController.activeFilters = []
    host.part_integratedDeck.agentTestController.enabled = false
    host.part_integratedDeck.sentRequests = []
    host.part_integratedDeck.navigationController.activeFilters = [{ id: "ai", excluded: false }]
    host.expect(host.part_integratedDeck.sentRequests.filter(
        request => request.operation === Protocol.Operations.AgentPrewarm).length === 0,
      "a disabled assistant suppresses the intent prewarm")

    host.part_integratedDeck.navigationController.activeFilters = []
    host.part_integratedDeck.agentTestController.enabled = true
    host.part_integratedDeck.sentRequests = []
    host.part_integratedDeck.openAgentSurface()
    host.expect(host.part_integratedDeck.assistantModeActive
        && host.part_integratedDeck.sentRequests.filter(
            request => request.operation === Protocol.Operations.AgentPrewarm).length === 1,
      "opening the assistant surface fires exactly one intent prewarm")
    host.part_integratedDeck.closeAgentSurface()

    // Restore a clean state for the assertions that follow. enabled returns to
    // its unqueried null so a later openAgentSurface still refreshes status.
    host.part_integratedDeck.navigationController.activeFilters = []
    host.part_integratedDeck.navigationController.queryText = ""
    host.part_integratedDeck.agentTestController.enabled = null
    host.part_integratedDeck.agentTestController.resetConversation()
  }
}
