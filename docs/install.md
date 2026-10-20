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
reindexes, installs the Ctrl+Space hotkey, imports the session environment, and
reloads the desktop. It finishes when the plugin has registered the hotkey's
global shortcut. Pass `--hotkey="SUPER + SPACE"` (or another chord) to bind a
different chord. Existing configuration is preserved. Plugin and binding backups
are kept under `$XDG_STATE_HOME/commanddeck/`.

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
binding or enable it. Add the hotkey with the installed binary:

```bash
commanddeck hotkey install                          # Ctrl+Space
commanddeck hotkey install --chord "SUPER + SPACE"  # or another chord
hyprctl reload
hyprctl configerrors
```

The command writes CommandDeck's own generated module,
`~/.config/hypr/commanddeck.lua`:

```lua
hl.unbind("CTRL + SPACE")
hl.bind("CTRL + SPACE", hl.dsp.global("commanddeck:toggle"), { description = "CommandDeck" })
```

It also adds one line to `~/.config/hypr/hyprland.lua`, directly after
`require("hypr.bindings")`, so the chord replaces a personal binding of the same
keys:

```lua
if package.searchpath("hypr.commanddeck", package.path) then require("hypr.commanddeck") end -- CommandDeck hotkey
```

The guard keeps the rest of `hyprland.lua` loading if the module is deleted.
Hyprland's global dispatcher sends `commanddeck:toggle` to the shortcut the
plugin registers inside `omarchy-shell`, so a key press starts no process. The
standalone host does not register the shortcut.

Rerunning the command changes nothing when the module and line are current; it
never adds a second require line. A changed chord rewrites only the module. An
edited file is first copied to `$XDG_STATE_HOME/commanddeck/binding-backups/`.
The command also removes the `-- BEGIN COMMANDDECK CTRL SPACE` block that earlier
installers appended to `~/.config/hypr/bindings.lua`. If `bindings.lua` already
binds the chord to CommandDeck itself, the command leaves your binding in place
and writes nothing; for the default chord, `commanddeck doctor` then accepts that
binding instead of the module. Validate Ctrl+Space before choosing a replacement for your
desktop's existing Super+Space binding.

## Check and troubleshoot

```bash
commanddeck version
commanddeck doctor
commanddeck status --json
systemctl --user status commanddeck.service
journalctl --user -u commanddeck.service -n 100 --no-pager
omarchy plugin validate ./qml
```

`commanddeck doctor` also reads the desktop wiring without changing it. It
checks that the plugin is installed and enabled in `~/.config/omarchy/shell.json`,
and that `commanddeck.lua` exists and `hyprland.lua` requires it once. In a
Hyprland session, it also checks that `hyprctl -j binds` shows the CommandDeck
chord and `hyprctl -j globalshortcuts` lists `commanddeck:toggle`. These checks
only warn or skip, so they never fail `doctor`. Each warning prints a `Fix:` line.

- No files: check roots/exclusions, run `commanddeck reindex`, and inspect
  `commanddeck index status`.
- No windows/workspaces: import the graphical session environment and restart
  `commanddeck.service`.
- No copy: check `command -v wl-copy` and the Wayland session.
- No overlay: inspect plugin validation and shell diagnostics; check that the
  daemon and `commanddeck` on the shell PATH match the installed frontend.
- Hotkey does nothing: run `commanddeck doctor` and follow its `Fix:` lines.
  Usually `commanddeck hotkey install` and `hyprctl reload` repair the binding.
  If `hyprctl globalshortcuts` does not list `commanddeck:toggle`, the shell has
  not loaded the plugin; run `omarchy restart shell`.
- AI failure: review [AI prerequisites](ai.md). Never print a key
  while checking systemd credential/environment setup.

Test open/type, arrows, Enter, actions, Escape, reopen, and immediate typing
after a mouse click. Multi-monitor focus, real clipboard ownership, compositor
previews, and recovery require a live Wayland session; offscreen tests alone
do not prove them.

## Disable or roll back

```bash
commanddeck hotkey remove
hyprctl reload
omarchy plugin disable local.commanddeck
omarchy restart shell
systemctl --user disable --now commanddeck.service
```

`commanddeck hotkey remove` deletes the require line and the generated
`commanddeck.lua`, after backing up `hyprland.lua`. It leaves a `commanddeck.lua`
that it did not generate. To restore a previous plugin or binding file instead,
copy it from the recorded backups, then reload Hyprland and rescan/restart the
shell. Do not erase user config, catalog, private stores, or credentials as part
of a binary/plugin rollback.

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
