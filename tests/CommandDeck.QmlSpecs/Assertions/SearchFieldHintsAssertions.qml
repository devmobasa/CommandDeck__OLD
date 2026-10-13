import QtQuick
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(){
    // A1 - rotating example placeholder.
    host.part_reduceMotionSearchField.inputControl.text = ""
    host.expect(!host.part_reduceMotionSearchField.placeholderRotating
        && host.part_reduceMotionSearchField.inputControl.placeholderText
          === "Search apps, files, config, windows, commands…",
      "reduced motion keeps the placeholder static on its descriptive first entry")

    host.part_searchFieldSpec.inputControl.text = ""
    host.expect(!host.part_searchFieldSpec.placeholderRotating,
      "a search field on a hidden launcher leaves its placeholder timer stopped")

    host.part_searchFieldSpec.surfaceVisible = true
    host.expect(host.part_searchFieldSpec.placeholderRotating
        && host.part_searchFieldSpec.inputControl.placeholderText
          === "Search apps, files, config, windows, commands…",
      "an empty field with motion allowed rotates from the descriptive placeholder")

    host.part_searchFieldSpec.inputControl.text = "x"
    host.expect(!host.part_searchFieldSpec.placeholderRotating,
      "typing a character stops the placeholder rotation at once")
    host.part_searchFieldSpec.surfaceVisible = false
    host.part_searchFieldNav.queryText = ""

    // A4 - live prefix hint.
    host.part_searchFieldSpec.inputControl.text = "sv"
    host.expect(host.part_searchFieldSpec.activeHintKind === "prefix"
        && host.part_searchFieldSpec.activeHintText.indexOf("svc:") >= 0
        && host.part_searchFieldSpec.activeHintText.indexOf("Services") >= 0,
      "a partial filter token shows the completion hint for the matching filter")

    host.part_searchFieldSpec.inputControl.text = "zz"
    host.expect(host.part_searchFieldSpec.activeHintKind === ""
        && host.part_searchFieldSpec.prefixHint === null,
      "an unmatched leading token shows no prefix hint")

    host.expect(host.part_searchFieldSpec.prefixHintFor("svc:") === null
        && host.part_searchFieldSpec.prefixHintFor("svc report") === null
        && host.part_searchFieldSpec.prefixHintFor("100") === null,
      "a completed colon token, a spaced query, or a number is not a filter prefix")

    // D11 - calculator nudge.
    host.part_searchFieldSpec.inputControl.text = "12 * 3"
    host.expect(host.part_searchFieldSpec.activeHintKind === "calculator"
        && host.part_searchFieldSpec.activeHintText.indexOf("=") >= 0
        && host.part_searchFieldSpec.activeHintText.indexOf("calculator") >= 0,
      "an arithmetic query nudges toward the calculator")

    host.part_searchFieldSpec.inputControl.text = "100 km"
    host.expect(host.part_searchFieldSpec.activeHintKind === "calculator",
      "a leading number and a unit word nudges toward the calculator")

    host.part_searchFieldSpec.inputControl.text = "=12*3"
    host.expect(host.part_searchFieldSpec.activeHintKind === "",
      "a query already starting with = shows no calculator nudge")

    host.part_searchFieldNav.activeFilters = [{ id: "files", excluded: false }]
    host.part_searchFieldSpec.inputControl.text = "12+3"
    host.expect(host.part_searchFieldSpec.activeHintKind === "",
      "an active filter prefix suppresses the calculator nudge")
    host.part_searchFieldNav.activeFilters = []

    // Precedence - the prefix hint wins over the calculator nudge.
    host.part_searchFieldSpec.inputControl.text = "2d"
    host.expect(host.part_searchFieldSpec.looksMathy("2d")
        && host.part_searchFieldSpec.activeHintKind === "prefix"
        && host.part_searchFieldSpec.activeHintText.indexOf("2d:") >= 0,
      "the prefix hint takes precedence over the calculator nudge when both apply")

    host.part_searchFieldSpec.inputControl.text = ""
  }
}
