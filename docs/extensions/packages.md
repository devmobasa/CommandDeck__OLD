# Local extension packages

Extension storage has its own per-installation revision in `extension_storage`.
Data writes do not change registry authority or invalidate Sources settings saves.
Existing registry data migrates atomically on startup; downgrading to an older
preview daemon after this migration is unsupported.

The current worktree supports inert discovery and experimental local C# commands
with native List/Detail/Form views and no-view completion. See the
[author workflow](authoring.md) to build the examples. Installation does not
run the package, build hooks, or settings code. These commands operate through the
running daemon; add `--socket PATH` when using a separate development daemon.

Use **Settings → Search → Manage extensions** for package search, inspection,
archive install/update, configuration, permissions, enable/disable, rollback,
and removal. Inspect an archive and review
its requested permissions before installing; enable new packages explicitly.
The UI sends daemon-validated configuration for declared non-secret settings.
The CLI also accepts non-secret settings through `extensions configure PACKAGE_ID
--settings-file FILE.json --expected-revision INSPECTED_REVISION`; see the
[settings workflow](authoring.md#configure-settings-from-the-cli).
Verify package management against matching installed host/renderer artifacts.

```bash
commanddeck extensions inspect --archive ./utilities.cdext --json
commanddeck extensions install --archive ./utilities.cdext --digest REVIEWED_SHA256 --yes
commanddeck extensions enable org.example.utilities
commanddeck extensions enable
commanddeck extensions list --json
commanddeck extensions inspect org.example.utilities --json
commanddeck extensions disable org.example.utilities
commanddeck extensions remove org.example.utilities --yes
```

Inspection returns the manifest and archive digest without changing the registry.
Review the requested capabilities and pass that exact digest to install. The host
copies and verifies the bytes again before activation. `--allow` accepts a comma
separated list of declared capabilities; omitted grants default to none for a new
package. Installation retains existing grants that the updated manifest still
declares. All new packages and the master extension switch start disabled.

`enable` and `disable` without a package ID change the master switch. Disabling
preserves the package's source preferences and customizations. Removal retains an
unavailable identity so aliases and favorites can resolve after a same-origin
reinstall. Settings and installation storage are retained by default.
`--erase-settings` permanently erases both on removal; the UI provides the same
explicit choice in its confirmation. Disable preserves both. Updates and rollback
retain stored data; restoring an executable version does not restore old data.
Erasure retains an empty storage tombstone with an increased revision. Reinstalling
the same identity cannot revive an old storage revision or a stale write.

Install an update with the same inspection and digest workflow. A version already
in the retained version inventory cannot be replaced with different bytes. Use
`extensions rollback PACKAGE_ID --digest RETAINED_DIGEST --yes` to select a retained
version. Rollback intersects current grants with its declared capabilities; it
does not restore revoked access. Use `extensions grants PACKAGE_ID --allow
CAPABILITY,... --yes` to replace grants explicitly.

Each installed package retains at most eight artifact digests, including the
current version. A committed update removes older digest directories outside that
rollback set. Failed activation artifacts are removed without disturbing the
current version, and daemon startup retries cleanup of abandoned staging,
unreferenced package, and obsolete version directories. Removal deletes all
artifacts for that package while retaining its unavailable logical identity.
Cleanup never follows a package or versions directory that has been replaced by
a symbolic link.

The inventory has a 128-package bound, including removed identities. `list` returns
at most 32 entries; use `--offset 32` for the next page and `--query TEXT` to filter
by package ID or name. Archive limits are 200 MiB compressed, 500 MiB expanded,
10,000 entries, and a 64 KiB manifest. Links, special files, traversal, path
collisions, unsupported profiles, and malformed executable headers are rejected.

Accepted manifests contribute one source per package. Bootstrap supplies its
stable filter ID and token; saved filters and aliases resolve against the same
registry. Disabled filters return an unavailable error rather than widening the
query. Settings source edits use bounded patches with registry and preference
revisions. A stale registry requires refreshing Settings.

If the stored registry cannot be read, for example because a package declares a
feature this build does not support, extensions are unavailable and the error
names the reason. The stored registry is left untouched, and Settings keeps
saving built-in source preferences against its stored revision.

These are experimental local-management contracts. They do not yet establish a
stable public SDK release, external feeds, or installed desktop
acceptance.

The package format recognizes `linux-x64` and `linux-arm64`, including their
specific ELF headers and sandbox profiles. The current host can launch only the
Linux x64 profile. Install and rollback reject a package with no runnable target
before changing the active version. A multi-target package uses its x64 entry.
ARM64 metadata support does not establish ARM64 execution or desktop acceptance.
