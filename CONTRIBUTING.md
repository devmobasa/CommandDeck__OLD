# Contributing to CommandDeck

Read [AGENTS.md](AGENTS.md) and [the architecture guide](docs/architecture.md).
Use the SDK pinned by `global.json` and packages pinned by
`Directory.Packages.props` and `packages.lock.json`.

## Build and test

```bash
dotnet restore CommandDeck.slnx --locked-mode
./scripts/build.sh
./scripts/test.sh
dotnet format CommandDeck.slnx --verify-no-changes --no-restore
```

Backend tests are executable specifications, not `dotnet test` projects:

```bash
dotnet run --project tests/CommandDeck.Specs -c Release -- --list
dotnet run --project tests/CommandDeck.Specs -c Release -- --filter "query parser"
dotnet run --project tests/CommandDeck.Specs -c Release -- --category Core
```

Choose the smallest gate that covers your change. The
[testing guide](docs/testing.md) documents prerequisites and reports:

| Change | Gate |
|---|---|
| Documentation | Verify source claims, commands, and local links; `git diff --check` |
| Backend | Relevant executable specs, then `./scripts/test.sh` as needed |
| QML or focus behavior | `dotnet scripts/validate.cs desktop` |
| Extension protocol, SDK, or host | `dotnet scripts/validate.cs extensions` |
| Native AOT or native lifetime | `dotnet scripts/validate.cs aot` |
| Backend, extensions, and Native AOT together | `dotnet scripts/validate.cs linux` |
| Broad changes | `dotnet scripts/validate.cs all` |

`partial` permits explicit skips for unavailable desktop tools. `desktop` requires
Omarchy, Quickshell, Qt 6 lint/event tools, and no skipped backend fixtures.
Installed Wayland, Polkit, upgrade, and rollback acceptance need their own
environments; offscreen suites do not establish those outcomes.

The GitHub workflow runs the `linux` profile and a separate QML job. The Linux
profile runs the common repository checks once, followed by extension isolation
and Native AOT checks. It observes hosted latency while enforcing other quality
budgets; use the default local quality gate for enforced latency on an appropriate
machine.

Automatic validation runs on pushes to `main` and on PRs ready for review,
including PRs targeting another feature branch. Draft PRs defer validation until
they become ready; manual dispatch remains available. Verify the finished batch
locally before pushing once. Do not push intermediate commits to use hosted CI
as a test loop; reproduce hosted failures locally before the next batch push.

## Generated references

After changing protocol or filter contracts:

```bash
./scripts/generate-protocol-contract.sh
```

After changing configuration options or capability descriptions:

```bash
./scripts/generate-reference-docs.sh
```

Include generated changes with the owning source change. Existing specs check
the protocol Markdown, QML constants, configuration schema, option inventory,
and capability table against their C# owners.

## Gallery screenshots

The README and [gallery](docs/gallery.md) images are rendered from the shared
QML with demonstration data. After a visible UI change, re-render the affected
scenes and review the images before committing them:

```bash
dotnet scripts/capture-gallery.cs desktop search
```

The [capture notes](docs/screenshots/README.md) list each scene's images and
explain how the offscreen renders are isolated.

## Changes and reports

Keep fixes focused, preserve unrelated work, and add regression coverage through
the closest public seam. For UI changes, exercise real keyboard events and the
mouse path, including typing immediately after clicking a result or action.

In a pull request, explain the problem, the resulting behavior, and checks you
actually ran. Name skipped prerequisites and unverified desktop paths. Avoid
personal paths, private data, credentials, and generated build reports in commits.

For a bug report, include the app version, relevant dependency versions,
reproduction steps, and sanitized `commanddeck doctor` output. Review output
before attaching it; paths and local configuration may be personal. Report
vulnerabilities privately as described in [SECURITY.md](SECURITY.md).
