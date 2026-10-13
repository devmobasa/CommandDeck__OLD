# Build and use C# extension commands

This experimental path supports Linux x64, local List filtering, Detail views,
bounded view stacks, Previous/Next pages, packaged PNG images, callback and URL actions,
and host-brokered clipboard copy, including one-shot no-view commands. The current
SDK also supports native forms, non-secret settings, and
installation-scoped persistent storage. The [connection preview](connections.md)
adds selected-file access, scoped HTTP and host-owned credentials. UUID Tools and Local Draft are examples
built outside the daemon source tree. Validate their actual desktop journeys
against matching installed host and renderer artifacts.
The SDK targets .NET 10; the daemon
retains the SDK pinned in the repository's `global.json`. The NuGet packages are
a local preview feed, not a public compatibility release.

## Prerequisites

- The repository's .NET SDK and .NET SDK **10.0.400** installed side by side.
- Linux x64 with Native AOT build prerequisites (Clang and the native development
  libraries used by the .NET SDK).
- To run: a matching published CommandDeck daemon and repository renderer,
  Bubblewrap, a systemd user manager with the required resource controllers,
  and the `linux-glibc-invariant-v1` libraries (glibc, libstdc++, libgcc,
  libm, pthread, libdl, librt, zlib, and OpenSSL 3). See the profile in
  `src/CommandDeck.Infrastructure/Extensions/ExtensionSandbox.cs`.
- A Wayland clipboard service and `wl-copy` for actual desktop copy.

No author SDK is loaded by the daemon. Missing isolation prerequisites fail the
command; they do not select an unrestricted execution mode.

## Create, check, build, package, and update

The `CommandDeck.ExtensionTools` local preview package provides `commanddeck-ext`.
It embeds the Local Draft form/settings/storage example, so creating a project
does not require copying source from the checkout. From the repository root:

```bash
repo_dir="$PWD"
feed="$repo_dir/artifacts/extensions/author-tools/feed"
dotnet scripts/build-extension-sdk.cs --output "$feed"
dotnet tool install CommandDeck.ExtensionTools --version 0.1.0-preview.6 \
  --tool-path /tmp/commanddeck-author-tools --add-source "$feed"
/tmp/commanddeck-author-tools/commanddeck-ext new \
  --directory /tmp/my-commanddeck-draft --id org.example.my-draft \
  --name "My draft" --feed "$feed"
/tmp/commanddeck-author-tools/commanddeck-ext check --directory /tmp/my-commanddeck-draft
/tmp/commanddeck-author-tools/commanddeck-ext build --directory /tmp/my-commanddeck-draft
/tmp/commanddeck-author-tools/commanddeck-ext pack --directory /tmp/my-commanddeck-draft \
  --output /tmp/my-draft-0.1.0.cdext
```

`new` refuses existing directories and writes the pinned SDK, mapped preview feed,
project, manifest, and source. The template requests SDK `0.1.0-preview.6`, which
includes the data APIs it uses; cached `0.1.0-preview.1` packages cannot satisfy
that reference. SDK and tooling changes must get new package versions instead
of replacing existing preview versions with different bytes. This also protects
authors invoking `dotnet build` directly. `check` validates the manifest and supported project
layout without building or running the extension. `build` runs Native AOT publish;
`pack` performs a fresh build before writing a new archive. Each publish has a
unique output directory under `.commanddeck/`; these ignored build outputs can be
deleted when no build is running. Build and pack execute the author's MSBuild
targets with the author's normal user privileges. They are author commands, not
operations the daemon runs on installed packages. The old pack script uses the
same package builder.

Inspect the finished archive with a matching daemon. Review its manifest, digest,
and requested permissions. Then supply that exact digest and selected grants:

```bash
host="$repo_dir/artifacts/publish/linux-x64/commanddeck"
"$host" extensions inspect --archive /tmp/my-draft-0.1.0.cdext --json
/tmp/commanddeck-author-tools/commanddeck-ext update --host "$host" \
  --archive /tmp/my-draft-0.1.0.cdext --digest REVIEWED_SHA256 \
  --allow storage.read,storage.write --yes
```

