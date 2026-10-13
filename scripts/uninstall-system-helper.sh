#!/usr/bin/env bash
set -euo pipefail

# Removes the CommandDeck privileged package helper installed by
# ./scripts/install-system-helper.sh. CommandDeck never runs this script.

libexec_dir=/usr/lib/commanddeck
unit_dir=/usr/lib/systemd/system
dbus_service_dir=/usr/share/dbus-1/system-services
dbus_policy_dir=/usr/share/dbus-1/system.d
polkit_action_dir=/usr/share/polkit-1/actions
tmpfiles_dir=/usr/lib/tmpfiles.d
runtime_dir=/run/commanddeck-package-helper

usage() {
  cat <<'EOF'
Usage: ./scripts/uninstall-system-helper.sh [--dry-run]

Stops and disables the CommandDeck package helper and removes every file the
installer wrote. Requires an interactive terminal because it calls sudo.

  --dry-run   Print every destination path and command; change nothing.
  -h, --help  Show this message.
EOF
}

dry_run=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --dry-run)
      dry_run=1
      ;;
    -h | --help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      echo "Run ./scripts/uninstall-system-helper.sh --help for usage." >&2
      exit 2
      ;;
  esac
  shift
done

echo "CommandDeck package helper removal"
echo "  files removed:"
echo "    $libexec_dir/commanddeck-package-helper"
echo "    $unit_dir/commanddeck-package-helper.service"
echo "    $dbus_service_dir/org.commanddeck.Package1.service"
echo "    $dbus_policy_dir/org.commanddeck.Package1.conf"
echo "    $polkit_action_dir/org.commanddeck.package.policy"
echo "    $tmpfiles_dir/commanddeck-package-helper.conf"
echo "    $runtime_dir"

if [[ $dry_run -eq 0 && ( ! -t 0 || ! -t 1 ) ]]; then
  echo "Run this uninstaller from an interactive terminal, or pass --dry-run." >&2
  exit 2
fi

run() {
  if [[ $dry_run -eq 1 ]]; then
    printf 'would run:'
    printf ' %q' "$@"
    printf '\n'
  else
    "$@"
  fi
}

run_optional() {
  if [[ $dry_run -eq 1 ]]; then
    printf 'would run:'
    printf ' %q' "$@"
    printf '\n'
  else
    "$@" || true
  fi
}

run_optional sudo systemctl stop commanddeck-package-helper.service
run_optional sudo systemctl disable commanddeck-package-helper.service

run sudo rm -f \
  "$libexec_dir/commanddeck-package-helper" \
  "$unit_dir/commanddeck-package-helper.service" \
  "$dbus_service_dir/org.commanddeck.Package1.service" \
  "$dbus_policy_dir/org.commanddeck.Package1.conf" \
  "$polkit_action_dir/org.commanddeck.package.policy" \
  "$tmpfiles_dir/commanddeck-package-helper.conf"
run sudo rm -rf "$runtime_dir"
run_optional sudo rmdir "$libexec_dir"

run sudo systemctl daemon-reload

echo
echo "Removed. Confirm the bus name is gone with:"
echo "  busctl --system status org.commanddeck.Package1"
