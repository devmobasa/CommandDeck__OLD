# Configuration reference

## Location and format

Default:

```text
$XDG_CONFIG_HOME/commanddeck/commanddeck.jsonc
```

Usually:

```text
~/.config/commanddeck/commanddeck.jsonc
```

The loader accepts JSON comments and trailing commas. Initialize:

```bash
commanddeck config init
```

`config init` writes a `"$schema": "commanddeck.schema.json"` hint and, when the
shipped schema is next to the binary, copies it beside the config file so
editors can validate and autocomplete. The checked-in example uses
`"$schema": "../schemas/commanddeck.schema.json"`. The schema is generated from
`CommandDeckOptions` and drift-tested like the protocol contract; regenerate it
with `scripts/generate-reference-docs.sh`.

The runtime never rewrites an existing JSONC file. `config init` refuses to
overwrite unless you pass `--force`. Launcher personalization (bookmarks,
aliases, source visibility, reduced motion, display-size, transparency, layout,
closing behavior, and color presets) lives outside JSONC: SQLite for daemon-owned preferences, and small
QML state files under `$XDG_STATE_HOME/commanddeck/` for overlay-only choices.
Display size is stored as an integer percentage in `display-size` (75–150% in
5% steps). **Recommended** selects 100% with sizing based on the presenting
window's logical workspace and Omarchy fonts. Zoom applies equally to text and
controls. Before scaling down for height, the top margin shrinks only as needed,
from its usual 12% (at least 54 logical pixels) toward 4% (at least 24); the
bottom margin stays unchanged. One fit factor then scales text and controls
together. Settings always describes the launcher, including while Settings is
open: it shows the requested percentage, “limited to fit” with the largest 5%
step that fits this screen, and any narrow or scrolling layout. The slider dims
the track above that maximum while keeping the full 75–150% range and saved
value. Zoom-key status also names the maximum when limited. The backdrop covers
the full screen; safe margins keep the launcher toward the top, while Settings
stays centred.

**Settings > Customization > Layout** offers independent **Width** and **Height**
percentages (80–140%, step 5, default 100). These change the launcher's 900 × 600
reference panel without enlarging text or controls: more height shows more rows,
and more width gives the preview room. Extra panel space shrinks to fit before
text or controls scale down. Settings keeps its own size. The choices persist
as `width=PERCENT` and `height=PERCENT` in `launcher-layout`, alongside compact
layout and preview visibility. Each slider has a Reset button for 100%.
`ui.width` and `ui.maximumVisibleRows` are not used by the launcher; use
Settings > Customization > Layout. They remain accepted for existing configs.

The separate `display-sizing` file versions this model. Before first opening a
catalog, the CLI records `logical-v1` for a fresh installation, or `legacy-v1`
when a catalog or earlier state already exists. The UI upgrades the latter to
`legacy-v1:PERCENT`, preserving its original text/layout ratio and initial zoom.
This includes existing default users who never saved a percentage. Legacy preset
words retain their sizes. Choose **Recommended** in Settings to opt into
the new model; it persists `logical-v1`. **Ctrl+0** resets the percentage to 100%
within the current profile and keeps previous sizing for existing users.
Unknown profiles conservatively keep previous sizing until Recommended is selected.
Read-only state still applies for the
current session. No monitor resolution, hardware DPI, or DPR influences Recommended.
There is no `config migrate` command yet; option renames will need one.

JSONC changes require daemon restart. Index-root/content changes in JSONC also
require `commanddeck reindex`. The Settings overlay below applies live.

Inspect or reset daemon-owned preferences with:

```bash
commanddeck preferences show [--json]
commanddeck preferences export
commanddeck preferences reset --yes
```

Export contains preferences and customization snapshots only; it excludes
queries and invocation history.

## Indexed folders in Settings

**Settings > Search > Indexed folders** lists configuration and added folders,
with their status or issue. The index inspector's **Manage indexed folders**
button opens this card. Add a path, remove an added folder, or disable/enable a
configuration folder without editing JSONC or restarting the daemon.

The launcher preference store keeps added folders (path, creation time and root
options) and disabled configuration paths in SQLite. The effective set is enabled
configuration roots plus added roots, de-duplicated by expanded, absolute path
with trailing separators removed. Disabling a nested root does not exclude it
from another enabled parent root; the parent's policy then applies.

New folders use `IndexRootOptions` defaults: filenames, metadata and eligible
content are searchable, hidden files and symlink following are off, and generated
directory exclusions and repository ignore rules are on. The daemon applies the
same configuration validation, path expansion, `privacy.neverIndex`, protected
paths, symlink and mount boundaries to added roots. Invalid, missing or protected
folders show an inline error. Existing missing configuration roots remain listed
so they can be disabled. Enabling an unavailable configuration root keeps its
issue visible.

A change commits its preferences, immediately purges revoked Search and Home
coverage, and requests a full reindex in one SQLite transaction. Overlapping roots
keep only the rows and search fields their remaining policy allows. The catalog
owner runs the same full scan used by `commanddeck reindex` and startup recovery,
rebuilding watches before scanning. Search and folder management stay available;
the Settings card shows progress or retry status. Repeated edits coalesce into one
pending full scan and cancel an outdated scan before it can publish. The durable
reindex request survives restart and clears only when the full generation commits.
Daemon shutdown cancels background work before draining requests. JSONC remains
unchanged.

```bash
commanddeck index roots [--json]
commanddeck index add ~/Documents
commanddeck index remove <root-id>
commanddeck index disable <root-id>
commanddeck index enable <root-id>
```

CLI paths are expanded and made absolute in the caller's working directory
before transmission. These commands use the running daemon by default, or a local
runtime when it is absent. Local edits wait for indexing and honour Ctrl+C; daemon
edits return after the preference transaction while indexing continues. `--socket <path>` selects a daemon explicitly. `--config <file>` selects a
local runtime unless `--socket` is also supplied; stop that configuration's daemon
first to release its index lease.

