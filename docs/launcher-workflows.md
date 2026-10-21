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
When the selected result's primary action takes one to three inputs and needs no
confirmation, they appear as pills after the query: Tab moves into them, Tab and
Shift+Tab move between them, Escape returns to the query, and Enter runs the action
with their values (or moves to the first empty one). Other actions keep the form.
The action menu also offers an expanded preview. Paste copied text into your
destination with its normal paste shortcut.

```json
"snippets": [
  { "id": "review", "title": "Review prompt",
    "text": "Review {topic default=\"code\"} by {date offset=\"+2d\"}: {clipboard | trim}" }
]
```

A placeholder is a name, then optional `key=value` options, then optional
`| modifier` steps: `{date format="dd.MM.yyyy" offset="+1d"}`, `{topic | uppercase}`.

| Placeholder | Inserts | Options |
|---|---|---|
| `{date}` / `{time}` | Local `yyyy-MM-dd` / `HH:mm` | `format`, `offset`, `locale` |
| `{datetime}` / `{day}` | Local `yyyy-MM-dd HH:mm` / weekday name (`dddd`) | `format`, `offset`, `locale` |
| `{uuid}` | A new random (version 4) UUID for each occurrence | None |
| `{clipboard}` | Current clipboard text, read only when you copy or preview | `offset=N` (0–999) |
| Any other, such as `{topic}` | A required form field | `default` prefills the field |

| Modifier | Effect |
|---|---|
| `uppercase`, `lowercase` | Culture-invariant case mapping |
| `trim` | Removes leading and trailing whitespace |
| `percent-encode` | Percent-encodes UTF-8, keeping only RFC 3986 unreserved characters |
| `json-stringify` | A JSON string literal, quotes included |

- `format` is a .NET custom date and time format string. Output uses the invariant
  culture unless `locale` names a culture from the system ICU data, such as `"de-DE"`.
- `offset` lists signed steps applied in order, such as `"+1d -2h"`. Units are `m`
  (minutes), `h` (hours), `d` (days), `M` (months) and `y` (years). Days, months and
  years keep the local wall-clock time across daylight-saving changes.
- Values can be bare (`offset=+1d`) or quoted with `"…"` or `'…'`. Inside quotes a
  backslash makes the next character literal. A bare value ends at a space, `}` or
  `|`; a quote, backslash or `{` inside one leaves the placeholder literal.
  Modifiers apply from left to right.
- A repeated name, such as `{topic}` and `{topic | uppercase}`, is one form field.
  Values are inserted literally, without recursive expansion.
- Braces that do not form a complete placeholder on one line stay literal, so JSON
  and code pass through and `{{date}}` becomes `{2026-09-06}`. A line break inside
  a placeholder also leaves it literal.
- An unknown option or modifier, an invalid format, offset or locale, or two
  defaults for one input stop configuration loading. The message names the snippet
  and the placeholder.

`{clipboard offset=N}` with N of 1 or more inserts the plain-text history entry N
places before the newest one, ordered by last copy; pins do not change the order.
Image, file and rich entries are skipped. It needs clipboard capture consent and
fails while consent is off. A snippet that reads history is a private clipboard
row: it is never shown on Home or in suggestions, attached to AI requests, or
offered to assistant tools.

Up to 1000 snippets and 16 inputs per snippet are supported. Expanded text is
limited to 131,072 characters (UTF-16 code units). Inputs and expanded text are not added to query/usage history.
Clipboard placeholders need `wl-paste`; copying needs `wl-copy`. The launcher does
not synthesize keystrokes or perform automatic text expansion.

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

## Run a program

Type `>` and a program with its arguments, for example `> htop` or
`> notify-send "Build done"`. When the program is on `PATH`, or is an absolute
path to an executable file, the first row is **Run notify-send 'Build done'**.
Press Enter to run it detached, the way applications start; the launcher then
closes as set by **Hide after opening an item**. Ctrl+K lists **Run in Terminal**
and **Copy Command**; Ctrl+Enter and Ctrl+Shift+C run them directly. An unknown
program, a relative path such as `./build.sh`, and `~/bin/tool` get no Run row,
so Enter never runs typed text without the row.

