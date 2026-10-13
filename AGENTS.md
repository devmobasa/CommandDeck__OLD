# Working on CommandDeck

This repository is self-contained. Begin with [README.md](README.md),
[CONTRIBUTING.md](CONTRIBUTING.md), and [the documentation index](docs/README.md).

## Scope and workflow

- Carry implementation requests through the change and relevant verification.
  Make routine, reversible choices from the existing code; ask when missing
  information materially changes the result.
- Keep reviews and diagnosis read-only unless a fix is requested. Commit, push,
  merge, installation, and deployment require authorization for the current task.
- Inspect Git status before editing and preserve unrelated changes. Do not use
  historical test counts as fresh verification.
- Read the affected implementation and tests. Use optional tools for the actual
  question; their absence does not block ordinary source work.
- Report the result, checks actually run, skips, and blockers plainly.

## Navigate and preserve boundaries

- [Architecture](docs/architecture.md) maps projects and ownership.
- For search/ranking, read [the search guide](docs/search-and-ranking.md).
  Preserve stable IDs, source budgets, and privacy gates.
- For AI or permissions, read [AI integration](docs/ai.md),
  [security](docs/security.md), and [the threat model](docs/threat-model.md).
  Prompt wording never grants authority beyond daemon-enforced permissions.
- For extensions, read [the authoring index](docs/extensions/README.md).
  Children are isolated processes; features are not permission grants.
- For tests/generated artifacts, use [the testing guide](docs/testing.md).
  `global.json` and `Directory.Packages.props` own SDK/package versions.
- For installation, use [the installation guide](docs/install.md).
  Offscreen tests do not establish installed Wayland behavior.

## Source and scripts

- Keep handwritten source around 500 lines, with a maximum of 600. Split by
  responsibility before exceeding that limit; generated files are exempt.
  Refactor oversized files when the task includes them, not during unrelated work.
- Separate validation, setup, execution, state checks, and mapping with blank
  lines. Keep closely related statements together.
- Assign or deserialize first, then use a separate braced null check that throws.
  Keep the assignment and guard together, followed by a blank line.
- Preserve logical spacing when formatting and inspect the resulting diff.
- Write new automation scripts as C# .NET file-based apps (`.cs`). Existing
  shell/Python entry points remain supported. If modifying a Python script,
  migrate it to C# and update callers in the same change.

## Keyboard-first, mouse-complete UI

- Focus search on searchable surfaces, the first editable field in forms, and
  Cancel in destructive confirmations. The primary task needs no preparatory click.
- Keep single-field search ready after clicks, copy, regeneration, pagination,
  refresh, and asynchronous replacement. Typing from results must reach search
  while preserving editing shortcuts and input methods.
- Respect deliberate Tab/Shift+Tab navigation and button-group arrows. Enter/Space
  activates the focused control; asynchronous updates preserve its identity.
  Mouse activation must not unnecessarily take focus from search.
- Scope input to the active surface; never steal from another editor or dialog.
- Keep every control reachable in a logical tab order, with visible focus.
  Support list arrows, consistent keyboard hints, and Escape to clear/dismiss.
- Trap focus in modals and restore it to the invoking control on dismissal.
- Test open, type, navigate, activate, dismiss, and reopen with real key events.
  Assert initial/restored focus without correcting it in setup. Verify mouse
  activation followed by immediate typing, pagination, replacement, and Back.
- For shared focus logic, identify and verify every affected surface. Report
  unverified surfaces explicitly.

## Verification

Add tests for every new behavior and bug fix. Regressions should reproduce the
user-visible failure through the closest existing public seam and fail without
the fix. Explain a missing credible seam instead of adding shallow assertions.

Run checks that exercise the changed behavior. Broaden or repeat them for new
changes, failures, or unresolved concerns. Documentation-only edits need source,
command, and link checks. Keep UI and security regression coverage intact.