Pass `--socket PATH` to both commands when using a private daemon. `update` uses
the existing daemon install transaction for both first installation and version
replacement. It does not enable or open a package, compute consent from its
manifest, or silently grant added capabilities. For the next edit, change the
manifest version, edit the source, pack to a new archive, inspect, and update.
Settings and installation storage survive replacement and rollback. Unchanged
versions with changed content are rejected by the daemon.

The public-tool acceptance script installs the tool into a temporary directory
with an isolated package cache, creates/builds/packages outside this repository,
tests reviewed update and rollback with a private daemon, and runs the generated
package through the connected Local Draft release-sandbox journey:

```bash
dotnet scripts/test-extension-author.cs "$host" "$feed"
# Regression with an earlier preview SDK feed already cached:
dotnet scripts/test-extension-author.cs "$host" "$feed" /path/to/older-preview-feed
```

This exercises an isolated test daemon. It does not establish installed desktop
acceptance; verify the keyboard/mouse journey and real clipboard separately.

## Development builds, reload, debugging, and logs

Tool preview 3 adds `dev`, `attach`, and `reload`. `dev` publishes Native AOT with
the normal release profile, then creates a new immutable archive with a separate
`.development` package ID, a `(Development)` label, and a unique build version.
It does not modify the source manifest or install anything. Inspect the archive,
then attach its exact digest and reviewed grants:

```bash
commanddeck-ext dev --directory /tmp/my-commanddeck-draft --output /tmp/draft-dev-1.cdext
commanddeck extensions inspect --archive /tmp/draft-dev-1.cdext --json
commanddeck-ext attach --host /absolute/path/to/commanddeck \
  --archive /tmp/draft-dev-1.cdext --digest REVIEWED_SHA256 \
  --allow storage.read,storage.write --yes
commanddeck extensions enable org.example.my-draft.development
commanddeck extensions enable
```

Choose the development extension's filter in the launcher, then open its command.
Development commands require that explicit filter; they do not participate in
unscoped search or shadow the original package. For a new edit, repeat `dev` with
a new output filename, inspect it, then use `reload` with the same arguments as
`attach`. Reload closes active development sessions before replacement and
preserves development settings/storage. It never replaces the production identity
or silently grants newly requested capabilities. Build or validation failure
leaves the installed version available. Reopen the command after reload.

For Native AOT debugging, install GDB and gdbserver through your system package
manager. Use the published executable and adjacent `.dbg` file in the publish
directory printed by `dev`; debug symbols are kept outside the release archive.
Grant a lease to the exact development build and command:

```bash
commanddeck extensions inspect org.example.my-draft.development --json
commanddeck extensions debug org.example.my-draft.development \
  --command edit --digest CURRENT_INSTALLED_DIGEST --seconds 300 --yes
```

Now open that command through its extension filter. Startup waits at the debugger.
The renderer must advertise support for visible debug leases; older renderers are
rejected while that command has an armed lease rather than hiding the exception.
The returned socket path and expiry also appear in the native view. Start GDB
with your published executable, then connect inside GDB (use absolute paths):

```text
target remote | /absolute/path/to/commanddeck extension-debug-connect --socket /tmp/commanddeck-debug-LEASE/debug.sock
break Program.cs:20
continue
```

Choose a source line in your command. This is native debugging, not a managed
debug adapter. The lease lasts at most five minutes, binds one artifact/command,
and is consumed by one session. It suspends child startup/callback deadlines;
host effects, delivery, resource/output ceilings and permissions stay bounded.
Closing the command/debugger, disabling/reloading the package, revoking the lease,
or reaching its expiry stops that debug session. Ordinary opens have no debugger
or deadline suspension. No debugger socket or source checkout is mounted into
other extensions. Stop explicitly with:

```bash
commanddeck extensions debug-stop org.example.my-draft.development
commanddeck extensions logs org.example.my-draft.development --json
commanddeck extensions logs org.example.my-draft.development --include-stderr
```

