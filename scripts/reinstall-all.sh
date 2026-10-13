#!/usr/bin/env bash
set -euo pipefail

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)

case "$(uname -m)" in
  x86_64) host_rid=linux-x64 ;;
  aarch64|arm64) host_rid=linux-arm64 ;;
  *)
    echo "Unsupported host architecture: $(uname -m)" >&2
    exit 2
    ;;
esac

if [[ ${1:-} == "-h" || ${1:-} == "--help" ]]; then
  cat <<'EOF'
Usage: ./scripts/reinstall-all.sh [linux-x64|linux-arm64] [--reset-config]

Validates, publishes, and reinstalls CommandDeck from this local working tree
for the current user, including uncommitted changes. This includes the Native
AOT binary, systemd user service, catalog, Omarchy plugin, Ctrl+Space binding,
and all required reloads. It does not fetch or pull remote changes.

By default, an existing CommandDeck configuration is preserved. Pass
--reset-config to permanently delete the complete CommandDeck configuration
directory before generating the current default configuration.

Set COMMANDDECK_SKIP_VALIDATE=1 to skip the repository validation gate.
EOF
  exit 0
fi

rid=$host_rid
reset_config=0
for argument in "$@"; do
  case "$argument" in
    linux-x64|linux-arm64)
      rid=$argument
      ;;
    --reset-config)
      reset_config=1
      ;;
    *)
      echo "Unknown argument: $argument" >&2
      echo "Run ./scripts/reinstall-all.sh --help for usage." >&2
      exit 2
      ;;
  esac
done

if [[ $rid != "$host_rid" ]]; then
  echo "Cannot install $rid on this $host_rid host." >&2
  exit 2
fi

config_home=${XDG_CONFIG_HOME:-$HOME/.config}
data_home=${XDG_DATA_HOME:-$HOME/.local/share}
state_home=${XDG_STATE_HOME:-$HOME/.local/state}
service_dir="$config_home/systemd/user"
service_file="$service_dir/commanddeck.service"
config_file="$config_home/commanddeck/commanddeck.jsonc"
installed_binary="$data_home/commanddeck/commanddeck"
bindings_file="$config_home/hypr/bindings.lua"
binding_backup_dir="$state_home/commanddeck/binding-backups"
daemon_needs_recovery=0
reindex_degraded=0

restore_daemon_on_failure() {
  local exit_status=$?
  trap - EXIT
  if (( exit_status != 0 && daemon_needs_recovery )); then
    echo "Reinstall failed after stopping CommandDeck; restoring the daemon." >&2
    systemctl --user daemon-reload 2>/dev/null || true
    systemctl --user restart commanddeck.service 2>/dev/null || true
  fi
  exit "$exit_status"
}

trap restore_daemon_on_failure EXIT

step() {
  printf '\n==> %s\n' "$1"
}

# A restarted shell answers a ping before it has loaded every plugin, and until
# then its IPC calls can outlast omarchy-shell's 2-second default. Give each
# call longer and retry it, so a slow start does not fail the reinstall.
shell_ipc_timeout=10s
shell_ready_seconds=60

shell_call() {
  local deadline=$((SECONDS + shell_ready_seconds))
  local output
  until output=$(OMARCHY_SHELL_IPC_TIMEOUT=$shell_ipc_timeout "$@" 2>&1); do
    if (( SECONDS >= deadline )); then
      printf '%s\n' "$output" >&2
      echo "Omarchy Shell did not complete '$*' within ${shell_ready_seconds}s." >&2
      return 1
    fi
    sleep 1
  done

  if [[ -n $output ]]; then
    printf '%s\n' "$output"
  fi
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Required command not found: $1" >&2
    exit 1
  fi
}

ensure_ctrl_space_binding() {
  mkdir -p "$(dirname -- "$bindings_file")"
  touch "$bindings_file"

  if grep -Fq -- "-- BEGIN COMMANDDECK CTRL SPACE" "$bindings_file"; then
    echo "Ctrl+Space CommandDeck binding already managed in $bindings_file"
    return
  fi

  if awk '
    /CTRL[[:space:]]*\+[[:space:]]*SPACE/ { remaining = 6 }
    remaining > 0 && /local\.commanddeck/ { found = 1 }
    remaining > 0 { remaining-- }
    END { exit(found ? 0 : 1) }
  ' "$bindings_file"; then
    echo "Ctrl+Space CommandDeck binding already present in $bindings_file"
    return
  fi

  mkdir -p "$binding_backup_dir"
  binding_backup="$binding_backup_dir/bindings.lua.$(date +%Y%m%d%H%M%S)"
  cp -a -- "$bindings_file" "$binding_backup"

  cat >>"$bindings_file" <<'EOF'

-- BEGIN COMMANDDECK CTRL SPACE
hl.unbind("CTRL + SPACE")
hl.bind(
  "CTRL + SPACE",
  hl.dsp.exec_cmd([[omarchy-shell shell toggle local.commanddeck '{}']]),
  { description = "CommandDeck" }
)
-- END COMMANDDECK CTRL SPACE
EOF

  echo "Installed Ctrl+Space binding; backup: $binding_backup"
}