Customization export includes `indexRoots`. `commanddeck index import <export-file>`
restores only that folder overlay from a customization export, replacing existing
added and disabled choices after validation; it does not restore other preferences.
Review paths before importing. **Reset customizations** (or `preferences reset
--yes`) removes added folders and re-enables all configuration roots, applying
the effective roots live. Exported data is never restored automatically.

## Top Matches rollout switch

`search.ranking.topMatches.enabled` defaults to `true`. It enables the bounded
three-row Top Matches section, including strongly relevant recent learned
matches. Set it to `false` and restart the daemon to show results only in their
source groups, including exact matches. History is retained
and no database migration is needed. **Top Matches** in Settings > Search
overrides this default from the next search, without a restart. Resetting
customizations returns it to the configuration. See [Search and ranking](search-and-ranking.md)
for eligibility, source caps, and privacy gates.

## Top-level schema

```jsonc
{
  "storage": {},
  "search": {},
  "indexing": {},
  "privacy": {},
  "ui": {},
  "quicklinks": [],
  "commands": [],
  "ai": {}
}
```

The generated JSON Schema sets `additionalProperties: false` so editors flag
unknown keys. The daemon still ignores unmapped members for forward
compatibility and never rewrites an existing JSONC file.

## Generated option inventory

The inventory below is generated from the actual option model and default
object. Detailed guidance remains in the hand-written sections that follow.