Settings → Search → Manage extensions → select a package → **Logs** shows recent
completed sessions, command/build identity and outcomes, with local filtering.
Development package details also offer a confirmed five-minute debug lease;
Logs can revoke it. Child stderr is omitted by default: the separate opt-in may
expose sensitive, untrusted extension text. Logs are in memory only, limited to
eight records per package and 4 KiB of child text per record; daemon restart clears
them. CLI `--socket PATH` selects a private daemon for all management commands.

The external author journey also verifies failed dev builds, separate attachment,
reload/settings preservation, and a real GDB session against the confined native
child. To run only the debugger journey:

```bash
dotnet scripts/test-extension-development.cs "$host" /tmp/draft-dev-1.cdext
```

## Configure settings from the CLI

Inspect the installed package **before editing**, saving its `revision` alongside
the returned `settings`. Write a JSON object of non-secret overrides, then save
against that same revision:

```bash
commanddeck extensions inspect org.example.my-draft --json
# Write settings.json, for example: {"defaultTitle":"Work notes"}
commanddeck extensions configure org.example.my-draft \
  --settings-file settings.json --expected-revision INSPECTED_REVISION
```

The file replaces the saved overrides; omitted optional settings remain unset,
and omitted settings with defaults use those defaults on read. `{}` clears
overrides, subject to required-setting validation. Input is limited to 64 KiB and
16 distinct names; the daemon validates types, ranges, required fields and secret
exclusion. A stale revision fails without overwriting newer settings. Reinspect
and reconcile the actual values before retrying; do not merely substitute a
newer revision into an old editor's save.

## Build an existing example outside the checkout

Run the first commands from the repository root. Choose a new empty author
directory; the example below uses `/tmp/commanddeck-uuid-author`.

```bash
repo_dir="$PWD"
dotnet scripts/build-extension-sdk.cs --output "$repo_dir/artifacts/extensions/v2/feed"
mkdir /tmp/commanddeck-uuid-author
cp -r samples/extensions/UuidTools /tmp/commanddeck-uuid-author/UuidTools
cd /tmp/commanddeck-uuid-author
```

Create `global.json` in that author directory:

```json
{"sdk":{"version":"10.0.400","rollForward":"disable"}}
```

Create `NuGet.Config`, replacing `ABSOLUTE_REPOSITORY_PATH` with the checkout
path. Source mapping keeps the experimental CommandDeck dependencies on the
local feed.

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="commanddeck-preview" value="ABSOLUTE_REPOSITORY_PATH/artifacts/extensions/v2/feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="commanddeck-preview"><package pattern="CommandDeck.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

Publish with a private package cache so a previously cached preview cannot mask
the feed being tested:

```bash
dotnet publish UuidTools -c Release -r linux-x64 --self-contained true \
  -p:PublishAot=true -p:RestorePackagesPath="$PWD/packages" -o "$PWD/publish"
cd "$repo_dir"
dotnet scripts/pack-extension.cs \
  --manifest /tmp/commanddeck-uuid-author/UuidTools/commanddeck.extension.json \
  --publish-dir /tmp/commanddeck-uuid-author/publish \
  --output "$repo_dir/artifacts/extensions/v2/uuid-tools.cdext"
```

The packer expects an executable directly under `bin/linux-x64` in the manifest,
copies the published output there, omits debugging symbols, and rejects symlinks.
It creates a new archive and refuses to overwrite an existing one. The packer
also includes bounded flat `assets/*.png` files beside the manifest. Daemon inspection performs the
authoritative manifest, archive, executable, and profile checks.

For your own command, copy the sample project, change the manifest package ID,
title and command declaration, and register that same command ID in `Program.cs`.
Keep the manifest entrypoint consistent with the published executable name.
Use `CommandContext.Views.ReplaceAsync` during a command invocation and declare
callback handlers through the SDK. The renderer receives typed content; authors
do not supply QML, shell commands, or clipboard authority.

## Install and use

The [Local Draft sample](../../samples/extensions/LocalDraft/README.md) adds native
forms, non-secret configuration, and persistent local data. Its installation and
configuration can be completed in Settings → Search → Manage extensions.

These commands change the selected running daemon's local package registry.
Add `--socket PATH` to each command for a private development daemon. Review the
inspection result and substitute its exact archive digest below.

