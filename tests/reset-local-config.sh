#!/usr/bin/env bash
set -euo pipefail

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test_root=$(mktemp -d)
trap 'rm -rf -- "$test_root"' EXIT

config_home="$test_root/config"
home_dir="$test_root/home"
mkdir -p "$config_home/commanddeck" "$home_dir"
touch "$config_home/commanddeck/commanddeck.jsonc"
touch "$config_home/unrelated"

if XDG_CONFIG_HOME="$config_home" HOME="$home_dir" \
  "$repo_dir/scripts/reset-local-config.sh" >/dev/null 2>&1; then
  echo "reset-local-config must require --yes" >&2
  exit 1
fi
test -f "$config_home/commanddeck/commanddeck.jsonc"

output=$(XDG_CONFIG_HOME="$config_home" HOME="$home_dir" \
  "$repo_dir/scripts/reset-local-config.sh" --yes)
test ! -e "$config_home/commanddeck"
test -f "$config_home/unrelated"
grep -Fq -- "Removed CommandDeck configuration: $config_home/commanddeck" <<<"$output"

mkdir -p "$config_home/commanddeck"
touch "$config_home/commanddeck/second.jsonc"
if XDG_CONFIG_HOME="relative/config" HOME="$home_dir" \
  "$repo_dir/scripts/reset-local-config.sh" --yes >/dev/null 2>&1; then
  echo "reset-local-config must reject a relative XDG_CONFIG_HOME" >&2
  exit 1
fi
test -f "$config_home/commanddeck/second.jsonc"

help_output=$("$repo_dir/scripts/reinstall-all.sh" --help)
grep -Fq -- "--reset-config" <<<"$help_output"
grep -Fq -- "including uncommitted changes" <<<"$help_output"
grep -Fq -- "It does not fetch or pull remote changes." <<<"$help_output"

printf 'PASS reset-local-config safety and behavior\n'