for required in dotnet systemctl omarchy omarchy-shell hyprctl jq; do
  require_command "$required"
done

cd "$repo_dir"

if [[ ${COMMANDDECK_SKIP_VALIDATE:-0} != "1" ]]; then
  step "Validate repository"
  "$repo_dir/scripts/validate-repository.sh"
fi

step "Publish Native AOT for $rid"
"$repo_dir/scripts/publish-aot.sh" "$rid"

step "Stop the existing daemon"
systemctl --user stop commanddeck.service 2>/dev/null || true
daemon_needs_recovery=1

step "Install binary and systemd user service"
"$repo_dir/scripts/install-local.sh" "$rid"
install -Dm644 "$repo_dir/packaging/systemd/commanddeck.service" "$service_file"
systemctl --user daemon-reload

if (( reset_config )); then
  step "Delete existing CommandDeck configuration"
  "$repo_dir/scripts/reset-local-config.sh" --yes
fi

if [[ ! -f $config_file ]]; then
  step "Create default configuration"
  "$installed_binary" config init
else
  echo "Preserving existing configuration: $config_file"
fi

step "Install Omarchy shell plugin"
"$repo_dir/scripts/install-omarchy-plugin.sh"

step "Verify installed files match this working tree"
if ! cmp -s -- "$repo_dir/artifacts/publish/$rid/commanddeck" "$installed_binary"; then
  echo "Installed binary differs from the freshly published binary." >&2
  exit 1
fi
plugin_dir="$config_home/omarchy/plugins/local.commanddeck"
if ! diff -qr -- "$repo_dir/qml" "$plugin_dir"; then
  echo "Installed plugin differs from the current QML source." >&2
  exit 1
fi

step "Rebuild search catalog"
if ! "$installed_binary" reindex --quiet; then
  reindex_degraded=1
  echo "Reindex reported unavailable or failed inputs; continuing with its usable catalog." >&2
fi

step "Ensure Ctrl+Space binding"
ensure_ctrl_space_binding

step "Import desktop session environment and start daemon"
session_variables=()
for variable_name in \
  HYPRLAND_INSTANCE_SIGNATURE \
  WAYLAND_DISPLAY \
  XDG_CURRENT_DESKTOP \
  XDG_SESSION_TYPE \
  PATH; do
  if [[ -v $variable_name ]]; then
    session_variables+=("$variable_name")
  fi
done
if (( ${#session_variables[@]} > 0 )); then
  systemctl --user import-environment "${session_variables[@]}"
fi
systemctl --user enable commanddeck.service >/dev/null
systemctl --user restart commanddeck.service
daemon_needs_recovery=0

step "Reload Hyprland and Omarchy Shell"
hyprctl reload >/dev/null
config_errors=$(hyprctl configerrors)
if [[ -n $config_errors ]]; then
  echo "$config_errors" >&2
  exit 1
fi
omarchy restart shell
shell_call omarchy-shell shell rescanPlugins >/dev/null
shell_call omarchy plugin enable local.commanddeck >/dev/null

step "Verify live installation"
systemctl --user is-active --quiet commanddeck.service
shell_call omarchy-shell shell ping >/dev/null
shell_call omarchy plugin list --json | jq -e '
  any(.[]; .id == "local.commanddeck" and .enabled == true)
' >/dev/null
"$installed_binary" doctor
"$installed_binary" search "= 2 + 2" --json | jq -e '
  any(.[]; .kind == "calculator" and .title == "4")
' >/dev/null

if (( reindex_degraded )); then
  echo "Warning: reindex reported unavailable or failed inputs; inspect commanddeck index status." >&2
fi

printf '\nCommandDeck reinstall complete. Press Ctrl+Space to open it.\n'
