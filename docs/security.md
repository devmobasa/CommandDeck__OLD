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
- Clipboard and HEY queries/results are excluded from assistant tools, AI context,
  query/learning history, saved searches, suggestions, and Home.
- Direct AI and the assistant are disabled until enabled. Provider configuration
  stores credential references, and assistant credentials use owned restricted files.

See [configuration](configuration.md) for actual options and defaults.
User-selected roots, commands, editors, remote services, and unrestricted assistant
access remain deliberate trust decisions.

## Action and process boundaries

The frontend requests typed daemon-authored actions. Executables and arguments
use literal vectors, not arbitrary renderer-supplied shell text. Result/session
identity and confirmation are checked at admission; a displayed label is not
authority. Command/script definitions remain trusted user configuration.

Omarchy menu rows can carry `when` and `checked` bash conditions from Omarchy's
own menu files and your menu extension file, the same trust as the row's action.
With Omarchy menu state on (Settings > Search; until chosen there it follows
`search.omarchyMenuState`, which is on by default), the conditions of the first
Omarchy rows a search returns run as one login bash batch: each in its own
subshell with stdin from `/dev/null` and output discarded, the batch limited to
five seconds, 256 MiB and bounded output, and cancelled on shutdown. The query is
never part of the batch. Answers are cached for twenty seconds; they only dim a
row or mark it checked.

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
