# CommandDeck QML frontend

This directory contains the shared Quickshell/QML frontend, its Omarchy host,
and an experimental standalone Hyprland host. It does not crawl files, query
SQLite, invoke AI endpoints, or execute commands inside the shell. It starts a
persistent `commanddeck bridge` process and exchanges versioned JSON Lines with
the user daemon.

## Status

This preview targets Omarchy Quattro's `overlay` plugin kind. Shell APIs can
move, so validate against the installed shell before treating it as a stable
package:

```bash
omarchy plugin validate ./qml
```

See [`docs/install.md`](../docs/install.md) for
installation, key bindings, focused-monitor behavior, troubleshooting, and the
standalone development workflow.

## Clipboard History shortcut

From the main search, type `clip:` or press **Ctrl+Shift+V** to open Clipboard
History. In **Settings → Shortcuts → Open Clipboard History**, enter a
`Ctrl+Shift+letter` or `Ctrl+Alt+letter` chord and select Apply; leave it blank to
disable the shortcut. `Ctrl+Shift+C` stays reserved for copying file paths.
The binding is stored in launcher preferences, works only in the active main
CommandDeck window, and does not change Hyprland bindings. Normal `Ctrl+V` paste
is unchanged. Existing saved-search bindings take precedence on upgrade; Settings
shows a conflict warning if one already uses the clipboard chord.

## Layout

- `manifest.json`: Omarchy package metadata pointing at its thin host.
- `common/`: shared launcher components, protocol client, and desktop
  integration contracts.
- `omarchy/CommandDeck.qml`: Omarchy host that injects Hyprland and theme
  integrations into the shared launcher.
- `omarchy/ThemeTokens.qml`: tokens read from the running Omarchy shell.
- `omarchy/hotkey/LauncherShortcut.qml`: the `commanddeck:toggle` Hyprland
  global shortcut that the Omarchy host creates at run time; the hotkey toggles
  the launcher in-process. Its `.qmllint.ini` explains the one lint setting.
- `quickshell-standalone/`: direct Quickshell host and usage guide.
- `common/CommandDeck.qml`: overlay lifecycle, focused-screen surface, and
  result model.
- `common/ProtocolClient.qml`: persistent stdio bridge.
- `common/Protocol.js`: generated protocol version, operations, messages, and Core domain constants.
- `common/UiConstants.js`: shared UI-only states, action identifiers, and presentation choices.
- `common/Theme.qml`: resolved design tokens with a built-in fallback palette.
- `common/DesktopIntegration.qml`: capability-safe fallback for unsupported desktops.
- `common/HyprlandDesktopIntegration.qml`: monitor, workspace, window, preview, and
  shortcut capabilities used by both current hosts.
- `common/ResultRow.qml`: list result primitive.
- `common/MatchHighlight.js`: renders the daemon's match ranges as escaped,
  highlighted markup for result titles and the detail pane's matched fields.
- `common/ResultListDelegate.qml`: result list row that builds either a section
  header or a `ResultRow`.
- `common/ResultModelController.qml`: sections the results into list rows and
  rewrites only rows whose shown fields changed; the selection and invocations
  read the latest result, including its fresh invocation reference.
- `common/ResultIcon.qml`: icon-theme lookup and glyph fallback for a result.
- `common/ThemedToolTip.qml`: bounded, plain-text hover hints rendered in the launcher
  with its colors and font, rather than the platform's default tooltip window.
- `SectionHeader.qml`: group label above a run of results of one kind.
- `KeyCap.qml`: key legend used by the footer and the row shortcut badge.
- `FooterHintBar.qml`: result actions plus persistent, clickable `? Browse prefixes`
  and `F1 Help` controls, including empty searches and daemon-offline states.
  Narrow layouts hide optional result shortcuts before discovery controls.
- `DetailPane.qml`: selected-result header, ordered facts, and optional
  Markdown body.
- `common/WindowPreview.qml`: debounced live preview for a selected window when
  the active desktop adapter advertises support.
- `ActionPanel.qml`: secondary actions primitive.
- `FilterChip.qml`: removable typed provider filter shown beside the query.
- `AliasDialog.qml`: locally validated primary-alias editor.
- `SettingsPanel.qml`: opaque, searchable settings dialog with a page rail or compact picker; `Ctrl+F` filters cards and pages.
- `SettingsNav.qml`: page rail for the settings dialog.
- `SettingsCard.qml`, `SettingsRow.qml`: searchable titled groups and label/control rows.
- `SettingsChoice.qml`: drop-down whose options carry a description and a badge.
- `SettingsToggle.qml`, `SettingsNumber.qml`, `SettingsSlider.qml`, `SettingsField.qml`, `SettingsTextArea.qml`: themed settings controls that keep the stock Qt Quick Controls contracts.
- `SettingsCheck.qml`: compact checkbox for columns of on/off choices; Space or Enter toggles.
- `ListKeys.js`: the movement keys every list answers (rows, pages, ends), used
  by the result list, action panel, filter palette, pickers, extension lists and
  clipboard history.