```bash
commanddeck extensions inspect --archive ./artifacts/extensions/v2/uuid-tools.cdext --json
commanddeck extensions install --archive ./artifacts/extensions/v2/uuid-tools.cdext \
  --digest REVIEWED_SHA256 --allow clipboard.write --yes
commanddeck extensions enable org.commanddeck.uuid-tools
commanddeck extensions enable
```

Open the launcher and search for **Generate UUIDs**. Enter opens the List with
search focused. Type to filter, use arrows to select, and Enter to copy the
displayed UUID. Tab through Copy UUID, UUID details, Generate again, and page
controls; Space runs the focused action. Details opens formatted text and metadata;
Escape or Back restores the List filter, selected item, scroll position, and
invoking control. Generate again creates 24 UUIDs across three pages of eight.
Mouse selection, actions, paging, and Back follow the same host-authorized path.
Escape clears a filter before returning to launcher search. Reopening creates a
new process session and new action handles.

Use `context.Views.PushAsync(new DetailView { ... })` from an admitted callback
to push a view. The daemon owns Back and retains at most eight views. Replace
must target the active view ID; a pushed ID cannot duplicate one still in the
stack. Returned parents receive fresh revisions and action handles. The SDK
updates its revision tracking from the next event before publishing again.

Set `ListView.NextPage` and `PreviousPage` to bounded opaque continuation tokens,
then handle `PageRequested` by calling `ReplaceAsync` for that List. The renderer
receives host-issued handles rather than raw tokens. Old page controls expire
when their revision changes. If the underlying continuation expires, replace
the current List with an explanatory `Error` and a recovery callback.
Search filters the current page locally, including if edited during page loading;
it does not search all pages or send queries to the extension.

Detail text uses the existing safe formatting subset: bold, inline code, and code
blocks. HTML, links, and images remain literal text. Metadata is plain text.
Declare `views.detail@1`, `views.stack@1`, and `views.pagination@1` when required;
both the SDK and daemon check negotiated support.

```bash
commanddeck extensions disable org.commanddeck.uuid-tools
```

Disabling closes active sessions. Grant changes, replacement, rollback, removal,
client disconnect, and daemon shutdown also retire their authority. A command
failure shows Restart and Disable; effects are never automatically retried.
See [local package management](packages.md) for update and rollback.

## Connected acceptance

After publishing the daemon, this opt-in check installs packages into a temporary
registry and runs their actual isolated processes. Its clipboard sink is private
and does not change the desktop clipboard.

```bash
dotnet scripts/test-extension-session.cs \
  artifacts/publish/linux-x64/commanddeck artifacts/extensions/v2/uuid-tools.cdext
```

Append `--wayland` to drive the repository launcher on the active compositor.
This needs Quickshell, Hyprland, and writable `/dev/uinput`; it temporarily drives
the keyboard and pointer. Keep both idle during the check. It also exercises
crash recovery and explicit Restart/Disable. Evidence and logs go to ignored
`artifacts/extensions/`. This uses a private daemon and copied repository QML;
it is separate from acceptance of the permanently installed Omarchy plugin and
real desktop clipboard. Follow the [installation guide](../install.md)
for authorized installation and record that result separately.

Commands requiring unsupported
features receive an explicit error before their child starts.

## Forms, settings, and persistent data

Use [Local Draft](../../samples/extensions/LocalDraft/README.md) for the complete
build, configure, edit, save, and reopen journey. The
[SDK API guide](../../src/CommandDeck.Extensions/README.md) describes `FormSubmitted`,
revision-bound validation errors, and `context.Data` settings/storage methods.
Declare `views.form@1`, `settings@1`, and `storage@1` as needed; storage reads and
writes also require their corresponding command/package capability grants.

Settings are non-secret. Storage is installation-scoped, bounded, and revision
checked; it is not arbitrary filesystem access. List filtering remains local to
the displayed page. [Connection brokers](connections.md) provide explicit
HTTP and host credentials. [Scoped and opt-in global search](search.md)
use isolated read-only cache projections; remote search remains outside that lane.
Templates and native
development/debug tooling are described above; managed debugger adapters remain
outside this Native AOT workflow.

