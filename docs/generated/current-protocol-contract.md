# Generated current protocol contract

Generated from Core protocol constants and registries. Run `scripts/generate-protocol-contract.sh` after an intentional contract change.

## Operations

- `agent.backends.get`
- `agent.clear`
- `agent.context.resolve`
- `agent.conversation.get`
- `agent.grants.add`
- `agent.grants.clear`
- `agent.grants.get`
- `agent.grants.remove`
- `agent.login.cancel`
- `agent.login.complete`
- `agent.login.start`
- `agent.logout`
- `agent.models.get`
- `agent.operations.get`
- `agent.output`
- `agent.preferences.get`
- `agent.preferences.update`
- `agent.prewarm`
- `agent.status.get`
- `agent.trust.challenge`
- `agent.trust.get`
- `agent.trust.update`
- `agent.turn.cancel`
- `agent.turn.input`
- `agent.turn.start`
- `ai.clear`
- `ai.models.get`
- `ai.output`
- `ai.preflight`
- `ai.providers.delete`
- `ai.providers.get`
- `ai.providers.save`
- `ai.providers.test`
- `ai.selection.get`
- `ai.selection.update`
- `ai.settings.get`
- `ai.settings.update`
- `ai.start`
- `ai.templates.delete`
- `ai.templates.get`
- `ai.templates.save`
- `alias.set`
- `bootstrap`
- `cancel`
- `catalog.inspect`
- `clipboard.action`
- `clipboard.actions.prepare`
- `clipboard.close`
- `clipboard.migration.acknowledge`
- `clipboard.open`
- `clipboard.preview`
- `clipboard.query`
- `clipboard.release.abort`
- `clipboard.settings.get`
- `clipboard.settings.set`
- `customizations.export`
- `customizations.reset`
- `database.integrity`
- `database.optimize`
- `database.purge`
- `database.repair`
- `extension.action`
- `extension.close`
- `extension.open`
- `extension.present`
- `extensions.manage`
- `favorite.move`
- `favorite.remove-orphan`
- `favorite.reorder`
- `favorite.set`
- `files.batch`
- `files.clear`
- `files.select`
- `filter-alias.set`
- `hidden.list`
- `hidden.restore`
- `hidden.set`
- `history.delete`
- `history.get`
- `history.reset`
- `index.add`
- `index.disable`
- `index.enable`
- `index.pause`
- `index.remove`
- `index.resume`
- `index.roots`
- `index.roots.import`
- `invoke`
- `launcher.open.cancel`
- `package.cancel`
- `package.commit`
- `package.preview`
- `package.search`
- `ping`
- `preferences.get`
- `preferences.update`
- `preview`
- `privacy.audit`
- `quicklinks.disable`
- `quicklinks.enable`
- `quicklinks.get`
- `quicklinks.remove`
- `quicklinks.save`
- `reindex`
- `saved-search.delete`
- `saved-search.get`
- `saved-search.save`
- `search`
- `status`
- `suggest`

## Message types

- `action-result`
- `agent-backends-result`
- `agent-conversation-result`
- `agent-event`
- `agent-grants-result`
- `agent-login-result`
- `agent-models-result`
- `agent-operations-result`
- `agent-preferences-result`
- `agent-status-result`
- `agent-trust-challenge-result`
- `agent-trust-result`
- `ai-chunk`
- `ai-done`
- `ai-models-result`
- `ai-preflight-result`
- `ai-provider-save-result`
- `ai-provider-test-result`
- `ai-providers-result`
- `ai-selection-result`
- `ai-settings-result`
- `ai-status`
- `ai-templates-result`
- `bootstrap-result`
- `catalog-inspection`
- `catalog-maintenance-result`
- `clipboard-result`
- `customization-result`
- `done`
- `error`
- `export-result`
- `extension-session`
- `extensions-result`
- `file-selection`
- `filter-alias-result`
- `hidden-items-result`
- `index.roots.result`
- `package-commit-result`
- `package-preview-result`
- `package-search-result`
- `pong`
- `preferences-result`
- `preview-result`
- `privacy-audit-result`
- `progress`
- `query-history-result`
- `quicklinks-result`
- `result`
- `saved-search-result`
- `status`
- `suggestions-result`

## Request fields

