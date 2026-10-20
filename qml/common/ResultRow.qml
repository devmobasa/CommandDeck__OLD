pragma ComponentBehavior: Bound
import QtQuick
import "MatchHighlight.js" as MatchHighlight
import "Protocol.js" as Protocol
import QtQuick.Layouts

Rectangle {
  id: root

  readonly property string serviceResultKind: "service"
  readonly property var cueFamilies: Object.freeze({
    Calculator: "calc", Service: "service", Application: "app", File: "file",
    Configuration: "config", Window: "window", Command: "command", Ai: "ai",
    Link: "link", System: "system", Search: "search", Herdr: "herdr", Control: "control",
    Process: "process"
  })

  NumberAnimation on opacity {
    from: 0
    to: 1
    duration: root.theme.reduceMotion ? 0 : root.theme.durationFast
  }

  // A refreshed list rewrites a row only when a field this row or its icon
  // reads changes; ResultModelController.sameShownResult lists those fields.
  // Add any newly shown field there too.
  required property var result
  required property var theme
  required property bool selected
  property int ordinal: 0
  // The compact layout: every row on one line, and the Ctrl+number badge only
  // while Ctrl is held.
  property bool compactLayout: false
  property bool controlHeld: false
  // B5 - the launcher's computed action count for this result. It is only wired
  // for the highlighted row (0 elsewhere), so the affordance is naturally
  // absent from unselected rows.
  property int actionCount: 0
  signal activated()
  signal actionsRequested()
  // The pointer's window position on every hover update over the row.
  signal pointerMoved(real x, real y)

  readonly property string titleText: root.result && root.result.title
    ? root.result.title
    : ""
  // A subtitle that repeats the title (a file at the top of its index root)
  // carries nothing new, so the row shows the title alone.
  readonly property string subtitleText: root.result && root.result.subtitle
    && String(root.result.subtitle) !== root.titleText ? String(root.result.subtitle) : ""

  readonly property string sourceId: root.result && root.result.sourceId
    ? String(root.result.sourceId)
    : ""
  readonly property string kind: root.result && root.result.kind
    ? String(root.result.kind)
    : ""

  readonly property bool compact: root.compactLayout
    || root.kind === Protocol.ResultKinds.Application || root.kind === Protocol.ResultKinds.CustomCommand
  readonly property string herdrStatus: sourceId === Protocol.SourceIds.Herdr && result.metadata
    ? String(result.metadata.herdrAgentStatus || "") : ""
  readonly property string statusBadgeText: herdrStatus === "blocked" ? "Needs attention"
    : herdrStatus === "working" ? "Working" : herdrStatus === "idle" ? "Idle" : ""
  readonly property var aliases: root.result && root.result.aliases ? root.result.aliases : []
  readonly property string primaryAlias: aliases.length > 0 ? String(aliases[0]) : ""
  readonly property bool showAlias: primaryAlias.length > 0 && root.width >= Math.round(330 * root.theme.scale)
  readonly property int rowIconSize: compact ? root.theme.compactIconSize : root.theme.iconSize
  // Space between a compact row's title and its right-aligned description.
  readonly property int compactLabelGap: Math.round(16 * root.theme.scale)
  readonly property alias titleControl: titleLabel
  readonly property alias subtitleControl: subtitleLabel
  readonly property alias aliasControl: aliasBadge

  // B7 - a quiet, consistent per-kind cue. `sourceId` is the most specific
  // signal (a service and a calculator both arrive from dedicated sources), so
  // it wins; otherwise the freedesktop-style `kind` decides. Everything folds
  // into a small family so the label and the tint stay in lock-step.
  readonly property string cueFamily: root.familyFor(root.sourceId, root.kind)
  readonly property string kindCue: root.cueLabel(root.cueFamily)
  readonly property bool hasKindCue: root.kindCue.length > 0
  // A calculator row leads its value with `=` in the accent colour so the answer
  // reads as an answer, not an echo of what was typed.
  readonly property bool isCalc: root.cueFamily === root.cueFamilies.Calculator

  function familyFor(sourceId, kind) {
    switch (sourceId) {
      case Protocol.SourceIds.Calculator: return root.cueFamilies.Calculator
      case Protocol.SourceIds.SystemdUnits: return root.cueFamilies.Service
      case Protocol.SourceIds.Ai: return root.cueFamilies.Ai
      case Protocol.SourceIds.Herdr: return root.cueFamilies.Herdr
      case Protocol.SourceIds.DesktopControls: return root.cueFamilies.Control
      case Protocol.SourceIds.Processes: return root.cueFamilies.Process
    }
    switch (kind) {
      case Protocol.ResultKinds.Application: return root.cueFamilies.Application
      case Protocol.ResultKinds.File: return root.cueFamilies.File
      case Protocol.ResultKinds.Configuration: return root.cueFamilies.Configuration
      case Protocol.ResultKinds.Window: return root.cueFamilies.Window
      case Protocol.ResultKinds.Workspace: return root.cueFamilies.Window
      case Protocol.ResultKinds.Tab: return root.cueFamilies.Herdr
      case root.serviceResultKind: return root.cueFamilies.Service
      case Protocol.ResultKinds.Calculator: return root.cueFamilies.Calculator
      case Protocol.ResultKinds.AiCommand: return root.cueFamilies.Ai
      case Protocol.ResultKinds.CustomCommand: return root.cueFamilies.Command
      case Protocol.ResultKinds.Quicklink: return root.cueFamilies.Link
      case Protocol.ResultKinds.Omarchy: return root.cueFamilies.System
      case Protocol.ResultKinds.SavedSearch: return root.cueFamilies.Search
      default: return ""
    }
  }

  function cueLabel(family) {
    switch (family) {
      case root.cueFamilies.Application: return "App"
      case root.cueFamilies.File: return "File"
      case root.cueFamilies.Configuration: return "Config"
      case root.cueFamilies.Window: return "Window"
      case root.cueFamilies.Command: return "Command"
      case root.cueFamilies.Service: return "Service"
      case root.cueFamilies.Calculator: return "Calc"
      case root.cueFamilies.Ai: return "AI"
      case root.cueFamilies.Link: return "Link"
      case root.cueFamilies.System: return "System"
      case root.cueFamilies.Search: return "Search"
      case root.cueFamilies.Herdr: return "Herdr"
      case root.cueFamilies.Control: return "Control"
      case root.cueFamilies.Process: return "Process"
      default: return ""
    }
  }

  // Theme-derived, deliberately faint. Launch/focus families lean on the accent
  // (mirroring ResultIcon's tile tint) while content families (file, config)
  // stay neutral, so the palette never turns loud.
  function cueTint(family) {
    switch (family) {
      case root.cueFamilies.Calculator:
      case root.cueFamilies.Ai:
        return root.theme.withAlpha(root.theme.accent, 0.20)
      case root.cueFamilies.Application:
      case root.cueFamilies.Command:
      case root.cueFamilies.Link:
      case root.cueFamilies.System:
        return root.theme.withAlpha(root.theme.accent, 0.14)
      case root.cueFamilies.Window:
      case root.cueFamilies.Service:
      case root.cueFamilies.Herdr:
        return root.theme.withAlpha(root.theme.accent, 0.10)
      default:
        return root.theme.withAlpha(root.theme.text, 0.07)
    }
  }

  // B5 - the highlighted row advertises that more actions exist. Hidden on
  // unselected rows and whenever the result carries no secondary actions
  // (count <= 1). The key shown is the real one: Right (or Ctrl+Enter) opens
  // the action panel; Tab is completion, so it is deliberately not claimed here.
  readonly property bool showActionHint: !root.compact && root.selected && root.actionCount > 1
  readonly property string actionHintText: root.actionCount > 1
    ? root.actionCount + " actions"
    : ""
  // Omarchy menu state from the daemon: a ✓ for a checked row, and a dimmed
  // row whose guard failed. Both are presentation only; actions stay as they are.
  readonly property bool omarchyChecked: !!root.result && !!root.result.metadata
    && root.result.metadata[Protocol.ResultMetadataKeys.OmarchyChecked] === "true"
  readonly property bool omarchyUnavailable: !!root.result && !!root.result.metadata
    && root.result.metadata[Protocol.ResultMetadataKeys.OmarchyUnavailable] === "true"
  readonly property string accessibilityName:
    (root.ordinal > 0 ? root.ordinal + ", " : "")
      + root.titleText
      + (root.omarchyChecked ? ", checked" : "")
      + (root.omarchyUnavailable ? ", unavailable here" : "")
      + (root.statusBadgeText ? ", " + root.statusBadgeText : "")
      + (root.subtitleText ? ", " + root.subtitleText : "")
      + (root.hasKindCue ? ", " + root.kindCue : "")
      + (root.result && root.result.section && root.result.section.title
        ? ", " + root.result.section.title
        : "")
      + (root.result && root.result.isFavorite ? ", bookmarked" : "")
      + (root.aliases.length > 0 ? ", aliases " + root.aliases.join(", ") : "")

  implicitHeight: Math.max(
    root.compact ? root.theme.compactRowHeight : root.theme.rowHeight,
    labels.implicitHeight + Math.round(12 * root.theme.scale),
    root.rowIconSize + Math.round(10 * root.theme.scale))
  radius: root.theme.radiusSmall
  color: root.selected ? root.theme.selectedBackground : "transparent"
  border.width: root.selected ? 1 : 0
  border.color: root.theme.selectedBorder
  Accessible.role: Accessible.ListItem
  Accessible.name: root.accessibilityName
  Accessible.selected: root.selected

  RowLayout {
    id: content
    anchors.fill: parent
    anchors.leftMargin: root.theme.rowPaddingX
    anchors.rightMargin: root.theme.rowPaddingX
    spacing: root.theme.iconGap
    opacity: root.omarchyUnavailable && !root.selected ? 0.5 : 1

    ResultIcon {
      theme: root.theme
      result: root.result
      size: root.rowIconSize
    }

    Item {
      id: labels
      Layout.fillWidth: true
      Layout.minimumWidth: 0
      implicitHeight: root.compact
        ? Math.max(titleLabel.implicitHeight, subtitleLabel.visible ? subtitleLabel.implicitHeight : 0)
        : titleLabel.implicitHeight + (subtitleLabel.visible ? subtitleLabel.implicitHeight + 2 : 0)

      TextMetrics {
        id: subtitleMetrics
        text: root.subtitleText
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
      }

      Text {
        id: titleLabel
        width: root.compact && subtitleLabel.visible
          ? Math.max(0, labels.width - subtitleLabel.width - root.compactLabelGap)
          : labels.width
        y: root.compact ? (labels.height - height) / 2 : 0
        // The daemon computes matched ranges against the title on every search.
        text: root.isCalc
          ? "= " + MatchHighlight.escapeMarkup(root.titleText)
          : MatchHighlight.highlighted(root.titleText, root.result ? root.result.matchedRanges : null, root.theme.accent)
        textFormat: Text.StyledText
        color: root.isCalc && !root.selected
          ? root.theme.accent
          : (root.selected ? root.theme.selectedText : root.theme.text)
        elide: Text.ElideRight
        font.family: root.isCalc ? "monospace" : root.theme.fontFamily
        font.pixelSize: root.theme.fontTitle
        font.weight: (root.selected || root.isCalc) ? Font.DemiBold : Font.Normal
      }

      // A compact row's description takes the room its title does not need,
      // keeping at least 35% when both are long. It is prose, so it elides at
      // the end; path subtitles on the other rows keep their middle elision.
      Text {
        id: subtitleLabel
        width: root.compact
          ? Math.min(subtitleMetrics.advanceWidth, Math.max(labels.width * 0.35,
            labels.width - titleLabel.implicitWidth - root.compactLabelGap))
          : labels.width
        x: root.compact ? labels.width - width : 0
        y: root.compact ? (labels.height - height) / 2 : titleLabel.height + 2
        visible: text.length > 0 && (!root.compact || labels.width >= 180 * root.theme.scale)
        textFormat: Text.PlainText
        text: root.subtitleText
        color: root.theme.textMuted
        elide: root.compact ? Text.ElideRight : Text.ElideMiddle
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
      }
    }

    Rectangle {
      id: aliasBadge
      visible: root.showAlias
      Layout.alignment: Qt.AlignVCenter
      implicitWidth: Math.min(aliasLabel.implicitWidth + Math.round(12 * root.theme.scale),
        Math.round(80 * root.theme.scale))
      implicitHeight: Math.round(root.theme.fontSmall * 1.8)
      radius: Math.max(2, root.theme.radiusSmall - 2)
      color: root.theme.surfaceStrong
      border.width: 1
      border.color: root.theme.separator
      Text {
        id: aliasLabel
        anchors.fill: parent
        anchors.leftMargin: Math.round(6 * root.theme.scale)
        anchors.rightMargin: anchors.leftMargin
        verticalAlignment: Text.AlignVCenter
        text: root.primaryAlias
        textFormat: Text.PlainText
        elide: Text.ElideRight
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
      }
    }

    Rectangle {
      visible: root.statusBadgeText.length > 0
      Layout.alignment: Qt.AlignVCenter
      implicitHeight: Math.round(root.theme.fontCaption * 1.8)
      implicitWidth: statusText.implicitWidth + 12
      radius: root.theme.radiusSmall
      color: root.theme.withAlpha(root.herdrStatus === "blocked" ? root.theme.urgent : root.theme.accent, 0.13)
      Text {
        id: statusText
        anchors.centerIn: parent
        textFormat: Text.PlainText
        text: root.statusBadgeText
        color: root.herdrStatus === "blocked" ? root.theme.urgent : root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontCaption
      }
    }

    // B5 - inline "N actions" affordance on the highlighted row, right where
    // the eye already is. The Right keycap matches the footer legend.
    Row {
      visible: root.showActionHint
      spacing: Math.max(3, Math.round(4 * root.theme.scale))
      Layout.alignment: Qt.AlignVCenter

      KeyCap {
        anchors.verticalCenter: parent.verticalCenter
        theme: root.theme
        label: "→"
      }

      Text {
        anchors.verticalCenter: parent.verticalCenter
        text: root.actionHintText
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontCaption
      }
    }

    // B7 - a faint per-kind tag so each row's kind is unmistakable at a glance.
    Rectangle {
      visible: root.hasKindCue
      Layout.alignment: Qt.AlignVCenter
      implicitHeight: Math.round(root.theme.fontCaption * 1.7)
      implicitWidth: kindCueText.implicitWidth + Math.round(10 * root.theme.scale)
      radius: Math.max(2, root.theme.radiusSmall - 2)
      color: root.cueTint(root.cueFamily)

      Text {
        id: kindCueText
        anchors.centerIn: parent
        textFormat: Text.PlainText
        text: root.kindCue
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontCaption
      }
    }

    // Rows one through nine advertise their Ctrl shortcut.
    KeyCap {
      id: ordinalBadge
      visible: root.ordinal > 0 && (!root.compactLayout || root.controlHeld)
      theme: root.theme
      label: "Ctrl+" + root.ordinal
    }

    Text {
      id: checkedMark
      visible: root.omarchyChecked
      text: "✓"
      textFormat: Text.PlainText
      color: root.theme.accent
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontBody
      font.weight: Font.DemiBold
    }

    Text {
      visible: !!root.result && root.result.isFavorite === true
      text: "★"
      color: root.theme.accent
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontBody
    }
  }

  readonly property alias toolTipControl: rowToolTip
  readonly property alias ordinalBadgeControl: ordinalBadge
  readonly property alias checkedMarkControl: checkedMark
  readonly property real contentOpacity: content.opacity
  ThemedToolTip {
    id: rowToolTip
    theme: root.theme
    visible: rowMouse.containsMouse && (titleLabel.truncated || subtitleLabel.truncated
      || (root.compact && !subtitleLabel.visible) || root.primaryAlias.length > 0)
    text: root.titleText
      + (root.subtitleText ? "\n" + root.subtitleText : "")
      + (root.aliases.length > 0 ? "\nAliases: " + root.aliases.join(", ") : "")
  }

  MouseArea {
    id: rowMouse
    anchors.fill: parent
    hoverEnabled: true
    onClicked: root.activated()
    onPressAndHold: root.actionsRequested()
    onPositionChanged: mouse => {
      const point = rowMouse.mapToItem(null, mouse.x, mouse.y)
      root.pointerMoved(point.x, point.y)
    }
  }
}
