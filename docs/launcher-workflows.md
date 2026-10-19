# Launcher workflows

## Projects

Add exact checkout paths to `projects` in your CommandDeck JSONC configuration,
then restart the daemon. Search `proj: vision` to select a checkout. The inspector
shows its current branch and tracked changes; the action menu opens its editor,
opens a terminal in that directory, or copies its path. Missing checkouts remain
visible with a copy action. Git is only queried in project searches.

```json
"projects": [
  { "title": "Vision main", "path": "~/code/Vision", "editor": "rider" },
  { "title": "Vision staging", "path": "~/code/VisionStaging", "editor": "rider" },
  { "title": "Website", "path": "~/code/ibro.me", "editor": "code", "editorArguments": ["{path}"] }
]
```

Editor arguments are individual arguments, with `{path}` replaced by the exact
checkout path. Terminal opening uses `xdg-terminal-exec`. At most 100 configured
projects and 20 matching results are considered. No repository contents run
during discovery. Git status excludes untracked files to keep searches quick.

## Snippets and templates

Search `snip: review`. Enter copies a snippet, asking for any named inputs first.
The action menu also offers an expanded preview. `{date}` uses the local date
(`yyyy-MM-dd`), `{time}` uses local `HH:mm`, and `{clipboard}` reads current text
only when you copy or preview. Other placeholders, such as `{topic}`, become
required form fields. Values are inserted literally, without recursive expansion.
Paste copied text into your destination with its normal paste shortcut.

```json
"snippets": [
  { "id": "review", "title": "Review prompt", "text": "Review {topic} on {date}: {clipboard}" }
]
```

Up to 1000 snippets and 16 inputs per snippet are supported. Inputs and expanded
text are not added to query/usage history. Clipboard placeholders need `wl-paste`;
copying needs `wl-copy`. The launcher does not synthesize keystrokes or perform
automatic text expansion.

The v3 `invoke` request accepts optional `inputValues` (name/value object).
Actions advertise their required `inputs`; only those names are accepted.
Existing calls without inputs remain valid for actions without input fields.
Preview and command output are displayed as plain text inside the launcher.

## Script commands

Put `*.commanddeck.json` manifests in `~/.config/commanddeck/scripts`, or list
other trusted directories in `scriptDirectories`. Search `script: tests`.
CommandDeck reads metadata during discovery, asks before executing, collects
named inputs, then displays stdout, stderr, and exit status inside the launcher.
Manifests are rechecked at invocation; an edited manifest requires another search.

Example `tests.commanddeck.json` beside an executable `run-tests.sh`:

```json
{
  "title": "Run project tests",
  "description": "Run a named suite in the selected checkout",
  "executable": "./run-tests.sh",
  "arguments": ["{suite}"],
  "inputs": [{ "name": "suite", "defaultValue": "unit" }],
  "workingDirectory": "~/code/my-project",
  "timeoutSeconds": 120
}
```

Make your script executable (`chmod +x run-tests.sh`) and give it a shebang.
Executables can be absolute or relative to the manifest directory. Each input
placeholder occupies a whole argument; values are passed literally as one argv
entry. Your scripts run with your normal environment and permissions, so only
add directories you trust. Discovery never runs them. Invalid manifests are
skipped. Execution defaults to 30 seconds (configurable from 1 to 300), with
64 KiB stdout and 16 KiB stderr limits. Timeout, cancellation, and output overflow
stop the process tree. Output is shown as plain text.

## Clipboard history

The in-launcher shortcut waits for preferences and saved-search ownership to load.
Changing its chord checks effective Hyprland bindings; if inspection fails or the chord
conflicts, the previous preference remains saved. Existing saved searches retain their
shortcut when renamed or edited after an upgrade.

Search `clip:` (or run the query-free **Clipboard History** command) and choose
**Enable capture** to opt in. Consent persists across daemon restarts and is off for
new installations. Capture runs only in the daemon through a supervised native
`ext-data-control` helper; there is no `wl-paste` fallback. **Text** consent retains
UTF-8 plain text up to 32 KiB. **Rich** consent additionally retains HTML, PNG images,
single URLs and local file references as bounded raw offers, classified after receipt
into Text, Links, Images and Files. Start, restart and resume never capture the current
selection; selections marked sensitive or produced by a GNOME cut are skipped.

