import QtQuick

Rectangle {
  id: root
  required property var theme
  property string imageSource: ""
  property int imageWidth: 0
  property int imageHeight: 0
  readonly property bool validSource: imageSource.startsWith("data:image/png;base64,")
    && imageSource.length <= 1536 * 1024
  readonly property alias imageControl: thumbnail
  implicitHeight: Math.round(Math.min(320 * theme.scale,
    Math.max(80 * theme.scale, width * imageHeight / Math.max(1, imageWidth))))
  radius: theme.radiusSmall
  color: theme.surfaceStrong
  clip: true

  Image {
    id: thumbnail
    objectName: "filePreviewImage"
    anchors.fill: parent
    anchors.margins: 8 * root.theme.scale
    source: root.validSource ? root.imageSource : ""
    sourceSize: Qt.size(512, 512)
    asynchronous: true
    cache: false
    fillMode: Image.PreserveAspectFit
    smooth: true
    Accessible.role: Accessible.Graphic
    Accessible.name: "Image preview, " + root.imageWidth + " by " + root.imageHeight + " pixels"
  }

  Text {
    objectName: "imagePreviewError"
    anchors.fill: parent
    anchors.margins: 12 * root.theme.scale
    visible: root.imageSource.length > 0 && (!root.validSource || thumbnail.status === Image.Error)
    text: "This image preview could not be displayed."
    color: root.theme.textFaint
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
    wrapMode: Text.Wrap
    verticalAlignment: Text.AlignVCenter
    horizontalAlignment: Text.AlignHCenter
  }
}