<!-- BEGIN GENERATED OPTION TABLE -->
<!-- Generated from CommandDeckOptions and its option types. Do not edit this table. -->
| Option | Type | Default |
|---|---|---|
| `clipboardHistory` | `ClipboardHistoryOptions` | object |
| `clipboardHistory.maximumEntries` | `integer` | `200` |
| `clipboardHistory.retentionHours` | `integer` | `168` |
| `scriptDirectories` | `string[]` | 1 default item(s) |
| `snippets` | `SnippetOptions[]` | 0 default item(s) |
| `snippets[].id` | `string` | `null` |
| `snippets[].title` | `string` | `null` |
| `snippets[].text` | `string` | `null` |
| `projects` | `ProjectOptions[]` | 0 default item(s) |
| `projects[].title` | `string` | `null` |
| `projects[].path` | `string` | `null` |
| `projects[].editor` | `string` | `null` |
| `projects[].editorArguments` | `string[]` | `null` |
| `storage` | `StorageOptions` | object |
| `storage.databasePath` | `string` | `$XDG_STATE_HOME/commanddeck/commanddeck.db` |
| `storage.busyTimeoutMilliseconds` | `integer` | `5000` |
| `search` | `SearchOptions` | object |
| `search.defaultLimit` | `integer` | `50` |
| `search.maximumLimit` | `integer` | `200` |
| `search.providerTimeoutMilliseconds` | `integer` | `350` |
| `search.learnFromInvocations` | `boolean` | `true` |
| `search.omarchyMenuState` | `boolean` | `true` |
| `search.calculator` | `CalculatorOptions` | object |
| `search.calculator.inline` | `boolean` | `true` |
| `search.calculator.exchangeRates` | `boolean` | `true` |
| `search.calculator.useQalc` | `string?` | `null` |
| `search.ranking` | `RankingOptions` | object |
| `search.ranking.titleWeight` | `number` | `7` |
| `search.ranking.pathWeight` | `number` | `1.5` |
| `search.ranking.metadataWeight` | `number` | `2` |
| `search.ranking.contentWeight` | `number` | `1` |
| `search.ranking.configWeight` | `number` | `4` |
| `search.ranking.sourceWeights` | `object` | 0 default item(s) |
| `search.ranking.maximumResultsPerSource` | `integer` | `50` |
| `search.ranking.firstPageSize` | `integer` | `9` |
| `search.ranking.minimumSourcesOnFirstPage` | `integer` | `2` |
| `search.ranking.topMatches` | `TopMatchesOptions` | object |
| `search.ranking.topMatches.enabled` | `boolean` | `true` |
| `search.ranking.adaptive` | `AdaptiveRankingOptions` | object |
| `search.ranking.adaptive.enabled` | `boolean` | `true` |
| `search.ranking.adaptive.retentionDays` | `integer` | `730` |
| `search.ranking.adaptive.maximumEvents` | `integer` | `10000` |
| `search.ranking.timeAware` | `TimeAwareRankingOptions` | object |
| `search.ranking.timeAware.enabled` | `boolean` | `true` |
| `search.ranking.timeAware.applicationMultiplier` | `number` | `1` |
| `search.ranking.timeAware.fileMultiplier` | `number` | `0.9` |
| `search.ranking.timeAware.workspaceMultiplier` | `number` | `1.1` |
| `search.ranking.timeAware.actionMultiplier` | `number` | `0.8` |
| `search.ranking.context` | `ContextRankingOptions` | object |
| `search.ranking.context.enabled` | `boolean` | `true` |
| `search.ranking.context.activeApplicationBoost` | `number` | `0.05` |
| `search.ranking.context.activeWorkspaceBoost` | `number` | `0.05` |
| `search.ranking.context.currentDirectoryBoost` | `number` | `0.04` |
| `search.ranking.context.selectedProjectBoost` | `number` | `0.06` |
| `search.webEngines` | `WebEngineOptions[]` | 2 default item(s) |
| `search.webEngines[].id` | `string` | `google` |
| `search.webEngines[].name` | `string` | `Google` |
| `search.webEngines[].urlTemplate` | `string` | `https://www.google.com/search?q={query}` |
| `search.webEngines[].alias` | `string` | `g` |
| `search.webEngines[].includeInGlobal` | `boolean` | `false` |
| `indexing` | `IndexingOptions` | object |
| `indexing.roots` | `IndexRootOptions[]` | 4 default item(s) |
| `indexing.roots[].path` | `string` | `~` |
| `indexing.roots[].includeHidden` | `boolean` | `true` |
| `indexing.roots[].includeFileNameInSearch` | `boolean` | `true` |
| `indexing.roots[].includeMetadataInSearch` | `boolean` | `true` |
| `indexing.roots[].includeContentInSearch` | `boolean` | `false` |
| `indexing.roots[].followSymbolicLinks` | `boolean` | `false` |
| `indexing.roots[].excludeGeneratedDirectories` | `boolean` | `true` |
| `indexing.roots[].respectGitIgnore` | `boolean` | `true` |
| `indexing.roots[].includeGlobs` | `string[]` | 0 default item(s) |
| `indexing.roots[].excludeGlobs` | `string[]` | 0 default item(s) |
| `indexing.roots[].fileTypePolicies` | `FileTypeIndexPolicyOptions[]` | 0 default item(s) |
| `indexing.roots[].fileTypePolicies[].pattern` | `string` | `*.ext` |
| `indexing.roots[].fileTypePolicies[].includeFileNameInSearch` | `boolean?` | `null` |
| `indexing.roots[].fileTypePolicies[].includeMetadataInSearch` | `boolean?` | `null` |
| `indexing.roots[].fileTypePolicies[].includeContentInSearch` | `boolean?` | `null` |
| `indexing.roots[].fileTypePolicies[].maximumFileBytes` | `integer?` | `null` |
| `indexing.roots[].fileTypePolicies[].maximumExtractedBytes` | `integer?` | `null` |
| `indexing.roots[].fileTypePolicies[].extractorTimeoutMilliseconds` | `integer?` | `null` |
| `indexing.roots[].fileTypePolicies[].extractorMemoryLimitBytes` | `integer?` | `null` |
| `indexing.maximumTextFileBytes` | `integer` | `2097152` |
| `indexing.maximumDocumentFileBytes` | `integer` | `20971520` |
| `indexing.maximumExtractedBytes` | `integer` | `2097152` |
| `indexing.indexDesktopApplications` | `boolean` | `true` |
| `indexing.indexOmarchyCommands` | `boolean` | `true` |
| `indexing.enablePdfExtraction` | `boolean` | `false` |
| `indexing.enablePandocExtraction` | `boolean` | `false` |
| `indexing.extractorTimeoutMilliseconds` | `integer` | `8000` |
| `indexing.extractorMemoryLimitBytes` | `integer` | `268435456` |
| `indexing.extractorStandardErrorBytes` | `integer` | `32768` |
| `indexing.maximumConcurrentExtractors` | `integer` | `2` |
| `indexing.enableIncrementalIndexing` | `boolean` | `true` |
| `indexing.incrementalDebounceMilliseconds` | `integer` | `120` |
| `indexing.incrementalMaximumLatencyMilliseconds` | `integer` | `750` |
| `indexing.incrementalEventBatchLimit` | `integer` | `2048` |
| `indexing.enableScheduledMaintenance` | `boolean` | `true` |
| `indexing.maintenanceIntervalMinutes` | `integer` | `360` |
| `privacy` | `PrivacyOptions` | object |
| `privacy.storeQueryHistory` | `boolean` | `false` |
| `privacy.queryHistoryRetentionDays` | `integer` | `90` |
| `privacy.maximumQueryHistoryEntries` | `integer` | `1000` |
| `privacy.storeUsageHistory` | `boolean` | `true` |
| `privacy.persistClipboard` | `boolean` | `false` |
| `privacy.neverIndex` | `string[]` | 15 default item(s) |
| `ui` | `UiOptions` | object |
| `ui.preferredBinding` | `string` | `ALT+SPACE` |
| `ui.alsoBindSuperSpace` | `boolean` | `false` |
| `ui.monitorPolicy` | `string` | `focused` |
| `ui.debounceMilliseconds` | `integer` | `45` |
| `ui.width` | `integer` | `760` |
| `ui.maximumVisibleRows` | `integer` | `9` |
| `quicklinks` | `QuicklinkOptions[]` | 2 default item(s) |
| `quicklinks[].id` | `string` | `arch-wiki` |
| `quicklinks[].title` | `string` | `Search Arch Wiki` |
| `quicklinks[].urlTemplate` | `string` | `https://wiki.archlinux.org/index.php?search={query}` |
| `quicklinks[].alias` | `string?` | `aw` |
| `quicklinks[].icon` | `string?` | `archlinux` |
| `commands` | `CustomCommandOptions[]` | 0 default item(s) |
| `commands[].id` | `string` | `example` |
| `commands[].title` | `string` | `Example` |
| `commands[].arguments` | `string[]` | 0 default item(s) |
| `commands[].keywords` | `string[]` | 0 default item(s) |
| `commands[].workingDirectory` | `string?` | `null` |
| `commands[].requiresConfirmation` | `boolean` | `false` |
| `commands[].icon` | `string?` | `null` |
| `ai` | `AiOptions` | object |
| `ai.enabled` | `boolean` | `false` |
| `ai.providers` | `AiProviderOptions[]` | 2 default item(s) |
| `ai.providers[].id` | `string` | `ollama` |
| `ai.providers[].displayName` | `string?` | `Ollama` |
| `ai.providers[].kind` | `string` | `ollama` |
| `ai.providers[].baseUrl` | `string` | `http://127.0.0.1:11434` |
| `ai.providers[].model` | `string` | `qwen3:8b` |
| `ai.providers[].credential` | `AiCredentialOptions` | object |
| `ai.providers[].credential.kind` | `string` | `none` |
| `ai.providers[].credential.name` | `string?` | `null` |
| `ai.providers[].allowRemoteEndpoint` | `boolean` | `false` |
| `ai.providers[].enabled` | `boolean` | `true` |
| `ai.providers[].contextLimit` | `integer?` | `null` |
| `agent` | `AgentOptions` | object |
| `agent.codexBinaryPath` | `string?` | `null` |
| `agent.claudeBinaryPath` | `string?` | `null` |
| `agent.cursorBinaryPath` | `string?` | `null` |
| `agent.grokBinaryPath` | `string?` | `null` |
| `agent.bubblewrapPath` | `string` | `/usr/bin/bwrap` |
| `agent.systemdRunPath` | `string` | `/usr/bin/systemd-run` |
| `agent.runtimeDirectory` | `string` | `$XDG_RUNTIME_DIR/commanddeck` |
| `agent.stateDirectory` | `string` | `$XDG_STATE_HOME/commanddeck` |
| `agent.minimumCodexMajorVersion` | `integer` | `0` |
| `agent.minimumCodexMinorVersion` | `integer` | `152` |
| `agent.minimumCodexPatchVersion` | `integer` | `1` |
| `agent.maximumCodexMajorVersion` | `integer` | `0` |
| `agent.maximumCodexMinorVersion` | `integer` | `999` |
| `agent.maximumCodexPatchVersion` | `integer` | `999` |
| `agent.apiKeyCredential` | `AiCredentialOptions` | object |
| `agent.apiKeyCredential.kind` | `string` | `environment` |
| `agent.apiKeyCredential.name` | `string?` | `OPENAI_API_KEY` |
| `agent.autoApproveReadOnlyCommands` | `boolean` | `false` |
<!-- END GENERATED OPTION TABLE -->