- `ActionId`
- `ActiveApplication`
- `ActiveWorkspace`
- `AgentPreferences`
- `AiDraft`
- `AiProvider`
- `AiTemplate`
- `AiTemplateId`
- `Alias`
- `Answers`
- `BackendId`
- `BrowseDirectory`
- `BrowseOffset`
- `ChallengeId`
- `Clipboard`
- `ConfirmationToken`
- `Confirmed`
- `ConversationId`
- `Create`
- `Credential`
- `CurrentDirectory`
- `CustomizationId`
- `Decision`
- `DraftRevision`
- `Enabled`
- `ExpectedRevision`
- `Expression`
- `Extension`
- `ExtensionInput`
- `Generation`
- `GrantId`
- `GrantKind`
- `GrantScope`
- `HerdrWorkspaceId`
- `HistoryId`
- `IncludeHidden`
- `IncludeShortcuts`
- `IndexRoots`
- `InputValues`
- `InvocationReference`
- `Limit`
- `LoginCode`
- `LoginMethod`
- `ModelId`
- `Offset`
- `OmarchyMenuId`
- `Operation`
- `OperationId`
- `OutputAction`
- `PackageNames`
- `PackageOperation`
- `Path`
- `PayloadDigest`
- `PendingRequestId`
- `Phrase`
- `Preferences`
- `PreflightId`
- `PreviewId`
- `ProviderId`
- `Query`
- `Quicklink`
- `QuicklinkId`
- `ReasoningEffort`
- `Refresh`
- `RequestId`
- `ResultId`
- `RootId`
- `SavedSearch`
- `SavedSearchId`
- `Scope`
- `SelectAfterSave`
- `SelectedProject`
- `SessionId`
- `SourcePatch`
- `StableIds`
- `TargetFilterId`
- `TargetRequestId`
- `TargetResultId`
- `TrustDuration`
- `TrustLevel`
- `TurnId`
- `Version`

## Response fields

- `ActionResult`
- `AgentBackends`
- `AgentConversation`
- `AgentEvent`
- `AgentGrants`
- `AgentLogin`
- `AgentModels`
- `AgentOperations`
- `AgentPreferences`
- `AgentStatus`
- `AgentTrust`
- `AgentTrustChallenge`
- `AiChunk`
- `AiDone`
- `AiError`
- `AiModels`
- `AiPreflight`
- `AiProviderSave`
- `AiProviderTest`
- `AiProviders`
- `AiSelection`
- `AiSettings`
- `AiTemplates`
- `Bootstrap`
- `CatalogInspection`
- `CatalogMaintenance`
- `Clipboard`
- `Customization`
- `Error`
- `Export`
- `ExtensionSession`
- `Extensions`
- `FileSelection`
- `FilterAliases`
- `HiddenItems`
- `IndexRoots`
- `PackageCommit`
- `PackagePreview`
- `PackageSearch`
- `Preferences`
- `Preview`
- `PrivacyAudit`
- `Progress`
- `QueryHistory`
- `Quicklinks`
- `ReindexReport`
- `RequestId`
- `Result`
- `RuntimeStatus`
- `SavedSearches`
- `SearchCompletion`
- `SessionId`
- `Suggestions`
- `Type`
- `Version`

## Search completion fields

- `Complete`
- `ContainsPrivateQuery`
- `IncompleteProviderCount`

## Action fields

- `Arguments`
- `CloseAfterAction`
- `Hidden`
- `Id`
- `Inputs`
- `IsDestructive`
- `Kind`
- `RequiresConfirmation`
- `Title`

## Query expression fields

- `Clauses`
- `ExcludeFilterIds`
- `IncludeFilterIds`
- `LiteralQuery`

## Ranking explanation fields

- `Contributions`
- `FinalScore`
- `Matches`
- `Placement`
- `Policies`

## Ranking placement fields

- `LastUseAgeDays`
- `Promoted`
- `Reason`
- `Tier`
- `UsageId`

## Search filters

| ID | Token | Aliases |
|---|---|---|
| `projects` | `proj:` | — |
| `snippets` | `snip:` | — |
| `scripts` | `script:` | — |
| `clipboard` | `clip:` | — |
| `folders` | `browse:` | — |
| `omarchy-control` | `oma:` | — |
| `applications` | `app:` | — |
| `files` | `file:` | `text:`, `files:` |
| `windows` | `win:` | `window:` |
| `workspaces` | `workspace:` | `ws:`, `workspaces:` |
| `herdr` | `herdr:` | — |
| `hey` | `hey:` | `mail:` |
| `herdr-spaces` | `hspace:` | `herdr-space:`, `hspaces:` |
| `herdr-tabs` | `htab:` | `herdr-tab:`, `htabs:` |
| `herdr-attention` | `hattention:` | `herdr-attention:` |
| `configuration` | `cfg:` | `config:` |
| `commands` | `cmd:` | — |
| `quicklinks` | `link:` | — |
| `system-menu` | `system:` | — |
| `ai` | `ai:` | `codex:`, `ask:`, `claude:`, `cursor:`, `grok:` |
| `services` | `svc:` | `unit:`, `service:` |
| `timezone` | `tz:` | `time:` |
| `web-search` | `web:` | — |
| `emoji` | `emoji:` | `e:` |
| `desktop-controls` | `ctl:` | `control:`, `controls:` |
| `processes` | `kill:` | `proc:`, `process:`, `processes:` |

## Configurable sources

- `projects` — Projects
- `snippets` — Snippets
- `scripts` — Script commands
- `clipboard` — Clipboard history
- `folders` — Browse folders
- `omarchy-control` — Omarchy Control
- `applications` — Applications
- `files` — Files
- `windows-workspaces` — Windows & Workspaces
- `herdr` — Herdr
- `hey` — HEY
- `configuration` — Configuration
- `commands-quicklinks` — Commands & Quicklinks
- `omarchy-system` — System Menu
- `systemd-units` — Services
- `timezone` — Timezone
- `web-search` — Web Search
- `emoji` — Emoji
- `desktop-controls` — Desktop controls
- `processes` — Processes