The browser offers focused search, a **Pinned only** toggle, a kind filter (All/Text/Links/
Images/Files), bounded pages, exact plain-text previews with match navigation, isolated PNG
thumbnails, pin/unpin, keywords, delete, pause/resume, limits and Clear. Pinned entries are
listed first under their own label. Enter or a double-click copies the selected entry;
Ctrl+P pins, Ctrl+E edits keywords, Ctrl+Tab and Ctrl+Shift+Tab switch the kind filter, and
Ctrl+G and Ctrl+Shift+G step through text matches. Up/Down, PageUp/PageDown and
Ctrl+Home/Ctrl+End move the selection; Down on the last loaded entry loads the next page.
File entries show references, never referenced contents; previews never fetch URLs. For a single copied
URL, **Open…** (Ctrl+K) lists the registered desktop handlers; launching from history stays
disabled until the release-acknowledgement and managed-launch gates pass. Escape clears
the query, then resets filters, then returns to the invoking result. These actions do
not alter the current system clipboard except when you explicitly copy an entry again.

```json
"clipboardHistory": { "maximumEntries": 200, "retentionHours": 168 }
```

JSON values seed only the initial policy; afterwards the daemon's stored limits win and
are edited through the browser's Limits dialog. Unpinned entries expire after the
retention period; pins survive until deleted. Expiry checks run at the next known
expiry, with a five-minute maximum interval to recheck clock changes, resume, unpins
and changes to the next expiry. Reading history and inserting new entries also apply
retention. At most half the entry capacity and half
the payload quota can be pinned, reserving room for new copies. History is kept in a
private SQLite file plus a `0700` payload directory beside the catalog, with mode `0600`
files and SQLite secure deletion enabled. It is not included in query/usage history,
assistant context or customization exports.

## Folder navigation and file selections

Type `browse:` to start in your home folder, or `browse: ~/Downloads` to go to a
specific folder. Enter on a directory browses into it without closing the
launcher. The **Up one folder**, **Previous page**, and **Next page** rows navigate.
Typing a name filters within the current folder. Searches inspect at most 50,000
entries in one directory; they do not recursively scan its contents. Explicit
browsing is independent of the background index and its root configuration.

Use **Toggle file selection** in the action menu to collect files or folders.
Indexed file results have the same action and **Browse containing folder**.
The selection bar keeps up to 32 paths across searches and directory changes.
Choose **Copy paths**, **Open all**, click an entry to remove it, or **Clear**.
Selections belong to this launcher connection, survive search-cache expiry, and
are cleared when the palette closes or disconnects. All paths are checked before
opening; failures report how many opened. Copy uses newline-separated paths.

Protocol v3 adds `browseDirectory` and `browseOffset` to search, plus
`files.select` (`resultId`, `enabled`), `files.clear`, and `files.batch`
(`actionId`: `copy-paths` or `open`). Selection requests accept only fresh,
daemon-issued file results; clients cannot submit arbitrary paths.

## Reusable script picker

```sh
printf '%s\n' main staging release | commanddeck pick --prompt 'Switch branch'
```

Filter with the keyboard, move with Up/Down, and press Enter (or click a row).
The original selected value is written to stdout with a newline. Escape or
Cancel returns exit status 1 and no stdout; success returns 0. No choice is
executed by CommandDeck. You can use command substitution in your own script:

```sh
branch=$(git branch --format='%(refname:short)' | commanddeck pick --prompt 'Branch') || exit
printf 'Selected: %s\n' "$branch"
```

`--label-tab` accepts `value<TAB>display label` lines, returning only the value.
`--index` returns the original zero-based input index, which distinguishes
identical values. UTF-8 input is bounded to 1 MiB, 10,000 choices, and 4096
characters per line. Choices live in a private temporary directory that is
removed on completion; picker logs never share stdout with the selected value.
The picker uses its own Quickshell window and does not need the CommandDeck
daemon. Build/publish and the local installer include its QML assets beside the
binary. It requires a running Wayland session and `qs`.

