#!/usr/bin/env bash
set -euo pipefail

if [[ ${1:-} == "-h" || ${1:-} == "--help" ]]; then
  cat <<'EOF'
Usage: ./scripts/reset-local-config.sh --yes

Permanently removes the current user's CommandDeck configuration directory.
This deletes $XDG_CONFIG_HOME/commanddeck, or ~/.config/commanddeck when
XDG_CONFIG_HOME is unset. It does not delete the catalog or other state.
EOF
  exit 0
fi

if [[ $# -ne 1 || $1 != "--yes" ]]; then
  echo "Refusing to delete CommandDeck configuration without --yes." >&2
  echo "Run: ./scripts/reset-local-config.sh --yes" >&2
  exit 2
fi

config_home=${XDG_CONFIG_HOME:-${HOME:?HOME is required}/.config}
if [[ $config_home != /* ]]; then
  echo "Refusing relative configuration root: $config_home" >&2
  exit 2
fi

resolved_config_home=$(realpath -m -- "$config_home")
config_dir="$resolved_config_home/commanddeck"
if [[ $resolved_config_home == "/" || $config_dir == "/commanddeck" || \
      $(basename -- "$config_dir") != "commanddeck" || \
      $(dirname -- "$config_dir") != "$resolved_config_home" ]]; then
  echo "Refusing unsafe CommandDeck configuration target: $config_dir" >&2
  exit 2
fi

if command -v mountpoint >/dev/null 2>&1 && mountpoint -q -- "$config_dir"; then
  echo "Refusing to delete mounted configuration directory: $config_dir" >&2
  exit 2
fi

if [[ -e $config_dir || -L $config_dir ]]; then
  rm -rf -- "$config_dir"
  echo "Removed CommandDeck configuration: $config_dir"
else
  echo "CommandDeck configuration is already absent: $config_dir"
fi
