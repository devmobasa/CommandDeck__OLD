pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts

StackLayout {
  id: pages
  required property var host
  currentIndex: host.pageIndex(host.currentPage)
  readonly property alias searchPage: searchScroll
  readonly property alias aiHub: aiHub
  readonly property alias assistantPage: aiHub.assistantPage
  readonly property alias editorsPage: editorsScroll
  readonly property alias filtersPage: filtersScroll
  readonly property alias aiPage: aiHub.aiPage
  readonly property alias shortcutsPage: shortcutsScroll
  readonly property alias customizationPage: customizationScroll

  function resetScroll() {
    for (let index = 0; index < children.length; index++) {
      const child = children[index]
      if (child.contentItem) child.contentItem.contentY = 0
    }
    aiHub.resetScroll()
  }

  SettingsSearchPage {
    id: searchScroll
    host: pages.host
  }

  SettingsAiHub {
    id: aiHub
    host: pages.host
  }

  SettingsEditorsPage {
    id: editorsScroll
    host: pages.host
  }

  SettingsFiltersPage {
    id: filtersScroll
    host: pages.host
  }

  SettingsShortcutsPage {
    id: shortcutsScroll
    host: pages.host
  }

  SettingsCustomizationPage {
    id: customizationScroll
    host: pages.host
  }
}