No shell reads the text. Whitespace separates arguments, and double quotes around
a whole argument keep its spaces. `~`, `$HOME`, globs, pipes, and redirection are
passed literally; use `> sh -c "make 2>&1 | tee build.log"` when you need them.
See [Run a program](search-and-ranking.md#run-a-program) for the exact rules.

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
Ctrl+P pins, Ctrl+S starts a sequence at the selected entry (see below), Ctrl+E edits
keywords, Ctrl+Tab and Ctrl+Shift+Tab switch the kind filter, and
Ctrl+G and Ctrl+Shift+G step through text matches. Up/Down, PageUp/PageDown and
Ctrl+Home/Ctrl+End move the selection; Down on the last loaded entry loads the next page.
File entries show references, never referenced contents; previews never fetch URLs. For a single copied
URL, **Open…** (Ctrl+K) lists the registered desktop handlers; launching from history stays
disabled until the release-acknowledgement and managed-launch gates pass. Escape clears
the query, then resets filters, then returns to the invoking result. These actions do
not alter the current system clipboard except when you explicitly copy an entry again.

To paste several entries in order, select the first one and choose **Start sequence**
(Ctrl+S in the browser, or the action on a `clip:` row). Each run of the query-free
**Copy next clipboard entry** command then copies the next entry by last-copy time,
oldest first, and reports how many remain queued. **Restart clipboard sequence**
instead begins with the next entry you copy. Entries copied later join the end; a
copied entry moves to the newest position but is not copied twice; deleted or expired
entries are skipped, and a failed copy does not advance. Both commands need capture
consent. They are private clipboard rows: they cannot be bookmarked, never appear on
Home, and are not recorded, suggested, attached to AI requests or offered to assistant
tools. The position lives only in daemon memory and is never exported; Clear, turning
capture off, or restarting the daemon ends it, while pausing keeps it.

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

Type what you want to change: `volume`, `mute`, `next track`, `wifi`,
`bluetooth`, `color`, or `left half`. Matching desktop commands appear beside your other results, and
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

### Color picker

Type `color` or `pick color` and press Enter on **Pick Color**. The launcher
closes, and `hyprpicker --autocopy` starts so you can click any pixel; the color
is copied to the clipboard as hex. Press Escape in the picker to cancel.
This action closes the launcher even when **Hide after opening an item** is
off. If dispatch fails, the launcher stays open to show the error.
hyprpicker freezes the screen when it starts, so it must not start while the
launcher is still visible. CommandDeck runs this literal argv, which returns
once a one-shot user timer exists:

```text
systemd-run --user --quiet --collect --on-active=300ms --timer-property=AccuracySec=10ms -- hyprpicker --autocopy
```

The timer starts the picker 300 ms later in your user session. Copying needs
`wl-copy`, as for hyprpicker itself. If `hyprpicker` or `systemd-run` is
missing, the **Pick Color** row stays visible as an inert row that names the
tool to install.

### Window commands

Window commands act on the window that was focused when you opened the
launcher. Type `left half`, `center third`, `maximize`, `tile`, `fullscreen`,
`next monitor`, or `workspace 3`. Each row names its window in the subtitle.

| Commands | Effect |
|---|---|
| Left Half, Right Half, Top Half, Bottom Half | Half of the monitor's work area |
| First Third, Center Third, Last Third | One column of three |
| First Two Thirds, Last Two Thirds | Two columns of three |
| Center Window | Keeps the window's size and centers it |
| Maximize Window / Almost Maximize Window | The whole work area / 90% of it, centered |
| Toggle Floating / Tile Window | Floats or tiles the window |
| Toggle Fullscreen | Hyprland fullscreen |
| Move Window to Next / Previous Monitor | The neighbouring enabled monitor, in monitor ID order |
| Move Window to Workspace 1–10 | Moves the window and follows it |

The launcher is a layer-shell overlay, so opening it does not change
Hyprland's active window. When a window command matches your search, the daemon
reads `hyprctl -j activewindow` and binds that window's literal address to the
row. At invocation it checks that the address is still a client, then dispatches
only to that address. It never acts on whatever has focus at dispatch time. A
closed window reports an error; search again.

Hyprland tiles windows, so a placement floats the window first and leaves
fullscreen if needed. **Tile Window** returns it to the layout. Placements use
the work area of the window's monitor from `hyprctl -j monitors`: the logical
size after scale and rotation, minus the space that bars reserve. Halves and
thirds share edges, so adjacent placements do not overlap. Gaps are not added.
Every dispatch must answer `ok`. A Lua warning stops the sequence and is shown
as a failure, even when `hyprctl` exits with status zero. Without `hyprctl`, a
focused window, or a second monitor, the affected rows are not shown.

## Kill process

Type `kill:` (also `proc:` or `process:`) to list your own processes, largest
resident memory first, with PID, memory, and CPU time. Add a name or an exact PID
to filter: `kill: firefox` or `kill: 4242`. In plain search, `kill`, `force
quit`, or `kill firefox` offers one **Kill Process** row that opens this list;
ordinary typing never reads `/proc`.

The list shows at most 100 rows from at most 8,192 process entries. It contains
only processes whose real, effective, and saved user IDs are all yours, so other
users' processes and setuid programs such as `sudo` are left out. Kernel threads,
zombies, and the CommandDeck daemon are also left out, and so is an installed
daemon's bridge, because it runs the same executable. Names are read from `/proc/<pid>/stat`
with control characters replaced; full command lines are not read.

The action menu offers **Terminate PID …** (SIGTERM, the primary action),
**Force kill PID …** (SIGKILL), and **Copy PID**. Both signals ask for
confirmation that names the PID and the process, for example
`Terminate PID 4242 “firefox”?`. Each row carries the PID, its start time from
field 22 of `/proc/<pid>/stat`, and your user ID. On confirmation, CommandDeck
pins the process that holds the PID with a pidfd, then reads `/proc` again. The
start time must match, and the process must still be yours, live, and not
CommandDeck. Only then is the signal sent through the pidfd. A PID that was
reused after the search fails the check, so nothing is signalled; search again.
A signal is not recorded in usage history. On success the launcher closes like
other actions.

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

## Calculator

Type `100 * 5` to show `500` above normal results, then press Enter to copy it.
Type `days until 11 sep` for an offline date count, or `weeks until 11 sep` for
weeks and remaining days. The subtitle shows the resolved dates. Complete
conversions such as `5 km in miles` and `105F in C` also work inline. Currency
conversions such as `100 usd to eur` need cached exchange rates.

Settings > Search > **Inline calculator** turns bare calculations on or off.
Use `=` for the full expression grammar, including constants and functions,
even when inline calculation is off. See the
[calculator reference](search-and-ranking.md#calculator-and-date-counts)
for the grammar, `g` and `in` rules, precision, and date-count rules.
