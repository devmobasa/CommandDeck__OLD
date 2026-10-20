#!/usr/bin/env bash
set -euo pipefail

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
source_dir="$repo_dir/qml"
plugin_dir=${XDG_CONFIG_HOME:-$HOME/.config}/omarchy/plugins/local.commanddeck
backup_root=${XDG_STATE_HOME:-$HOME/.local/state}/commanddeck/plugin-backups

command -v omarchy >/dev/null
omarchy plugin validate "$source_dir"

if [[ -e $plugin_dir ]]; then
  mkdir -p "$backup_root"
  backup_dir="$backup_root/local.commanddeck.$(date +%s)"
  mv -- "$plugin_dir" "$backup_dir"
  echo "Backed up the previous plugin to $backup_dir"
fi

mkdir -p "$plugin_dir"
cp -a "$source_dir/." "$plugin_dir/"
omarchy plugin validate "$plugin_dir"

echo "Installed plugin in $plugin_dir"
echo "Enable with: omarchy plugin enable local.commanddeck"
echo "Bind the hotkey with: commanddeck hotkey install && hyprctl reload"
