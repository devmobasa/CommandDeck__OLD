# Security and privacy

CommandDeck is a local user application. Its daemon handles sensitive local
paths and can execute user-authorized actions. It does not provide an isolation
boundary against every process already running as the same user.

## Defaults and storage

- No telemetry or automatic cloud synchronization.
- Default filename/path indexing covers home, including hidden files. Content
  indexing is configured per root; protected credential paths and deny patterns
  take precedence over inclusions.
- Query history is off. Accepted-invocation usage learning is on and independently
  configurable/resettable. Copy/preview/highlight do not train usage.
- Clipboard capture is off until enabled. Its bounded private store persists
  captured entries; pause/delete/clear and retention controls are available.
  Clipboard schema 4 removes the unused FTS index, which could retain terms
  from deleted entries. Opening schema 3 migrates it with secure deletion;
  search and payload retention remain unchanged. Older binaries cannot reopen
  schema 4; downgrading requires a compatible pre-upgrade backup.
- HEY results use transient memory caching. HEY owns its credential and login.
- Keeping the last search requires the daemon to classify the current query as
  nonprivate. Private provider syntax, mixed queries and aliases are excluded;
  an unfinished or unclassified search is not kept.
- Clipboard and HEY queries/results are excluded from assistant tools, AI context,
  query/learning history, saved searches, suggestions, and Home.
- Direct AI and the assistant are disabled until enabled. Provider configuration
  stores credential references, and assistant credentials use owned restricted files.

See [configuration](configuration.md) for actual options and defaults.
Settings can add roots or disable configuration roots through the local daemon.
The SQLite overlay does not bypass configuration validation, `neverIndex`, protected
paths, symlink or mount rules. Export contains these personal paths. Removing a
root commits its preference, policy-aware Search and Home purge, and full reindex
request together. Surviving overlapping roots retain permitted coverage. Only the
catalog owner publishes the background scan; restart resumes a pending request.
Reset removes additions and re-enables configuration roots. Adding a root does
not grant assistant or extension filesystem access.

User-selected roots, commands, editors, remote services, and unrestricted assistant
access remain deliberate trust decisions.

## Action and process boundaries

The frontend requests typed daemon-authored actions. Executables and arguments
use literal vectors, not arbitrary renderer-supplied shell text. Result/session
identity and confirmation are checked at admission; a displayed label is not
authority. Command/script definitions remain trusted user configuration.

A `>` Run row is built by the daemon from the typed text: it splits the text
into a literal argument vector without a shell, and offers the row only when the
program is on `PATH` or is an absolute executable path. The row's ID follows that
vector, the frontend invokes it by result reference without arguments, and the
launch uses the same detached path as applications. Assistant tools cannot
invoke it.

Omarchy menu rows can carry `when` and `checked` bash conditions from Omarchy's
own menu files and your menu extension file, the same trust as the row's action.
With Omarchy menu state on (Settings > Search; until chosen there it follows
`search.omarchyMenuState`, which is on by default), the conditions of the first
Omarchy rows a search returns run as one login bash batch: each in its own
subshell with stdin from `/dev/null` and output discarded, the batch limited to
five seconds, 256 MiB and bounded output, and cancelled on shutdown. The query is
never part of the batch. Answers are cached for twenty seconds; they only dim a
row or mark it checked.

An Omarchy row's action runs as a login shell in the user session, not in the
daemon's sandbox: `systemd-run --user --wait` starts it as a transient service,
as from Omarchy's own menu. In the daemon, `NoNewPrivileges` would block `sudo`
and `pkexec`, the root file system is read-only, files would be created with
`UMask=0077`, and a daemon restart would kill the command. The daemon waits up
to five minutes for its exit status and error output, which are the action's
result, and the service manager stops a command still running a few seconds
after that. Apps that Omarchy's launchers start through `uwsm-app`
run in their own units and keep running.

Process signals (`kill:`) are limited to processes whose real, effective, and
saved user IDs are the user's own, and never reach CommandDeck itself. Each
signal needs a confirmation that names the PID and process. The daemon pins the
process with a pidfd and checks its start time and owner again before it sends
SIGTERM or SIGKILL, so a reused PID is not signalled. Assistant tools see the same
confirmation requirement. A confirmed signal can still lose that process's
unsaved work.

Extensions are disabled by default and execute in separate restricted processes.
Package features negotiate compatibility; permission declarations and grants
control effects. Files, HTTP, credentials, clipboard, and URL actions are brokered
by the host. Generic extensions do not receive the host home or network directly.

AI request preflight binds the displayed bytes and destination to transmission.
Assistant tools are mediated by the daemon. Levels below unrestricted use a
Bubblewrap boundary; unrestricted intentionally grants much broader access.
The package helper is a separate Polkit-protected service, not an automatic part
of the user-local installer.

## Limitations and reporting

Native AOT, tests, and subprocess limits reduce specific failure modes; they do
not certify an untrusted package, model, parser, or configured executable. Review
extension grants and remote destinations before enabling them. Installed sandbox,
Polkit, clipboard, and compositor behavior require the appropriate live environment.

Avoid attaching credentials, prompts, mail, clipboard text, or unsanitized logs to
public issues. Use the repository's GitHub Security advisory reporting when it is
enabled. If unavailable, request a private reporting channel without including
exploit details or secrets in the public issue.
[SECURITY.md](../SECURITY.md) describes what to include in a report.

The [threat model](threat-model.md) explains the main trust boundaries and residual
risks. [Permissions](permissions.md) includes a generated capability inventory.
