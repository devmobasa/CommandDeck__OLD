import QtQuick

// Builds a launcher surface the first time it is wanted and keeps it, so a
// window carries none of the surfaces the user has not opened while a reopen
// stays as instant as before and keeps the surface's page and scroll state.
//
// The surface binds its open state through `presented`, which turns true only
// once the item exists. The surface then sees an ordinary closed-to-open
// change and runs its own focus-on-open, on the first open and every reopen.
Loader {
  id: root

  // Whether this window should show the surface now.
  required property bool wanted
  property bool presented: false

  active: false
  onWantedChanged: root.build()
  Component.onCompleted: root.build()
  onLoaded: root.presented = true

  function build() {
    if (root.wanted) root.active = true
  }
}
