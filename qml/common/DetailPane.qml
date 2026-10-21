pragma ComponentBehavior: Bound
import QtQuick
import "Protocol.js" as Protocol
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
  id: root

  required property var theme
  property var result: null
  property bool opened: false
  property bool selected: false
  property bool workspacePreviewEligible: false
  property var workspacePreviewBackend: null
  property string workspacePreviewMode: "layout"
  property var workspaceSnapshotLifecycle: null
  property DesktopIntegration windowPreviewIntegration: null
  property int workspaceIssuedRequestId: 0
  property int workspaceResultRequestId: 0
  property var filePreview: null
  property bool previewLoading: false
  property string previewError: ""
  signal copyRequested()
  signal previewMatchRequested(int index)
  signal previewOpenRequested()
  property Item typingTarget: null
  property Item previousTabStop: null
  property Item nextTabStop: null
  readonly property Item firstPreviewControl: filePreviewView.firstControl
  readonly property Item lastPreviewControl: filePreviewView.lastControl

  readonly property var details: root.result && root.result.details
    ? root.result.details
    : []
  readonly property string body: root.result && root.result.detailMarkdown
    ? root.result.detailMarkdown
    : ""
  readonly property int renderedDetailCount: detailRepeater.count
  readonly property bool hasDetails: root.details.length > 0
  readonly property bool hasBody: root.body.length > 0
  readonly property var rankingMatches: root.result && root.result.ranking
    && root.result.ranking.matches
      ? root.result.ranking.matches.filter(match => match.field !== Protocol.RankingFieldNames.Title)
      : []
  readonly property bool hasRankingMatches: root.rankingMatches.length > 0
  readonly property int renderedMatchCount: matchedFields.renderedCount
  readonly property bool hasWindowPreview: windowPreview.hasPreview
  readonly property bool isFileResult: root.result && root.result.kind === Protocol.ResultKinds.File
  readonly property bool isCalculator: root.result && root.result.kind === Protocol.ResultKinds.Calculator
  readonly property string calcValue: root.result && root.result.title ? root.result.title : ""
  readonly property string calcInput: root.result && root.result.subtitle ? root.result.subtitle : ""
  readonly property string calcActionTitle: root.result && root.result.actions && root.result.actions.length > 0
    ? String(root.result.actions[0].title) : ""
  readonly property bool calcAnswerVisible: calculatorView.visible
  readonly property bool detailsSectionVisible: detailsColumn.visible
  readonly property bool matchedListVisible: matchedFields.visible
  readonly property bool hasFilePreview: filePreviewView.hasContent
  readonly property bool hasContent: root.isCalculator || root.hasDetails || root.hasBody || root.hasRankingMatches
    || root.hasWindowPreview || root.hasFilePreview || workspacePreview.hasContent

  function detailItemAt(index) {
    return detailRepeater.itemAt(index)
  }

  function renderedValueAt(index) {
    const row = root.detailItemAt(index)
    return row ? row.renderedValue : ""
  }

  function renderedValueWrapModeAt(index) {
    const row = root.detailItemAt(index)
    return row ? row.renderedValueWrapMode : Text.NoWrap
  }

  function renderedValueHeightAt(index) {
    const row = root.detailItemAt(index)
    return row ? row.renderedValueHeight : 0
  }

  function renderedValueWidthAt(index) {
    const row = root.detailItemAt(index)
    return row ? row.renderedValueWidth : 0
  }

  function renderedMatchMarkupAt(index) {
    return matchedFields.renderedMarkupAt(index)
  }

  visible: root.hasContent
  color: root.theme.surface
  radius: root.theme.radiusSmall

  Flickable {
    anchors.fill: parent
    anchors.margins: Math.round(16 * root.theme.scale)
    contentWidth: width
    contentHeight: content.implicitHeight
    clip: true
    boundsBehavior: Flickable.StopAtBounds

    ScrollBar.vertical: ScrollBar { }

    ColumnLayout {
      id: content

      width: parent.width
      spacing: Math.round(16 * root.theme.scale)

      CalculatorResultView {
        id: calculatorView
        Layout.fillWidth: true
        visible: root.isCalculator
        exactValue: {
          const field = root.result && root.result.details
            ? root.result.details.find(value => value.label === "Exact value") : null
          return field ? field.value : ""
        }
        theme: root.theme
        expression: root.calcInput
        value: root.calcValue
        actionTitle: root.calcActionTitle
        onCopyRequested: root.copyRequested()
      }

      RowLayout {
        visible: !root.isCalculator
        Layout.fillWidth: true
        spacing: Math.round(12 * root.theme.scale)

        ResultIcon {
          theme: root.theme
          result: root.result || ({})
          size: Math.round(54 * root.theme.scale)
        }

        ColumnLayout {
          Layout.fillWidth: true
          spacing: Math.round(3 * root.theme.scale)

          Text {
            Layout.fillWidth: true
            visible: !root.isCalculator
            textFormat: Text.PlainText
            text: root.result && root.result.title ? root.result.title : ""
            color: root.theme.text
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontHeading
            font.weight: Font.DemiBold
            elide: Text.ElideRight
          }

          Text {
            Layout.fillWidth: true
            visible: !root.isCalculator && text.length > 0
            textFormat: Text.PlainText
            text: root.result && root.result.subtitle ? root.result.subtitle : ""
            color: root.theme.textMuted
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontBody
            elide: Text.ElideRight
          }
        }
      }

      WindowPreview {
        id: windowPreview

        Layout.fillWidth: true
        theme: root.theme
        result: root.result
        opened: root.opened
        selected: root.selected
        integration: root.windowPreviewIntegration
      }

      WorkspacePreview {
        id: workspacePreview
        Layout.fillWidth: true
        theme: root.theme
        result: root.result
        eligible: root.workspacePreviewEligible
        mode: root.workspacePreviewMode
        snapshotLifecycle: root.workspaceSnapshotLifecycle
        issuedRequestId: root.workspaceIssuedRequestId
        resultRequestId: root.workspaceResultRequestId
        backend: root.workspacePreviewBackend
      }

      FilePreview {
        id: filePreviewView

        Layout.fillWidth: true
        theme: root.theme
        preview: root.isFileResult ? root.filePreview : null
        loading: root.isFileResult && root.previewLoading
        error: root.isFileResult ? root.previewError : ""
        typingTarget: root.typingTarget
        previousTabStop: root.previousTabStop
        nextTabStop: root.nextTabStop
        onMatchRequested: index => root.previewMatchRequested(index)
        onOpenRequested: root.previewOpenRequested()
      }

      Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 1
        visible: root.hasWindowPreview || root.hasFilePreview
          || (root.hasDetails && !root.isCalculator) || root.hasBody
        color: root.theme.separator
      }

      Column {
        id: detailsColumn
        Layout.fillWidth: true
        spacing: Math.round(9 * root.theme.scale)
        visible: root.hasDetails && !root.isCalculator

        Repeater {
          id: detailRepeater
          model: root.details

          delegate: RowLayout {
            id: detailRow

            required property var modelData

            readonly property string renderedValue: valueText.text
            readonly property int renderedValueWrapMode: valueText.wrapMode
            readonly property real renderedValueHeight: valueText.implicitHeight
            readonly property real renderedValueWidth: valueText.width

            width: content.width
            spacing: Math.round(12 * root.theme.scale)

            Text {
              Layout.preferredWidth: Math.round(92 * root.theme.scale)
              Layout.alignment: Qt.AlignTop
              textFormat: Text.PlainText
              text: detailRow.modelData.label || ""
              color: root.theme.textFaint
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontSmall
              wrapMode: Text.Wrap
            }

            Text {
              id: valueText

              Layout.fillWidth: true
              Layout.alignment: Qt.AlignTop
              text: detailRow.modelData.value || ""
              color: root.theme.textMuted
              font.family: detailRow.modelData.monospace
                ? "monospace"
                : root.theme.fontFamily
              font.pixelSize: root.theme.fontBody
              wrapMode: Text.WrapAnywhere
              textFormat: Text.PlainText
            }
          }
        }
      }

      MatchedFieldList {
        id: matchedFields
        Layout.fillWidth: true
        visible: root.hasRankingMatches && !root.isCalculator
        theme: root.theme
        matches: root.rankingMatches
      }

      Text {
        Layout.fillWidth: true
        visible: root.hasBody
        text: root.body
        textFormat: root.result && [Protocol.SourceIds.Clipboard, Protocol.SourceIds.Snippets, Protocol.SourceIds.Scripts, Protocol.SourceIds.Projects].indexOf(root.result.sourceId) >= 0
          ? Text.PlainText : Text.MarkdownText
        wrapMode: Text.Wrap
        color: root.theme.textMuted
        linkColor: root.theme.accent
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
        onLinkActivated: link => Qt.openUrlExternally(link)
      }
    }
  }
}
