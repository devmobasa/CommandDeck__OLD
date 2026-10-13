pragma ComponentBehavior: Bound
import QtQuick
import "FocusTrap.js" as FocusTrap
import "Protocol.js" as Protocol
import QtQuick.Controls
import QtQuick.Layouts

FocusScope {
  id: root

  required property var theme
  property bool opened: false
  property var inspection: null
  property string healthDetails: ""
  property string healthSummary: ""
  readonly property string heading: "Index inspector" + (healthSummary.length > 0 ? " · " + healthSummary : "")
  property int page: 0
  property int pageSize: healthDetails.length > 0 ? 4 : 6
  readonly property var workItems: inspection ? inspection.workItems || [] : []
  readonly property int pageCount: Math.max(1, Math.ceil(workItems.length / pageSize))
  readonly property int pageStart: page * pageSize
  readonly property int visibleWorkItemCount: Math.max(
    0, Math.min(pageSize, workItems.length - pageStart))
  readonly property bool countsExact: !inspection || inspection.workItemCountsExact !== false
  readonly property string countPrefix: countsExact ? "" : "at least "
  readonly property string pageSummary: "Page " + (page + 1) + " of " + pageCount
    + (inspection && inspection.workItemsTruncated ? " loaded" : "")
  readonly property bool focusTrapConfigured: true
  readonly property alias closeControl: closeButton
  readonly property alias healthControl: healthText
  readonly property alias previousControl: previousButton
  readonly property alias nextControl: nextButton

  signal dismissed()

  function close() {
    root.dismissed()
  }

  function movePage(delta) {
    page = Math.max(0, Math.min(pageCount - 1, page + delta))
  }

  onOpenedChanged: {
    if (!opened) return
    page = 0
    Qt.callLater(closeButton.forceActiveFocus)
  }

  visible: opened
  enabled: opened
  Accessible.role: Accessible.Dialog
  Accessible.name: "Index inspector"
  Accessible.description: inspection
    ? (healthSummary.length > 0 ? healthSummary + ". Files: " : "Files: ")
      + countPrefix + inspection.failedItemCount + " failed, "
      + countPrefix + inspection.staleItemCount + " stale, "
      + countPrefix + inspection.pendingItemCount + " pending"
    : "Index state unavailable"

  Keys.onPressed: event => {
    if (event.key === Qt.Key_Escape) {
      root.close()
      event.accepted = true
    } else if (event.key === Qt.Key_PageDown) {
      root.movePage(1)
      event.accepted = true
    } else if (event.key === Qt.Key_PageUp) {
      root.movePage(-1)
      event.accepted = true
    } else if (event.key === Qt.Key_Tab || event.key === Qt.Key_Backtab) {
      // The details show only with health issues, and a page button is
      // disabled at its end; both are skipped then.
      FocusTrap.cycleTargets([closeButton, healthText, previousButton, nextButton],
        event.key === Qt.Key_Tab && !(event.modifiers & Qt.ShiftModifier))
      event.accepted = true
    }
  }

  Rectangle {
    anchors.fill: parent
    color: root.theme.scrim

    TapHandler {
      onTapped: root.close()
    }
  }

  Rectangle {
    width: Math.min(parent.width - 48, 620)
    height: Math.min(parent.height - 48, 520)
    anchors.centerIn: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.theme.separator

    TapHandler {
      onTapped: eventPoint => eventPoint.accepted = true
    }

    ColumnLayout {
      anchors.fill: parent
      anchors.margins: root.theme.panelPadding
      spacing: 10

      RowLayout {
        Layout.fillWidth: true

        Text {
          Layout.fillWidth: true
          textFormat: Text.PlainText
          text: root.heading
          color: root.theme.text
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontHeading
          font.bold: true
          wrapMode: Text.Wrap
        }

        DeckButton {
          id: closeButton
          theme: root.theme
          text: "Close"
          Accessible.name: "Close index inspector"
          onClicked: root.close()
        }
      }

      Text {
        Layout.fillWidth: true
        text: root.inspection
          ? "Files: Indexed " + root.inspection.stats.itemCount
            + " · Excluded " + root.inspection.totalExclusionDiagnosticCount
            + " · Failed " + root.countPrefix + root.inspection.failedItemCount
            + " · Stale " + root.countPrefix + root.inspection.staleItemCount
            + " · Pending " + root.countPrefix + root.inspection.pendingItemCount
          : "Index state unavailable"
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
        wrapMode: Text.Wrap
      }

      ScrollView {
        Layout.fillWidth: true
        Layout.preferredHeight: Math.min(100, healthText.implicitHeight + 12)
        visible: root.healthDetails.length > 0
        clip: true
        contentWidth: availableWidth
        TextArea {
          id: healthText
          text: root.healthDetails
          textFormat: TextEdit.PlainText
          readOnly: true
          wrapMode: TextEdit.Wrap
          color: root.theme.textMuted
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontSmall
          background: null
          Accessible.name: "Index and service issue details"
        }
      }

      Text {
        Layout.fillWidth: true
        visible: root.workItems.length === 0
        text: root.inspection && root.inspection.totalWorkItemCount > 0
          ? "No work items in this loaded page. More may remain."
          : "No failed, stale, or pending index items."
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
      }

      ColumnLayout {
        Layout.fillWidth: true
        Layout.fillHeight: true
        spacing: 6

        Repeater {
          model: root.visibleWorkItemCount

          Rectangle {
            id: workRow

            required property int index
            readonly property var work: root.workItems[root.pageStart + index]
            Layout.fillWidth: true
            Layout.preferredHeight: 54
            radius: root.theme.radiusSmall
            color: root.theme.surfaceStrong
            Accessible.role: Accessible.ListItem
            Accessible.name: workRow.work.state + ": " + workRow.work.relativePath
            Accessible.description: workRow.work.reason + "; root " + workRow.work.root

            Column {
              anchors.fill: parent
              anchors.margins: 7
              spacing: 2

              Text {
                width: parent.width
                textFormat: Text.PlainText
                text: workRow.work.state.toUpperCase()
                  + " · " + workRow.work.relativePath
                color: workRow.work.state === Protocol.IndexWorkStates.Failed
                  ? root.theme.urgent
                  : root.theme.text
                font.family: root.theme.fontFamily
                font.pixelSize: root.theme.fontBody
                elide: Text.ElideMiddle
              }

              Text {
                width: parent.width
                textFormat: Text.PlainText
                text: workRow.work.reason
                color: root.theme.textMuted
                font.family: root.theme.fontFamily
                font.pixelSize: root.theme.fontSmall
                elide: Text.ElideRight
              }
            }
          }
        }
      }

      RowLayout {
        Layout.fillWidth: true

        DeckButton {
          id: previousButton
          theme: root.theme
          text: "Previous"
          enabled: root.page > 0
          Accessible.name: "Previous index page"
          onClicked: root.movePage(-1)
        }

        Text {
          Layout.fillWidth: true
          horizontalAlignment: Text.AlignHCenter
          text: root.pageSummary
          color: root.theme.textMuted
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontSmall
        }

        DeckButton {
          id: nextButton
          theme: root.theme
          text: "Next"
          enabled: root.page + 1 < root.pageCount
          Accessible.name: "Next index page"
          onClicked: root.movePage(1)
        }
      }
    }
  }
}
