import QtQuick
import "Protocol.js" as Protocol
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(context) {
    Qt.callLater(function() {
      host.expect(host.part_pane.renderedDetailCount === 1, "detail pane renders one field")
      host.expect(host.part_pane.visible, "detail pane shows structured fields")

      host.part_pane.result = ({
        title: "Body",
        details: [],
        detailMarkdown: "A **Markdown** body"
      })
      Qt.callLater(function() {
        host.expect(host.part_pane.visible && host.part_pane.hasBody,
          "detail pane shows an optional Markdown body")

        host.part_pane.result = ({ title: "Preview", kind: "file", details: [] })
        host.part_pane.filePreview = ({
          text: "preview content",
          language: "text",
          truncated: false
        })
        Qt.callLater(function() {
          host.expect(host.part_pane.visible && host.part_pane.hasFilePreview,
            "detail pane shows file preview content")

          host.part_pane.filePreview = null
          host.part_pane.previewError = "Preview offline"
          host.part_pane.result = ({ title: "Application", kind: "application", details: [] })
          host.expect(!host.part_pane.hasFilePreview && !host.part_pane.visible,
            "file preview state does not leak into non-file results")

          const longValue = "a very long value ".repeat(30)
          host.part_pane.previewError = ""
          host.part_pane.result = ({
            title: "Long field",
            details: [{ label: "Value", value: longValue, monospace: false }]
          })
          Qt.callLater(function() {
            host.expect(host.part_pane.renderedDetailCount === 1, "detail pane keeps long values in one field")
            host.expect(host.part_pane.renderedValueAt(0) === longValue,
              "detail pane renders the complete long value")
            host.expect(host.part_pane.renderedValueWrapModeAt(0) === Text.WrapAnywhere,
              "detail pane wraps long values anywhere")
            host.expect(host.part_pane.renderedValueWidthAt(0) > 0,
              "detail pane allocates width to long values")
            host.expect(host.part_pane.renderedValueHeightAt(0) > host.part_theme.fontBody * 2,
              "detail pane lays long values out across multiple lines")
            host.part_pane.result = ({
              title: "Matched fields",
              details: [],
              ranking: {
                matches: [
                  {
                    field: "path",
                    value: "/tmp/<unsafe>-notes.md",
                    ranges: [{ start: 5, length: 8 }]
                  },
                  {
                    field: "content",
                    value: "literal <script> marker",
                    ranges: [{ start: 8, length: 8 }]
                  }
                ]
              }
            })
            Qt.callLater(function() {
              host.expect(host.part_pane.renderedMatchCount === 2,
                "detail pane renders every non-title matched field")
              host.expect(host.part_pane.renderedMatchMarkupAt(0).indexOf("&lt;unsafe&gt;") >= 0,
                "matched field rendering escapes indexed markup")
              host.expect(host.part_pane.renderedMatchMarkupAt(1).indexOf("&lt;script&gt;") >= 0,
                "content highlights remain literal text")

              // A calculator result is an answer display: the value block shows and
              // the redundant Expression detail + Matched-metadata rows are gated off.
              host.part_pane.result = ({
                kind: "calculator",
                title: "40.56 °C",
                subtitle: "105 °F to °C",
                details: [{ label: "Expression", value: "105 °F to °C", monospace: true }],
                actions: [{ id: "copy-result", title: "Copy Result" }],
                ranking: { matches: [{ field: "metadata", value: "105F in C", ranges: [] }] }
              })
              Qt.callLater(function() {
                host.expect(host.part_pane.isCalculator, "detail pane recognises a calculator result")
                host.expect(host.part_pane.calcAnswerVisible && host.part_pane.calcValue === "40.56 °C",
                  "the calculator answer block shows the value")
                host.expect(host.part_pane.calcActionTitle === "Copy Result",
                  "the calculator button reuses the primary action title shown in the footer")
                host.expect(!host.part_pane.detailsSectionVisible,
                  "the redundant Expression detail is hidden for a calculator result")
                host.expect(!host.part_pane.matchedListVisible,
                  "the Matched-metadata list is hidden for a calculator result")

                host.part_pane.result = ({
                  kind: "file",
                  title: "notes.md",
                  subtitle: "/tmp/notes.md",
                  details: [{ label: "Path", value: "/tmp/notes.md", monospace: true }]
                })
                Qt.callLater(function() {
                  host.expect(!host.part_pane.isCalculator && !host.part_pane.calcAnswerVisible,
                    "a non-calculator result shows no answer block")
                  host.expect(host.part_pane.detailsSectionVisible,
                    "details still render for a non-calculator result")

                  host.verifyIntegratedSurface(function() {
                    host.part_integratedDeck.open("{}")
                    host.part_integratedDeck.reconciliationPending = false
                    host.part_integratedDeck.daemonAvailable = true
                    const herdrSpace = { id: "herdr:workspace:w1", sourceId: "herdr", kind: "workspace", title: "Deck",
                      metadata: { herdrWorkspaceId: "w1" } }
                    host.part_integratedDeck.invokeAction(herdrSpace, { id: "browse-tabs", kind: "browse-herdr-tabs" }, false)
                    let herdrRequest = host.part_integratedDeck.sentRequests[host.part_integratedDeck.sentRequests.length - 1]
                    host.expect(herdrRequest.operation === Protocol.Operations.Search && herdrRequest.herdrWorkspaceId === "w1"
                      && herdrRequest.expression.includeFilterIds[0] === "herdr-tabs",
                      "Herdr browse actions send an exact space scope with the tab filter")
                    const createTab = { id: "new-tab", kind: "herdr-create-tab", title: "New tab in this space",
                      inputs: [{ name: "Name", defaultValue: "New tab" }] }
                    host.part_integratedDeck.invokeAction(herdrSpace, createTab, false)
                    host.expect(host.part_integratedDeck.part_invocationController.actionInputOpen && host.part_integratedDeck.part_invocationController.actionInputResult.id === herdrSpace.id,
                      "Herdr creation opens the name dialog bound to the selected space")
                    host.part_integratedDeck.part_invocationController.actionInputOpen = false
                    host.part_integratedDeck.invokeAction(herdrSpace, createTab, false, { Name: "literal name" })
                    herdrRequest = host.part_integratedDeck.sentRequests[host.part_integratedDeck.sentRequests.length - 1]
                    host.expect(herdrRequest.operation === Protocol.Operations.Invoke && herdrRequest.resultId === herdrSpace.id
                      && herdrRequest.inputValues.Name === "literal name", "Herdr edits send only the declared name input")
                    const beforeDuplicate = host.part_integratedDeck.sentRequests.length
                    host.part_integratedDeck.invokeAction(herdrSpace, createTab, false, { Name: "duplicate" })
                    host.expect(host.part_integratedDeck.sentRequests.length === beforeDuplicate, "pending Herdr creation cannot be submitted twice")
                    const editFocusRevision = host.part_integratedDeck.focusRevision
                    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: herdrRequest.requestId,
                      actionResult: { succeeded: true, message: "Created Herdr tab." } })
                    host.expect(host.part_integratedDeck.focusRevision === editFocusRevision,
                      "Herdr edit acknowledgements preserve keyboard navigation made while waiting")
                    herdrRequest = host.part_integratedDeck.sentRequests[host.part_integratedDeck.sentRequests.length - 1]
                    host.expect(host.part_integratedDeck.opened && herdrRequest.operation === Protocol.Operations.Search && herdrRequest.herdrWorkspaceId === "w1",
                      "successful Herdr edits refresh the scoped browser and keep the launcher open")
                    host.part_integratedDeck.invokeAction(herdrSpace, createTab, false, { Name: "uncertain" })
                    herdrRequest = host.part_integratedDeck.sentRequests[host.part_integratedDeck.sentRequests.length - 1]
                    const beforeFailure = host.part_integratedDeck.sentRequests.length
                    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: herdrRequest.requestId,
                      actionResult: { succeeded: false, message: "Refresh before trying again." } })
                    host.expect(host.part_integratedDeck.opened && host.part_integratedDeck.statusText === "Refresh before trying again."
                      && host.part_integratedDeck.sentRequests.length === beforeFailure, "unconfirmed Herdr edits remain visible without an automatic retry")
                    host.part_integratedDeck.close()
                    host.part_integratedDeck.open("{}")
                    host.part_integratedDeck.reconciliationPending = false
                    host.part_integratedDeck.daemonAvailable = true
                    host.part_integratedDeck.invokeAction({ id: "folder:test" }, { id: "open", kind: "browse-folder", arguments: { path: "/tmp/a folder" } }, false)
                    const browseRequest = host.part_integratedDeck.sentRequests[host.part_integratedDeck.sentRequests.length - 1]
                    host.expect(browseRequest.operation === Protocol.Operations.Search && browseRequest.browseDirectory === "/tmp/a folder",
                      "folder actions request in-launcher browsing with the exact directory")
                    host.expect(!browseRequest.herdrWorkspaceId, "closing and reopening clears the previous Herdr space scope")
                    const project = { id: "project:test", title: "Project" }
                    const openProject = { id: "open-herdr", kind: "herdr-open-project" }
                    host.part_integratedDeck.invokeAction(project, openProject, false)
                    const projectRequest = host.part_integratedDeck.sentRequests[host.part_integratedDeck.sentRequests.length - 1]
                    host.expect(projectRequest.operation === Protocol.Operations.Invoke && projectRequest.resultId === project.id
                      && projectRequest.actionId === "open-herdr" && !projectRequest.inputValues,
                      "opening a project in Herdr sends cached identities without a client path")
                    const projectCount = host.part_integratedDeck.sentRequests.length
                    host.part_integratedDeck.invokeAction(project, openProject, false)
                    host.expect(host.part_integratedDeck.sentRequests.length === projectCount, "a pending Herdr project open cannot be submitted twice")
                    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: projectRequest.requestId,
                      actionResult: { succeeded: false, message: "Switched Herdr space, but could not focus its window." } })
                    host.expect(host.part_integratedDeck.opened && host.part_integratedDeck.statusText.indexOf("could not focus") >= 0
                      && host.part_integratedDeck.sentRequests.length === projectCount, "Herdr project focus failures stay visible without automatic retry")
                    host.part_integratedDeck.invokeAction({ id: "file:test", title: "Test" }, { id: "collect", kind: "collect-file", arguments: { path: "/tmp/a folder/test" } }, false)
                    const selectionRequest = host.part_integratedDeck.sentRequests[host.part_integratedDeck.sentRequests.length - 1]
                    host.expect(selectionRequest.operation === Protocol.Operations.FilesSelect && selectionRequest.enabled,
                      "collect actions send a server-owned selection request")
                    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.FileSelection, requestId: selectionRequest.requestId,
                      fileSelection: [{ id: "file:test", title: "Test", path: "/tmp/a folder/test" }] })
                    host.expect(host.part_integratedDeck.fileSelection.length === 1,
                      "selection acknowledgements update the file bar")
                    host.part_integratedDeck.runFileSelection(Protocol.FileSelectionActionIds.CopyPaths)
                    const batchRequest = host.part_integratedDeck.sentRequests[host.part_integratedDeck.sentRequests.length - 1]
                    host.expect(batchRequest.operation === Protocol.Operations.FilesBatch && batchRequest.actionId === Protocol.FileSelectionActionIds.CopyPaths,
                      "copy paths invokes the retained selection")
                    host.part_integratedDeck.handleProtocolDisconnected()
                    host.expect(host.part_integratedDeck.fileSelection.length === 0 && host.part_integratedDeck.activeFileSelectionRequestId === 0,
                      "disconnect clears selection state and pending requests")
                    host.part_integratedDeck.close()
                    host.expect(host.part_integratedDeck.fileSelection.length === 0,
                      "closing clears the launcher file selection")
                    for (const scenario of [
                      { preferences: null, succeeded: true, output: "", staysOpen: false },
                      { preferences: { hideAfterAction: false }, succeeded: true, output: "", staysOpen: true },
                      { preferences: { hideAfterAction: true }, succeeded: false, output: "", staysOpen: true },
                      { preferences: { hideAfterAction: true }, succeeded: true, output: "Script output", staysOpen: true }
                    ]) {
                      host.part_integratedDeck.opened = true
                      host.part_integratedDeck.launcherPreferences = scenario.preferences
                      host.part_integratedDeck.part_invocationController.refreshAfterAction = false
                      host.part_integratedDeck.part_invocationController.requestId = 9901
                      host.part_integratedDeck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: 9901,
                        actionResult: { succeeded: scenario.succeeded, detailMarkdown: scenario.output } })
                      host.expect(host.part_integratedDeck.opened === scenario.staysOpen,
                        "hide-after-action honors default, opt-out, failure, and output")
                    }
                    host.part_integratedDeck.close()
                    if (host.assertions === 0) {
                      host.failures++
                      console.error("FAIL QML harness executed zero assertions")
                    }
                    host.verifyOmarchyMenuBrowse()
                    host.verifyHerdrLiveUpdates()
                  })
                })
              })
            })
          })
        })
      })
    })  }
}
