# HEY mail search

CommandDeck searches HEY mail through Basecamp's
[HEY CLI](https://github.com/basecamp/hey-cli). The CLI is the engine and
owns the sign-in; CommandDeck adds a `hey:` search and opens the thread you
pick. HEY's own Omarchy bar plugin stays responsible for new-mail awareness,
so CommandDeck shows no unread counts and adds nothing to Home.

## Using it

Type `hey:` (or `mail:`, or `@hey`) and then words. Results appear in HEY's
own order, one row per thread: the subject, then the sender, a short date,
and the matching snippet. Enter opens the thread. The action panel (Ctrl+K) also
offers Read Thread, which shows the whole thread (`hey thread read --markdown`)
in the launcher's output dialog as plain text, so no link or image in the mail
is ever loaded; a thread longer than 64 KiB ends with a pointer to HEY. It also
offers Copy Link.

Settings → Search → HEY mail chooses where threads open, and saves at once:

- **HEY web app** (default): `omarchy-launch-webapp` opens the thread's page,
  the way Omarchy's own HEY launcher does; without it, the browser opens it.
- **HEY terminal app**: a running `hey tui --instance omarchy` takes the thread
  over its socket (`--remote`) and its `org.omarchy.hey` window is raised;
  otherwise `omarchy-launch-or-focus-tui` opens a new TUI on the thread, or a
  plain terminal does when that launcher is absent. The `--remote` call runs
  with the same clean, bounded environment as searches.

The preference is `heyOpenTarget` (`web` or `terminal`); an unknown stored
value opens threads in the web app.

Words typed without a filter (three characters or more) end the list with one
row, **Search HEY for “…”**, whenever the HEY CLI is installed and the HEY source
is enabled. It runs nothing until chosen; Enter moves the same words into
`hey:`. It follows every ranked row, takes no Top Match or first-page slot, and
stays behind the web fallback when nothing else matches.

An empty `hey:` lists HEY's boxes and pages instead of a blank list: Imbox,
Reply Later, Set Aside, The Feed, Paper Trail, and Bubbled Up, then The
Screener, Write an Email, and Open HEY. No row carries a count. Enter on a box
replaces the query with `box:<name>` under the HEY chip and lists the box's
newest threads (unseen ones marked); words typed after it filter that listing
without another request. The Screener and Open HEY follow the setting above
(`hey tui --screener` in the terminal app); Write an Email always opens HEY's
web compose page, because the terminal app has no compose destination.

Refinements narrow the search. Each takes one value:

| Refinement | Meaning | Values |
|---|---|---|
| `from:` | Sender name or address | `from:jane`, `from:"Jane Cooper"` |
| `to:` | Recipient name or address | `to:billing@example.com` |
| `subject:` | Words in the subject | `subject:invoice` |
| `label:` | A HEY label | `label:Receipts` |
| `in:` | Where to look | `imbox`, `feed`, `papertrail`, `trash` |
| `has:` | An attachment kind | `any`, `images`, `pdfs` (`pdf`), `invites`, `documents` (`docs`), `spreadsheets` (`sheets`), `presentations` (`slides`), `media`, `zip` |
| `date:` | How far back | `7d` (`week`), `30d` (`month`), `90d`, `year` |
| `box:` | List one box instead of searching | `imbox`, `reply-later`, `set-aside`, `feed`, `paper-trail`, `bubbled-up` |

A quoted phrase must appear as written, and `-word` excludes a word. An
unknown `in:`, `has:`, or `date:` value is explained before anything is sent.
Unknown, excluded, or conflicting refinements are rejected. `box:` listings
accept local words but cannot be combined with search refinements; use `in:`
to search supported boxes.
While the last word is a refinement with an unfinished value (`in:`, `has:p`,
`date:`, `box:re`), the rows are its values instead of a search, and nothing
runs. Enter or Tab puts the value into the query, followed by a space, so typing
goes on; Tab uses the row's `completionText` rather than its title. A finished
value, or a space after it, searches.

When HEY cannot answer, the one row says why and what to do next:

| State | Row |
|---|---|
| hey-cli is not installed | Install the HEY CLI: runs `omarchy-mise-install github:basecamp/hey-cli hey` in Omarchy's presented terminal, or opens the install guide |
| Signed out | Sign in to HEY: runs `hey auth login` in a terminal; the browser finishes the sign-in |
| Offline, rate limited, too slow | The reason, and Search Again (Enter) to rerun the same query |
| Refused | HEY's reason, for example an invalid value |
| No matches | Try fewer words or a refinement |
| More than a page | The first page, then a hint to narrow the search |
| A box with older threads | The newest threads, then a row that opens HEY |

## Triage

Thread rows that HEY lists with a box item offer reversible changes in the
action panel, each with Undo in the strip under the results (Ctrl+Z works
while it shows):

| Change | Command | Undo |
|---|---|---|
| Mark Seen / Mark Unseen | `hey seen` / `hey unseen` | the other one |
| Reply Later | `hey move <id> --to=laterbox` | `hey move <id> --to=<box it came from>` |
| Set Aside | `hey move <id> --to=asidebox` | `hey move <id> --to=<box it came from>` |
| Bubble Up Tomorrow | `hey bubble up <id> --tomorrow` | `hey bubble pop <id>` |

A box listing knows each thread's seen state and box, so it offers the one mark
that applies plus Reply Later and Set Aside (except in those boxes). A search
match can offer both marks and Bubble Up, but offers Undo only when the prior
seen or bubble-up state is known. An existing bubble-up is never replaced.
A match without a box item offers none. Nothing sends, trashes,
or reports mail. A change refreshes the list and keeps the launcher open. Each
undo is a hidden `undo:<id>` action. A successful change returns a separate,
owner-scoped `undoInvocationReference` for that action, so Undo survives a row
refresh and needs no new request type. Its lifetime and capacity limits match
other invocation snapshots, and disconnect revokes it. A timed-out or unreadable
change may still have landed, and says so; its cached listing is invalidated.

## How it runs

`HeyClient` is the only boundary to the CLI. It resolves the binary once per
call: `COMMANDDECK_HEY` wins when set (a full path to the binary, or `off` to
turn the integration off; the spec harness sets `off` so a plain query gets the
same rows on every machine), then a native `hey` on `PATH`, then the binary mise
installed (`~/.local/share/mise/installs/github-basecamp-hey-cli/latest/hey`),
and the mise wrapper on `PATH` is the fallback, because it runs `mise use -g` before
every call (about 70 ms, against about 15 ms for the binary).

Each call runs a fixed argv from `/`, so no repository-local
`.hey/config.json` applies. Refinements are single `--name=value` arguments
and the text follows `--`. The environment is cleared except `PATH`, the XDG
directories, `HOME`, and the session bus address (hey keeps its token in the
keyring), plus `HEY_NONINTERACTIVE=1` and `NO_COLOR=1`. Output is capped at
4 MiB, reads time out after 4.5 s, and the address space is capped at 2 GiB:
the Go runtime reserves its heap ranges up front, and hey 1.6 fails at
512 MiB.

The provider runs only when a query asks for HEY and the HEY source is
enabled in Settings → Search sources. It waits 250 ms for typing to pause, so
a replaced search never starts a process, and keeps successful listings in
memory for 60 s (a box listing for 20 s, with no typing pause). Failures are
never cached, so a sign-in takes effect on the next search. A HEY-scoped search has a 5 s provider deadline instead of the
local one. Rows keep HEY's order: they skip field scoring and Top Matches.

Omarchy's launchers (`omarchy-launch-webapp`, `omarchy-launch-or-focus-tui`, the
presented terminal) run as new user services (`uwsm-app -t service`), never
inside the daemon. The daemon is sandboxed with a read-only root,
`NoNewPrivileges`, and `UMask=0077`, and those launchers start their program in a
`uwsm-app` scope that keeps the caller's mounts and restrictions: a browser
started there would inherit that sandbox instead of the session's.

Box rows and Search Again use the `apply-query` action, which the launcher
handles itself: it replaces the query and its filter chip, searches, and
returns focus to the search field. The daemon refuses to execute it, and the
assistant can never invoke it.

## Privacy

HEY mail is a private source (`PrivateSources`), like clipboard history. Its
rows and queries are never logged, kept in query or learning history, saved
as searches, offered as suggestions, shown on Home, attached to AI requests,
or given to the assistant or its tools. Rows cannot be customized, so no
favorite, alias, or hidden item stores a subject. Mail text is shown inert:
control and format characters are removed and lengths are bounded. A thread
always opens HEY's own page, `https://app.hey.com/topics/<id>`; a host named in a
HEY payload is never used, and the executor checks the link again. CommandDeck
never reads or stores a HEY credential. Settings shows no Home switch for
HEY, and a save cannot turn one on.