## `storage`

```jsonc
{
  "storage": {
    "databasePath": "$XDG_STATE_HOME/commanddeck/commanddeck.db",
    "busyTimeoutMilliseconds": 5000
  }
}
```

`databasePath` supports `~`, `$HOME`, and selected XDG variables. Keep the
database on a local filesystem. Do not place it on a network mount.

## `search`

```jsonc
{
  "search": {
    "defaultLimit": 50,
    "maximumLimit": 200,
    "providerTimeoutMilliseconds": 350,
    "learnFromInvocations": true,
    "calculator": {
      "inline": true,
      "exchangeRates": true
    }
  }
}
```

- `defaultLimit`: normal response cap.
- `maximumLimit`: server-side cap.
- `providerTimeoutMilliseconds`: shared search deadline.
- `learnFromInvocations`: usage/recency updates after successful actions.
- `calculator.inline`: `true` (default) shows a clear calculation above the normal
  search results without `=`. Settings > Search > **Inline calculator** overrides
  this default without a restart. Explicit `=` expressions work with either choice.
- `calculator.exchangeRates`: `true` (default) permits CommandDeck's lazy ECB fetch.
  A currency expression with no fresh cache starts exactly one HTTPS GET of
  `https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml`, with only
  `User-Agent: CommandDeck`, no cookies and no query content. Refreshes run at most
  once per 6 hours after success and once per 30 minutes after a failure. Rates
  are cached beside the catalog database as `<databasePath>.exchange-rates`.
  Set `search.calculator.exchangeRates=false` to prevent network access; an existing
  cache still works. Missing rates produce no row; the next search can use the fetch.
  There is no Settings control for this option.
- `calculator.useQalc` is deprecated and ignored. Any string value is accepted for
  compatibility. Remove it; Doctor reports one non-failing reminder when it is set.

