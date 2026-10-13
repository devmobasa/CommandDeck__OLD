# Installation and desktop integration

CommandDeck is a source-built preview for Linux x64, Omarchy Quattro's shell
plugin system, and Hyprland. The shared frontend also has an experimental
standalone Quickshell host. An older Omarchy desktop without shell plugins is
not covered by the plugin installer.

## Prerequisites

- The .NET SDK pinned in [global.json](../global.json).
- A native build toolchain, Clang, zlib, SQLite, and ICU.
- Omarchy Quattro with `omarchy plugin` support, Quickshell (`qs`), and Hyprland.
- A systemd user manager, `hyprctl`, `xdg-open`, `jq`, and `wl-copy`.
- ShellCheck for repository validation and Bubblewrap for isolated extensions/AI.
- Qt 6 `qmllint` and `qmltestrunner` for desktop validation.

On Arch:

```bash
sudo pacman -S --needed base-devel clang zlib sqlite icu shellcheck bubblewrap wl-clipboard jq
```

Optional PDF/document content extraction needs `poppler`/`pandoc` and explicit
configuration. The calculator can optionally use `qalc`. AI CLIs, Ollama, HEY,
and Herdr are separately installed optional integrations.

## Build and inspect

```bash
git clone https://github.com/devmobasa/commanddeck.git
cd commanddeck
```

### Install the pinned SDK

Use a .NET SDK installation that satisfies `global.json`. If your package manager
does not provide that preview version, the
[Microsoft installer](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script)
can read the pin directly:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/commanddeck-dotnet-install.sh
bash /tmp/commanddeck-dotnet-install.sh --jsonfile global.json --install-dir "$HOME/.dotnet"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

Keep these environment settings in your shell configuration if you use this
SDK installation for later builds. The installer does not install native system
dependencies. With the SDK available, continue:

```bash
dotnet --version
dotnet restore CommandDeck.slnx --locked-mode
./scripts/build.sh
./scripts/test.sh
```

Inspect [the configuration example](../config/commanddeck.example.jsonc).
Default roots index home filenames/paths, including hidden files, and enable
content search in selected document/configuration roots. Credential exclusions
remain active. Narrow roots before the first reindex if you prefer.

## Complete user-local installation

Run inside your graphical session:

```bash
./scripts/reinstall-all.sh
```

The script validates, publishes, installs under `~/.local/share/commanddeck`,
links `~/.local/bin/commanddeck`, installs/enables the user service and plugin,
reindexes, ensures Ctrl+Space, imports the session environment, and reloads the
desktop. Existing configuration is preserved. Plugin and binding backups are
kept under `$XDG_STATE_HOME/commanddeck/`.

Add `~/.local/bin` to PATH if it is absent. The installer requires a working
Omarchy session; it does not install a compositor or Omarchy itself.

`./scripts/reinstall-all.sh --reset-config` permanently deletes the CommandDeck
configuration directory and regenerates defaults. Use it only when you intend
that reset. Catalog and other state are not deleted by the reset option.

## Manual installation

Publish and install the binary/assets:

```bash
./scripts/publish-aot.sh linux-x64
./scripts/install-local.sh linux-x64
commanddeck config init
```

Review `~/.config/commanddeck/commanddeck.jsonc`, then install the service:

```bash
mkdir -p ~/.config/systemd/user
install -m644 packaging/systemd/commanddeck.service ~/.config/systemd/user/commanddeck.service
systemctl --user daemon-reload
systemctl --user import-environment HYPRLAND_INSTANCE_SIGNATURE WAYLAND_DISPLAY XDG_CURRENT_DESKTOP XDG_SESSION_TYPE PATH
commanddeck reindex
systemctl --user enable --now commanddeck.service
```

Import only environment names present in your graphical session. If using custom
XDG locations, inspect the service's executable and writable paths and provide
a user-unit override.

Install/validate the plugin:

```bash
./scripts/install-omarchy-plugin.sh
omarchy-shell shell rescanPlugins
omarchy plugin enable local.commanddeck
omarchy restart shell
omarchy-shell shell toggle local.commanddeck '{}'
```

The manual plugin installer backs up an existing plugin but does not add a
binding or enable it. The complete installer manages this Lua block in
`~/.config/hypr/bindings.lua`:

```lua
hl.unbind("CTRL + SPACE")
hl.bind(
  "CTRL + SPACE",
  hl.dsp.exec_cmd([[omarchy-shell shell toggle local.commanddeck '{}']]),
  { description = "CommandDeck" }
)
```

Back up bindings before manual edits, then run `hyprctl reload` and
`hyprctl configerrors`. Validate Ctrl+Space before choosing a replacement for
your desktop's existing Super+Space binding.

## Check and troubleshoot

```bash
commanddeck version
commanddeck doctor
commanddeck status --json
systemctl --user status commanddeck.service
journalctl --user -u commanddeck.service -n 100 --no-pager
omarchy plugin validate ./qml
```

- No files: check roots/exclusions, run `commanddeck reindex`, and inspect
  `commanddeck index status`.
- No windows/workspaces: import the graphical session environment and restart
  `commanddeck.service`.
- No copy: check `command -v wl-copy` and the Wayland session.
- No overlay: inspect plugin validation and shell diagnostics; check that the
  daemon and `commanddeck` on the shell PATH match the installed frontend.
- AI failure: review [AI prerequisites](ai.md). Never print a key
  while checking systemd credential/environment setup.

Test open/type, arrows, Enter, actions, Escape, reopen, and immediate typing
after a mouse click. Multi-monitor focus, real clipboard ownership, compositor
previews, and recovery require a live Wayland session; offscreen tests alone
do not prove them.

## Disable or roll back

```bash
omarchy plugin disable local.commanddeck
omarchy restart shell
systemctl --user disable --now commanddeck.service
```

Restore the previous binding/plugin from the recorded backups, then reload
Hyprland and rescan/restart the shell. Do not erase user config, catalog, private
stores, or credentials as part of a binary/plugin rollback.

## Standalone host and ARM64

With the daemon running on Hyprland:

```bash
qs -p ./qml
# Another terminal:
qs ipc -p ./qml call commanddeck toggle '{}'
```

See [the standalone host guide](../qml/quickshell-standalone/README.md).
Other compositors require frontend adapters and backend providers.

The publish script accepts `linux-arm64`, but build/runtime smoke and installed
acceptance need a matching ARM64 host. Cross-publishing alone is not platform
support evidence.