## User services and timers

`svc:` (also `unit:` / `service:`) discovers loaded user `.service` and `.timer`
units, including inactive units still known to systemd. Search lists names and
state only; it does not read the journal or change a unit.

- **Show recent logs** explicitly reads up to 100 journal entries. Output is
  bounded to 16 KiB and shown as literal text; truncation is disclosed.
- **Show current status / timer schedule** refreshes state and displays the
  timer's next realtime/monotonic trigger, last trigger, and associated unit
  when systemd supplies those properties.
- **Start / Stop / Restart user unit** each requires confirmation naming the
  exact unit. CommandDeck rechecks its identity, then requests the operation
  without waiting for the job to finish. The launcher stays open and refreshes;
  use current status to verify completion. Stopping CommandDeck's own service
  will disconnect the launcher. Stopping a timer does not stop its already
  running service.
- **Edit unit with preferred editor** opens systemd's current `FragmentPath`
  using the editor chosen in Settings. It does not invoke sudo, create an
  override, or modify a unit automatically. Packaged/read-only unit files may
  not be writable. After saving, run `systemctl --user daemon-reload` before
  expecting the manager to use the changed definition.

All commands target the user manager with literal argument vectors, never shell
strings. Globs and invalid unit names are rejected. Search and action processes
have timeout, output, and memory bounds. Logs are on-demand action output, not
indexed content. These controls neither enable/disable boot activation nor act
on system-wide services.

## Desktop controls

Type what you want to change: `volume`, `mute`, `next track`, `wifi`, or
`bluetooth`. Matching desktop commands appear beside your other results, and
`ctl:` (also `control:`) lists all of them. Enter runs the command; on success
the launcher closes like other launches, as set by **Hide after opening an
item**. A failure, such as no media player for `playerctl`, stays visible with
the tool's error output.

| Commands | Literal argv |
|---|---|
| Volume Up / Volume Down | `wpctl set-volume -l 1.0 @DEFAULT_AUDIO_SINK@ 5%+` / `5%-` |
| Toggle Mute | `wpctl set-mute @DEFAULT_AUDIO_SINK@ toggle` |
| Set Volume to 0%, 25%, 50%, 75%, 100% | `wpctl set-volume @DEFAULT_AUDIO_SINK@ 50%` |
| Toggle Microphone Mute | `wpctl set-mute @DEFAULT_AUDIO_SOURCE@ toggle` |
| Play / Pause Media, Next Track, Previous Track | `playerctl play-pause`, `next`, `previous` |
| Toggle Wi-Fi / Toggle Bluetooth | `rfkill toggle wlan` / `rfkill toggle bluetooth` |

Each command is one literal argument vector, never a shell string. A command
whose tool is not on the daemon's `PATH` is left out of the results instead of
showing an error. Wi-Fi and Bluetooth use the rfkill soft block, the same switch
that Omarchy's Bluetooth power script uses, so NetworkManager, iwd, and BlueZ
follow it. Search needs at least two characters and does no work on bare Home.
Turn the **Desktop controls** source off in Settings to remove these rows.

## Verification

`dotnet tests/workflows-smoke.cs /path/to/commanddeck` runs all six workflows
against a real daemon and a piped picker process. It uses temporary databases,
project files, fake application/copy tools, and the specs' synthetic Wayland server,
so it does not access your clipboard or open your applications. It requires Linux,
the pinned .NET SDK, and `libwayland-server.so.0`; it builds the existing specs to
reuse their isolated compositor fixture. It also works with the Native AOT binary.
Invocations forward the reference supplied by each search result; after an action
changes a row, the helper uses the refreshed row and reference.
The executable spec suite covers input limits, literal expansion, timeouts,
retention, paging, and selection ownership. QML tests cover the input form,
file-selection controls, picker keyboard behavior, and launcher protocol wiring.