## Images, URL actions, and no-view commands

[Quick Tools](../../samples/extensions/QuickTools/README.md) is a second executable
sample using these features. Build it using the same external author directory,
SDK pin, local feed, and private cache as UUID Tools.

- Set `ListItem.Asset` or `DetailView.Asset` to `assets/name.png` and declare
  `assets.png@1`. The packer includes the flat `assets` directory beside the manifest.
  Images must be non-interlaced 8-bit grayscale, grayscale/alpha, RGB, or RGBA PNGs,
  at most 512×512 pixels and 32 KiB. Only IHDR, IDAT, and IEND chunks are accepted;
  remove metadata and color-profile chunks when exporting. The host checks CRCs,
  dimensions, and decompressed scanlines. It rejects symlinks, paths outside that
  directory, animations, SVGs, and remote images. Images become bounded data URLs;
  QML never receives a package path. Encoded images have a combined 128 KiB limit
  per view, within the 240 KiB rendered-view limit. Images are decorative; put
  essential information in the item's title or Detail text.
- Use `ViewAction.OpenUrl(id, title, "https://...")` and declare `actions.open-url@1`.
  Declare `desktop.open-url` in both package and command capabilities and grant it
  at installation. Only HTTP(S) URLs without embedded credentials are accepted.
  Publishing does not open a URL: a displayed action must be activated. The daemon
  retains its exact target and checks current grants before invoking the desktop
  URL handler. Markdown links remain inert.
- Set `IExtensionCommand.Mode` and the manifest command's `mode` to `no-view`.
  Declare `commands.no-view@1`; add `effects.copy-text@1` and `clipboard.write`
  to use `await context.Effects.CopyTextAsync(text, cancellationToken)` in
  `OpenAsync`. This permits one clipboard attempt of at most 64 KiB per invocation,
  with a five-second effect deadline inside the eight-second startup/work deadline.
  Discovery, initialization, retained contexts, and view callbacks cannot use it.
  A no-view command cannot publish views. Completion stops its process and
  descendants before showing Done; running work offers Cancel. The launcher
  stays open to show the result, and failed effects are never retried automatically.

Run the additional connected check against a published host and packed Quick Tools:

```bash
dotnet scripts/test-extension-quicktools.cs \
  artifacts/publish/linux-x64/commanddeck artifacts/extensions/m3b-complete/quick-tools.cdext
```

It uses a private registry and private clipboard/URL sinks. It does not open a
browser, change the desktop clipboard, or prove installed desktop acceptance.

## Preview.6 author tests and API migration

Preview.6 uses typed actions and fields, `init` records and immutable sequences.
The static factories remain available; use collection expressions or
`ToImmutableArray()` for sequences. Form submit is a callback action. Use the
[SDK migration notes](../../src/CommandDeck.Extensions/README.md#migrating-to-preview6)
for typed broker results, validation paths and optional typed event handlers.
Remote view search remains unavailable; the reserved APIs produce `CDEXT0001`.

The [testing package](../../src/CommandDeck.Extensions.Testing/README.md) runs the
real SDK over in-memory pipes, with settings, revisioned storage, fixture files,
scripted HTTP and copied-text recording. It shares the host's feature requirements
and view validation. This is an author test seam; confinement and real provider
behavior require daemon tests.

```csharp
await using var host = await ExtensionTestHost.StartAsync(new MyCommand());
await host.OpenAsync();
await host.InvokeAsync("refresh");
var view = host.CurrentView;
```

[LocalDraft.Tests](../../samples/extensions/LocalDraft.Tests/Program.cs) tests the
real sample's open, validation and save flow. Run its managed and Native AOT checks,
including a fresh local package feed, with:

```bash
dotnet scripts/test-extension-testing.cs
```

The `extensions` validation profile also runs this check. Unhandled author errors
write up to 4 KiB to child stderr; cancellation is omitted. Request logs explicitly
with `commanddeck extensions logs PACKAGE --include-stderr`. Host errors remain
safe and generic; a healthy action-handler failure retains the displayed view.
