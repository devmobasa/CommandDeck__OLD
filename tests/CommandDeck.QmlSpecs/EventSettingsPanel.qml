import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

CommandDeck.SettingsPanel {
  required property var host

  id: settingsPanel
  anchors.fill: parent
  visible: false
  theme: host.part_theme
  iconResolver: function(iconName) { return "" }
  sourceDescriptors: [
    { id: "applications", label: "Applications", icon: "applications-other" },
    { id: "files", label: "Files", icon: "folder" }
  ]
  filterDescriptors: [
    { id: "applications", label: "Applications", token: "app:", aliases: [] },
    { id: "files", label: "Files", token: "file:", aliases: ["text:"] }
  ]
  snapshot: ({
    preferences: {
      sources: [
        { sourceId: "applications", enabledInSearch: true, enabledOnHome: true, position: 1 },
        { sourceId: "files", enabledInSearch: true, enabledOnHome: true, position: 2 }
      ],
      reduceMotion: false,
      webFallbackQuicklinkId: null
    },
    shortcuts: [],
    customizations: []
  })
  property int dismissals: 0
  property var changedPreferences: null
  property bool queueChanges: false
  property string filterAliasSaved: ""
  property string filterAliasTargetSaved: ""
  property string filterAliasRemoved: ""
  property int hiddenItemsRequests: 0
  property string hiddenItemsQueryRequested: ""
  property int hiddenItemsOffsetRequested: -1
  property string hiddenItemRestored: ""
  property var savedSearchSaved: null
  property var savedSearchRan: null
  property var savedSearchDeleted: null
  property string aiModelsRequestedFor: ""
  property string aiSelectionProviderRequested: ""
  property string aiSelectionModelRequested: ""
  property var aiSaveRequested: null
  property string aiDeleteRequestedId: ""
  property int aiDeleteRevision: -1
  property var aiTestRequested: null
  property int aiEnableRequests: 0
  property bool aiEnableRequestedValue: false
  property int aiEnableRevision: -1
  property int aiDraftInvalidations: 0
  property int aiReloads: 0
  property int aiTestCancels: 0
  property var agentPreferencesEmitted: null
  property int agentTrustChallengeLevel: -1
  property var agentTrustUpdate: null
  property string agentModelsBackendRequested: ""
  property string agentGrantRemoved: ""
  property int agentGrantsCleared: 0
  property int agentLogouts: 0
  property int agentLogins: 0
  currentExpression: ({
    includeFilterIds: ["files"],
    excludeFilterIds: [],
    clauses: [{ field: "title", text: "roadmap", excluded: false }]
  })
  onDismissed: {
    dismissals++
    visible = false
  }
  onAgentLoginRequested: agentLogins++
  onPreferencesChanged: preferences => {
    changedPreferences = preferences
    if (queueChanges) host.part_settingsPersonalization.submitPreferences(preferences)
  }
  onFilterAliasSetRequested: (alias, filterId) => {
    filterAliasSaved = alias
    filterAliasTargetSaved = filterId
  }
  onFilterAliasRemoveRequested: alias => filterAliasRemoved = alias
  onHiddenItemsRequested: (query, offset) => {
    hiddenItemsRequests++
    hiddenItemsQueryRequested = query
    hiddenItemsOffsetRequested = offset
  }
  onHiddenItemRestoreRequested: customizationId => hiddenItemRestored = customizationId
  onSavedSearchSaveRequested: search => savedSearchSaved = search
  onSavedSearchRunRequested: search => savedSearchRan = search
  onSavedSearchDeleteRequested: search => savedSearchDeleted = search
  onAiModelsRefreshRequested: providerId => aiModelsRequestedFor = providerId
  onAiSelectionUpdateRequested: (providerId, modelId) => {
    aiSelectionProviderRequested = providerId
    aiSelectionModelRequested = modelId
  }
  onAiProviderSaveRequested: (provider, create, revision, selectAfterSave) => aiSaveRequested = ({
    provider: provider, create: create, revision: revision, selectAfterSave: selectAfterSave
  })
  onAiProviderDeleteRequested: (providerId, revision) => {
    aiDeleteRequestedId = providerId
    aiDeleteRevision = revision
  }
  onAiProviderTestRequested: provider => aiTestRequested = provider
  onAiProviderTestCancelRequested: aiTestCancels++
  onAiEnableRequested: (enabled, revision) => {
    aiEnableRequests++
    aiEnableRequestedValue = enabled
    aiEnableRevision = revision
  }
  onAiDraftInvalidated: aiDraftInvalidations++
  onAiSettingsReloadRequested: aiReloads++
  onAgentPreferencesChanged: preferences => agentPreferencesEmitted = preferences
  onAgentTrustChallengeRequested: level => agentTrustChallengeLevel = level
  onAgentTrustUpdateRequested: (challengeId, level, phrase, duration) =>
    agentTrustUpdate = ({
      challengeId: challengeId,
      level: level,
      phrase: phrase,
      duration: duration
    })
  onAgentModelsRefreshRequested: backendId => agentModelsBackendRequested = backendId
  onAgentGrantRemoveRequested: grantId => agentGrantRemoved = grantId
  onAgentGrantsClearRequested: agentGrantsCleared++
  onAgentLogoutRequested: agentLogouts++
}