The calculator evaluates arithmetic, functions, constants, units, durations, and
currency in process. Complete conversions such as `100 usd to eur`, `5 km to miles`,
`105F in C`, and `255 to hex` work inline. Use `=` for the full expression grammar
or when inline calculation is off. Currency titles show two decimals; details
include the precise value and the ECB reference date. See the
[calculator grammar and precision rules](search-and-ranking.md#calculator-and-date-counts).

`search.ranking` controls FTS field weights, optional per-source multipliers,
per-source result caps, and first-page source diversity. Its nested `adaptive`
policy is enabled by default, retains at most 10,000 successful invocation
events for 730 days, and contributes an inspectable decaying frecency boost.
Set `adaptive.enabled=false` to ignore and stop collecting this signal without
deleting it. Inspect, export, or reversibly clear the local learning state with:

```bash
commanddeck ranking inspect [--json]
commanddeck ranking export
commanddeck ranking reset --yes
```

Inspection is bounded to 256 events, export is bounded to 256 KiB of UTF-8,
and human-readable query text is terminal-escaped. Reset removes both aggregate
usage and invocation events; it does not affect bookmarks or aliases.

Production should use per-provider deadlines rather than one linked timeout.

### Herdr

The Herdr space and tab source has no JSONC keys. It is controlled only by the
`herdr` entry under **Settings → Search sources** (search on by default, Home
off by default) and by whether a `herdr` executable is on the daemon PATH. The
daemon always targets Herdr's default session and forwards none of its own
`HERDR_*` environment; named sessions and socket overrides are not configurable.

## `indexing`

**Settings → Search → File search → Home folder only** is an immediate search
filter, saved as the `searchHomeOnly` launcher preference. It restricts file and
configuration results, including aliases and Home entries, to the current user's
home and its descendants. Applications and other sources remain available. The
toggle defaults to off, which searches all configured index roots. It does not
add roots, rebuild the index, or remove indexed data.

```jsonc
{
  "indexing": {
    "roots": [
      {
        "path": "~/Projects",
        "includeHidden": false,
        "includeFileNameInSearch": true,
        "includeMetadataInSearch": true,
        "includeContentInSearch": true,
        "followSymbolicLinks": false,
        "excludeGlobs": [
          "*/.git/*",
          "*/node_modules/*",
          "*/bin/*",
          "*/obj/*",
          "*/target/*"
        ],
        "fileTypePolicies": [
          {
            "pattern": "*.log",
            "includeContentInSearch": false,
            "maximumFileBytes": 1048576,
            "maximumExtractedBytes": 131072,
            "extractorTimeoutMilliseconds": 2000,
            "extractorMemoryLimitBytes": 134217728
          }
        ]
      }
    ],
    "maximumTextFileBytes": 2097152,
    "maximumDocumentFileBytes": 20971520,
    "maximumExtractedBytes": 2097152,
    "indexDesktopApplications": true,
    "indexOmarchyCommands": true,
    "enablePdfExtraction": false,
    "enablePandocExtraction": false,
    "extractorTimeoutMilliseconds": 8000,
    "extractorMemoryLimitBytes": 268435456,
    "extractorStandardErrorBytes": 32768,
    "maximumConcurrentExtractors": 2,
    "enableIncrementalIndexing": true,
    "incrementalDebounceMilliseconds": 120,
    "incrementalMaximumLatencyMilliseconds": 750,
    "incrementalEventBatchLimit": 2048,
    "enableScheduledMaintenance": true,
    "maintenanceIntervalMinutes": 360
  }
}
```

### Root fields

- `path`: directory. Missing roots are skipped.
- `includeHidden`: permits dotfiles/directories within this root, still subject
  to global deny.
- `includeFileNameInSearch`: include the display filename in FTS. The result
  still displays its filename when this is false.
- `includeMetadataInSearch`: include relative/full path metadata in FTS. The
  canonical path remains available to typed open/reveal actions.
- `includeContentInSearch`: permit eligible body extraction and FTS content.
- `followSymbolicLinks`: false recommended. Current security hardening is not
  sufficient for true.
- `excludeGeneratedDirectories`: defaults to true. Prunes recognized dependency,
  cache, and project output directories before extraction.
- `respectGitIgnore`: defaults to true. Applies repository `.gitignore` rules
  and `.git/info/exclude`, including linked worktrees.
- `includeGlobs`: root-specific exceptions to generated-directory and repository
  ignore rules. Defaults to an empty array.
- `excludeGlobs`: root-specific exclusions. A matching directory excludes all
  descendants during both scans and individual updates.
- `fileTypePolicies`: ordered filename-glob overrides for the three search
  fields and optional extraction limits. `maximumFileBytes`,
  `maximumExtractedBytes`, `extractorTimeoutMilliseconds`, and
  `extractorMemoryLimitBytes` inherit the global value when omitted; later
  matching rules win. Patterns are filename globs, not paths.

New installations index filenames and paths throughout the user's home,
including hidden files. Content extraction stays limited to the more specific
Documents, Omarchy, and Hyprland roots:

```text
~                          (filenames and paths)
~/Documents
~/.config/omarchy
~/.config/hypr
```

Privacy exclusions still apply to every root. CommandDeck automatically excludes
its own SQLite database and indexing sidecars from scanning and watcher events,
including when `storage.databasePath` is customized.

### Developer directory exclusions

With `excludeGeneratedDirectories: true`, `node_modules`, `__pycache__`, and
`.git` directories are excluded anywhere below a root. A directory containing
`pyvenv.cfg` is recognized as a Python environment. Other names require a marker
in their immediate parent:

| Project marker | Excluded child directories |
|---|---|
| `package.json` | `dist`, `.next`, `.nuxt`, `.svelte-kit`, `.output` |
| `Cargo.toml` or `pom.xml` | `target` |
| `*.csproj`, `*.fsproj`, `*.vbproj` | `bin`, `obj` |
| `build.gradle`, `build.gradle.kts`, `settings.gradle`, `settings.gradle.kts` | `build`, `.gradle` |
| `pyproject.toml`, `setup.py`, `setup.cfg`, `requirements.txt`, `Pipfile` | `.venv`, `venv` |
| `composer.json` | `vendor` |

An explicitly configured root is not excluded by its own directory name.
Nested roots use their own settings. Custom output locations can be covered by
repository ignores or `excludeGlobs`; CommandDeck does not execute build tools
or evaluate their configuration to discover those locations.

Repository ignores use ordered, directory-relative rules, nested `.gitignore`
files, `!` exceptions, anchored patterns, `**`, and Git character classes.
An ignored parent must be re-included before Git negation can include its children.
Rules above a configured subdirectory still apply. Matching tracked files are
also excluded from search. Global Git configuration, `core.excludesFile`,
`.ignore`, and `.rgignore` are not read. Symlinked ignore files are not followed.
Unreadable or oversized rule files exclude their scope with an
`ignore-rules-unavailable` diagnostic. Limits are 1 MiB per file, 16,384 patterns,
and 1,024 characters per line.

Root globs use the existing simple wildcard syntax, independently of Git rules:
`*` and `?` match paths or basenames, with `**` treated as `*`. Relative paths
are relative to the configured root; directory matches cover descendants.
For example, this retains one useful generated document:

```json
{
  "path": "~/code",
  "includeHidden": true,
  "excludeGeneratedDirectories": true,
  "respectGitIgnore": true,
  "includeGlobs": ["my-app/dist/manual.pdf"],
  "excludeGlobs": ["my-app/private/"]
}
```

Explicit inclusions open the necessary parent directories for traversal and
override generated defaults and Git ignores. Privacy rules, explicit exclusions,
hidden-file settings, storage exclusions, and path boundaries remain authoritative.
Set either default switch to false on a root to disable that layer.

The same policy covers full and scoped scans, direct updates, and previews.
Watcher events in excluded trees are dropped before journal writes. Ignore and
project-marker changes reconcile existing catalog and content rows; saved rule
fingerprints also detect changes made while the daemon was stopped. With
incremental indexing disabled, run `commanddeck reindex` to apply file changes;
a restart alone does not rescan roots.
Configuration changes require a daemon restart.

The index inspector shows an approximate exclusion count.
`commanddeck catalog inspect` also lists skipped paths and matching rules from the last full scan,
including `generated-directory` and `repository-ignore` reasons. Each inspection
checks at most 256 recorded entries and omits path-level exclusions whose
non-redacted paths no longer exist.
Root-unavailable entries remain unchanged until a full scan. Existing paths are not
rechecked against policy. Counts exclude omitted entries in that sample;
additional stored entries remain counted until checked or rescanned.
Entries with a root or relative path at the 512-character storage cap remain unchecked because the path may be truncated.
Private entries have no stored path to check, so they remain unchanged until
the next full scan or `commanddeck reindex`. New exclusions appear after a scan
records them.

`commanddeck status --json` includes `ignoredEventCount` in its incremental
status, and scan reports count discovered,
indexed, and skipped entries. A pruned directory counts as one skipped entry;
its contents are not enumerated. Native recursive filesystem watches remain
registered inside excluded directories; event filtering reduces journal and
indexing work, not the native watch count.

Roots do not cross mount boundaries or follow symbolic links by default. Add
each additional mounted folder explicitly; indexing `/` does not include a
separately mounted `/home`. Existing installations retain their configured
roots. To adopt home filename search, add the `~` root shown in the example,
remove obsolete roots such as a nonexistent `~/Projects`, stop the daemon,
run `commanddeck reindex`, and start the daemon again.

### Extractors

PDF requires `pdftotext`; document formats require `pandoc`. Enable only after
installing and accepting parser risk.

External extraction is serialized by `maximumConcurrentExtractors` and runs
with a cleared environment containing only `PATH`, `LANG`, and `LC_ALL`.
Standard output and error are capped while streaming, timeout kills the full
process tree, and Linux address space is hard-limited with `/usr/bin/prlimit`.
Any limit, launch, or exit failure falls back to filename/path metadata rather
than indexing partial document output. Plain-text results are safely truncated
to the resolved `maximumExtractedBytes` byte limit.

### Incremental operation and maintenance

Incremental indexing is enabled by default. The debounce and maximum-latency
values coalesce ordinary editor bursts; `incrementalEventBatchLimit` bounds a
single journal batch before affected-root reconciliation takes over.

Watchers only observe a running daemon. On start, the runtime that owns the
event journal compares each previously scanned, watched, unpaused root with the
size and modification time recorded in the catalog. It then indexes files created
or changed while it was stopped and removes rows for deleted files, without a
full rebuild. A root that was missing at start is watched again after it exists
and is resumed or reindexed.

With `indexDesktopApplications` on, the same owner watches the XDG applications
directories and replaces the application rows after any change there, after the
same debounce. It rescans them once at start as well. Modification times are
not compared, so packages that keep old file times are found. A directory
created or replaced after start is watched after a restart.

Scheduled maintenance is enabled by default every 360 minutes. The interval is
validated from 1 minute through 7 days. Each run removes catalog rows for files
that no longer exist, checks SQLite/FTS integrity, and optimizes the index while
holding the same mutation lock as reindexing. Last/next run and errors are
visible through `commanddeck index status` and the runtime status protocol.

## `privacy`

```jsonc
{
  "privacy": {
    "storeQueryHistory": false,
    "storeUsageHistory": true,
    "persistClipboard": false,
    "neverIndex": [
      "~/.ssh",
      "~/.gnupg",
      "~/.password-store",
      "~/.local/share/keyrings",
      "**/.env",
      "**/.env.*",
      "**/*credentials*",
      "**/*secret*",
      "**/*.pem",
      "**/*.key"
    ]
  }
}
```

`neverIndex` overrides roots. Clipboard capture is off by default and is enabled
explicitly through the `clip:` workflow. Its persistent opt-in and bounded
private store are described in [clipboard history](launcher-workflows.md#clipboard-history);
`persistClipboard` is not the capture toggle.

Unless Settings overrides them (below), `storeQueryHistory=false` means successful
invocations record no raw query, and `storeUsageHistory=false` means they record
no usage.

These are defaults that Settings can override without editing this file:
**Remember searches** (Customization > Data) overrides `storeQueryHistory`,
**Learn from what you open** overrides `search.learnFromInvocations` together
with `storeUsageHistory`, **Omarchy menu state** (Search) overrides
`search.omarchyMenuState`, and **Top Matches** (Search) overrides
`search.ranking.topMatches.enabled`. **Inline calculator** (Search) overrides
`search.calculator.inline`. Until you change one in Settings, the configuration
applies; once changed, the choice is stored with the launcher preferences and
takes effect from the next search, without a restart. Resetting customizations
returns these choices to the configuration. `search.ranking.adaptive.enabled` still
gates usage learning, and `commanddeck preferences` shows each value and where
it comes from.

## `ui`

UI options are static shell hints. Interactive launcher preferences override
only the corresponding behavior and do not rewrite this JSONC file.

```jsonc
{
  "ui": {
    "preferredBinding": "ALT+SPACE",
    "alsoBindSuperSpace": false,
    "monitorPolicy": "focused",
    "debounceMilliseconds": 45,
    "width": 760,
    "maximumVisibleRows": 9
  }
}
```

Bindings are applied in Hyprland config, not automatically by the daemon.

## `quicklinks`

```jsonc
{
  "quicklinks": [
    {
      "id": "arch-wiki",
      "title": "Search Arch Wiki",
      "alias": "aw",
      "urlTemplate": "https://wiki.archlinux.org/index.php?search={query}",
      "icon": "archlinux"
    }
  ]
}
```

Manage quicklinks in **Settings > Search > Quicklinks**, or from the CLI:

```bash
commanddeck quicklinks
commanddeck quicklinks add --title "Search docs" --url 'https://example.org/search?q={query}' --alias docs --icon help-browser
commanddeck quicklinks edit USER_ID --title "Updated docs" --url 'https://example.org/?q={query}'
commanddeck quicklinks remove USER_ID
commanddeck quicklinks disable CONFIGURED_ID
commanddeck quicklinks enable CONFIGURED_ID
commanddeck quicklinks --json
commanddeck preferences export > backup.json
commanddeck quicklinks restore backup.json
```

Settings shows a source chip for each row. User quicklinks can be edited or
removed; configured ones can be disabled or enabled without changing the config
file. Settings confirms removal, shows validation errors inline, and previews
the URL using an editable sample query. Add and Edit focus the title field;
Tab moves through the form, Enter saves, and Escape cancels. Up and Down move
between list actions. Changes apply immediately to search, aliases, and the web
fallback choices. Refresh reloads changes made by another client.

User quicklinks and disabled configured IDs are stored in SQLite, separately
from the rebuildable index. The effective list contains enabled configured
quicklinks and user quicklinks. At most 100 user quicklinks are supported.
Configured IDs and aliases stay reserved even while disabled. A configuration
change that conflicts with stored user quicklinks reports the same duplicate
error; remove or rename the user quicklink before applying that configuration.
Removing or disabling the selected fallback resets it to the default, **None**,
and reports that reset. It leaves other preferences intact.

`preferences export` includes portable user quicklinks (including creation times)
and disabled configured IDs. `quicklinks restore BACKUP.json` replaces that
quicklink state and its web fallback choice atomically, validates collisions
against the current configuration and aliases, and leaves other preferences
intact. If the saved fallback is unavailable, restore selects **None** and reports
it. `preferences reset --yes` clears user quicklinks and exclusions and restores
the configured search projection in the same transaction.

Both configuration and management use the same validation and error messages:
IDs use 1–64 ASCII letters, digits, dots, underscores, or hyphens and start with
a letter or digit. IDs and aliases are unique case-insensitively. Quicklink,
bookmark, and filter aliases share a namespace; built-in launcher tokens are
reserved. Titles use
1–64 characters, URL templates 1–2048, and optional icons 1–256; controls are
rejected. Optional aliases use 1–32 ASCII letters, digits, dots, underscores,
or hyphens and start with a letter or digit. URLs must be absolute HTTP or
HTTPS URLs without credentials. Use HTTPS for remote services.

`{query}` placeholders are case-insensitive, may appear more than once, and are
replaced with URL-encoded search text. Other brace placeholders are rejected.
A URL without a placeholder opens as a fixed link. An alias at the beginning
of the query selects the quicklink and is stripped before URL expansion.
Preview and search do not connect to the destination; invocation opens it in
the browser. Review destinations before sending sensitive query text.

## `commands`

```jsonc
{
  "commands": [
    {
      "id": "open-project-terminal",
      "title": "Open Project Terminal",
      "arguments": [
        "foot",
        "--working-directory",
        "/home/user/Projects/example"
      ],
      "keywords": ["terminal", "project"],
      "workingDirectory": "~/Projects/example",
      "requiresConfirmation": false,
      "icon": "utilities-terminal"
    }
  ]
}
```

Rules:

- first `arguments` element is executable;
- remaining elements are literal argv;
- no shell expansion, pipes, redirection, globbing, `$VAR`, or `~` expansion in
  argument values;
- `workingDirectory` is expanded;
- use absolute paths when ambiguity matters;
- confirmation should be true for irreversible operations.

Configured commands are indexed during reindex.

## `ai`

```jsonc
{
  "ai": {
    "enabled": true,
    "providers": [
      {
        "id": "ollama",
        "displayName": "Ollama",
        "kind": "ollama",
        "baseUrl": "http://127.0.0.1:11434",
        "model": "qwen3:8b",
        "enabled": true
      },
      {
        "id": "cloud",
        "displayName": "OpenAI",
        "kind": "openai-chat-completions",
        "baseUrl": "https://api.openai.com/v1",
        "model": "gpt-5-mini",
        "credential": {
          "kind": "environment",
          "name": "OPENAI_API_KEY"
        },
        "allowRemoteEndpoint": true,
        "enabled": true
      }
    ]
  }
}
```

Kinds currently supported:

```text
ollama
openai-chat-completions
```

AI as a whole remains disabled until `ai.enabled` is explicitly set. Never add
an `apiKey` field; the model deliberately has no such property. Credentials
use `none`, `environment`, or `systemd` plus a validated name. Remote endpoints
require HTTPS and `allowRemoteEndpoint: true`. Plain HTTP is accepted only for
a literal loopback IP address such as `127.0.0.1` or `::1`; `localhost` is not
trusted as loopback because DNS resolution is mutable. Provider IDs may not
reuse the reserved assistant backend IDs (`codex.local`, `claude.local`,
`cursor.local`, `grok.local`).

### Managed provider document

Settings and `commanddeck ai settings|enable|disable|providers add|edit|…`
manage providers in the running daemon. They never rewrite this
file. Instead the daemon owns `ai-providers.json` next to it (mode 0600):

```json
{ "schemaVersion": 1, "revision": 3, "enabled": true, "providers": [ /* AiProviderOptions */ ] }
```

- While the document is absent, `ai.enabled` and `ai.providers` above apply
  and Settings reports the source as `legacy-configuration`.
- The first change made from Settings or the CLI seeds the document from the
  whole effective set plus that change. From then on the document owns the
  enabled flag and the provider set; the `ai` section here is inactive for
  those fields and removed providers do not return on restart.
- An invalid, oversized, symlinked, or unsupported document disables direct
  AI with a reported error and keeps the launcher usable. Repair or delete
  the file deliberately and restart; nothing falls back to this file silently.
- Editing the document by hand while the daemon runs is detected as a
  conflict on the next change; restart the daemon after external edits.

Provider definitions are configuration and are not part of the customization
export or reset.

Discovery and generation deadlines are fixed safety limits rather than
configuration: 3 seconds for local discovery, 10 seconds for remote discovery
(both measured from admission and including capacity-queue time),
30 seconds between stream reads, and 10 minutes overall for one generation.
Preflight discloses the exact adapter route and serialized provider-request JSON
derived from `baseUrl` before any prompt bytes are sent.

## `agent`

The `agent` section configures the embedded assistant's process boundary. It
holds no secrets and no authority. Enablement, backend, model, trust level,
context policy, and retention live in the daemon database as acknowledged
preferences, and the CommandDeck-owned Codex credential lives in a mode-0600
file under `stateDirectory`.

```jsonc
{
  "agent": {
    "codexBinaryPath": null,
    "bubblewrapPath": "/usr/bin/bwrap",
    "systemdRunPath": "/usr/bin/systemd-run",
    "runtimeDirectory": "$XDG_RUNTIME_DIR/commanddeck",
    "stateDirectory": "$XDG_STATE_HOME/commanddeck",
    "minimumCodexMajorVersion": 0,
    "minimumCodexMinorVersion": 152,
    "minimumCodexPatchVersion": 1
  }
}
```

- `codexBinaryPath` overrides PATH discovery. The path is resolved to its
  canonical target once; the target must be a regular file owned by the current
  user or root and not group- or world-writable.
- `bubblewrapPath` and `systemdRunPath` must be absolute. Bubblewrap isolates
  levels 0 to 5; `systemd-run` starts the level-6 transient unit. At level 6,
  Codex keeps its App Server socket and a `.lock` file in
  `/tmp/codex-daemon-<uid>/`; it removes them on a clean exit, and they can
  remain after a crash.
- `runtimeDirectory` receives one private, disposable Codex home per App Server
  generation under `codex/<generation>`. It is wiped on stop.
- `stateDirectory` receives `codex/auth.json`, the CommandDeck-owned login.
  The user's `~/.codex` is never read or written.
- `claudeBinaryPath`, `cursorBinaryPath`, and `grokBinaryPath` override PATH
  discovery of the `claude`, `cursor-agent`, and `grok` CLIs (the Grok CLI is
  also found at `~/.grok/bin/grok`). Each runs inside the same Bubblewrap
  profile with `HOME` set to `stateDirectory/<backend>/home`, so their
  sign-ins, caches, and sessions stay in CommandDeck-owned 0700 directories
  and the user's own `~/.claude`, `~/.cursor`, and `~/.grok` are never read
  or written. See [AI integration](ai.md).
- The Codex version floor defaults to 0.152.1, the version whose App Server
  schema this build reviewed; the ceiling defaults to the rest of the 0.x line
  (0.999.999). A routine Codex update inside that range runs, and
  `commanddeck agent status` and `commanddeck doctor` mark any version above the
  reviewed one as unreviewed. A 1.0 major bump falls outside the range and is
  rejected until its schema is reviewed. Both bounds are configurable, so an
  operator can pin the ceiling back to the reviewed version if they prefer.
- `apiKeyCredential` is the credential reference the API-key sign-in resolves
  when the request carries none. It follows the AI provider credential shape:
  `kind` is `none`, `environment`, or `systemd`, and `name` is the variable or
  systemd credential name. The default reads `OPENAI_API_KEY` from the
  daemon's environment; a systemd user service does not inherit the shell
  environment, so use `systemctl --user set-environment` or the `systemd`
  kind. The key itself never travels over the protocol.

## Environment variables

```text
HOME
XDG_CONFIG_HOME
XDG_STATE_HOME
XDG_CACHE_HOME
XDG_RUNTIME_DIR
HYPRLAND_INSTANCE_SIGNATURE
WAYLAND_DISPLAY
OMARCHY_PATH
VISUAL
EDITOR
PATH
<AI key variable names>
```

## Validation

The loader runs `ConfigurationValidator` before constructing transports,
indexers, commands, or storage. It rejects duplicate IDs, invalid limits,
unsupported provider/credential kinds, unsafe credential names, invalid
provider URLs, non-HTTPS remote providers without explicit permission, invalid
editor adapters, empty command argv, invalid indexing policies, and conflicting
ranking/filter configuration. Unknown JSON fields are ignored; required C#
members and the semantic validator remain the authority for accepted input.

## Example profiles

### Metadata-only project root

```jsonc
{
  "path": "~/LargeArchive",
  "includeHidden": false,
  "includeFileNameInSearch": true,
  "includeMetadataInSearch": true,
  "includeContentInSearch": false,
  "followSymbolicLinks": false,
  "excludeGlobs": [],
  "fileTypePolicies": []
}
```

### Selected dotfiles

```jsonc
{
  "path": "~/.config/hypr",
  "includeHidden": true,
  "includeFileNameInSearch": true,
  "includeMetadataInSearch": true,
  "includeContentInSearch": true,
  "followSymbolicLinks": false,
  "excludeGlobs": [],
  "fileTypePolicies": []
}
```

### No AI

```jsonc
{
  "ai": {
    "enabled": false,
    "providers": []
  }
}
```

Linux directory watching honors indexing exclusions. If the inotify watch limit
is reached, status reports a partially unwatched root. Exclude large directories
or adjust `fs.inotify.max_user_watches`, then resume or reindex to retry.