- `TabFence.js`: hands Tab and Shift+Tab from a control to the nearest ancestor
  that declares `settingsTabFence`, so focus cycles inside that dialog.
- `HiddenUndoBar.qml`, `SettingsHiddenItemsCard.qml`, `DeckHiddenItemActions.js`: hidden items — the undo strip after "Don't show this" (and after a reversible HEY change, with its own message), the Settings → Customization list with search and Restore, and the hide/restore/list actions behind both.
- `SettingsColorsCard.qml`, `SettingsColorRow.qml`: Settings → Customization → Colors, choosing the Omarchy theme or CommandDeck's palette and editing its accent, background, and text with swatches or a hex value.
- `DisplayPreferences.qml`: owns the QML-only display-size, transparency, and color stores for the launcher.
- `DeckButton.qml`: the one button used across the whole deck (primary, secondary, danger; compact and flat modes) with shared hover and keyboard-focus emphasis. `AgentButton.qml` is the same button without the settings tab fence.
- `HelpSheet.qml`: F1 keyboard and filter reference.
- `PreferenceUpdateQueue.qml`: serialized optimistic preference updates with rollback.
- `PersonalizationController.qml`: serialized customization dispatch and disconnect recovery.
- `ProtocolRequestTracker.qml`: correlated search, action, preview, bootstrap, and settings request state.
- `ResultModelController.qml`: section grouping, row projection, and selection traversal.
- `AnnouncementCoalescer.qml`: settled accessibility status announcements.
- `SourcePreferenceRow.qml`: one source with Search and Home checkboxes, quiet ordering arrows, a drag grip, a dimmed look when it is off everywhere, and a dash instead of a Home switch for a source that never appears on Home (HEY).
- `SettingsHeyCard.qml`: Settings → Search → HEY mail, choosing whether a HEY thread opens in the web app or the HEY terminal app.
- `SettingsSourceList.qml`: the Search sources list; column headings, a keyboard grid with one Tab stop (arrows move, Alt+Up/Down reorders, focus follows the moved source), and drag-to-reorder with a drop line and edge scrolling, saved as one move.
- `AgentController.qml`: daemon-owned assistant conversation state, coalesced
  streaming, and parked-request correlation.
- `AgentSurface.qml`: inline assistant conversation, trust header, pending
  decision cards, login card, and composer.

Headless component behavior is checked by
`scripts/test-qml-components.sh` as part of repository validation. Live
`ScreencopyView` behavior still requires a rendered Wayland session.

The UI uses List, Detail, and ActionPanel alongside settings, assistant, workflow
dialogs, and experimental native extension views. There is no public extension store.

## Shared identifiers

Import `Protocol.js` as `Protocol` for wire/domain values and `UiConstants.js`
as `Ui` for shared UI identifiers. Both use frozen, semantically named groups;
request-tracking tags used by only one controller stay in that controller's
readonly `requestKinds` table. Do not merge unrelated concepts merely because
both currently use a string such as `status` or `failed`.

Regenerate `Protocol.js` with `scripts/generate-protocol-contract.sh` after a
Core contract change. Backend specs check the generated file and resolve QML
constant references. Keep literal wire expectations in tests independent of the
constants. Display text, example inputs, icon names, and Qt property/object names
remain at their point of use; they are not protocol discriminants.

Copy the common `.js` files alongside the `.qml` files when staging a test
harness. The normal plugin installer copies the complete `qml` directory.

## Theming

The Omarchy host reads Omarchy's per-theme tokens, including the `[launcher]`,
`[spacing]`, and `[font]` sections of `theme/shell.toml`. `omarchy/ThemeTokens.qml` is
the only file that imports `qs.Commons`, and `Theme.qml` loads it through
`Qt.createComponent`, because a failed QML import aborts the file it sits in
and `qs.Commons` is an internal shell API that can move. When the load fails
the overlay logs one warning and keeps CommandDeck's own dark palette.

Settings → Customization → Colors can also choose that palette over the Omarchy
theme and adjust its accent, background, and text. `ColorThemeStore.qml` keeps
the choice in a QML-only state file, `ColorThemeFormat.js` holds its pure
validation and contrast rules, and `Theme.qml` replaces only the host's colors
while fonts and metrics keep following Omarchy. The presets and defaults live in
`UiConstants.js`.

See [the architecture guide](../docs/architecture.md) for frontend boundaries.
