#!/usr/bin/env bash
set -euo pipefail

# Installs the CommandDeck privileged package helper. CommandDeck never runs
# this script; a person runs it in a visible terminal and approves each sudo
# step. Pass --dry-run to print every destination without changing anything.

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
packaging_dir="$repo_dir/packaging/helper"

libexec_dir=/usr/lib/commanddeck
unit_dir=/usr/lib/systemd/system
dbus_service_dir=/usr/share/dbus-1/system-services
dbus_policy_dir=/usr/share/dbus-1/system.d
polkit_action_dir=/usr/share/polkit-1/actions
tmpfiles_dir=/usr/lib/tmpfiles.d

usage() {
  cat <<'EOF'
Usage: ./scripts/install-system-helper.sh [--dry-run] [--binary PATH]

Installs the CommandDeck package helper, its systemd unit, its D-Bus
activation file and bus policy, its Polkit actions, and its tmpfiles
definition. Requires an interactive terminal because it calls sudo.

  --dry-run      Print every destination path and command; change nothing.
  --binary PATH  Use this helper binary instead of the published artifact.
  -h, --help     Show this message.
EOF
}

case "$(uname -m)" in
  x86_64) host_rid=linux-x64 ;;
  aarch64 | arm64) host_rid=linux-arm64 ;;
  *)
    echo "Unsupported host architecture: $(uname -m)" >&2
    exit 2
    ;;
esac

dry_run=0
binary=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --dry-run)
      dry_run=1
      ;;
    --binary)
      if [[ $# -lt 2 ]]; then
        echo "--binary requires a path." >&2
        exit 2
      fi
      binary=$2
      shift
      ;;
    --binary=*)
      binary=${1#--binary=}
      ;;
    -h | --help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      echo "Run ./scripts/install-system-helper.sh --help for usage." >&2
      exit 2
      ;;
  esac
  shift
done

if [[ -z $binary ]]; then
  binary="$repo_dir/artifacts/publish/$host_rid/commanddeck-package-helper"
fi

echo "CommandDeck package helper installation"
echo "  source binary:  $binary"
echo "  destinations:"
echo "    $libexec_dir/commanddeck-package-helper"
echo "    $unit_dir/commanddeck-package-helper.service"
echo "    $dbus_service_dir/org.commanddeck.Package1.service"
echo "    $dbus_policy_dir/org.commanddeck.Package1.conf"
echo "    $polkit_action_dir/org.commanddeck.package.policy"
echo "    $tmpfiles_dir/commanddeck-package-helper.conf"

if [[ ! -x $binary ]]; then
  echo "Helper binary not found or not executable: $binary" >&2
  echo "Publish it first, or pass --binary PATH." >&2
  exit 2
fi

for source_file in \
  "$packaging_dir/commanddeck-package-helper.service" \
  "$packaging_dir/org.commanddeck.Package1.service" \
  "$packaging_dir/org.commanddeck.Package1.conf" \
  "$packaging_dir/org.commanddeck.package.policy" \
  "$packaging_dir/commanddeck-package-helper.conf"; do
  if [[ ! -f $source_file ]]; then
    echo "Packaging file not found: $source_file" >&2
    exit 2
  fi
done

if [[ $dry_run -eq 0 && ( ! -t 0 || ! -t 1 ) ]]; then
  echo "Run this installer from an interactive terminal, or pass --dry-run." >&2
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

run sudo install -D -o root -g root -m 0755 \
  "$binary" "$libexec_dir/commanddeck-package-helper"
run sudo install -D -o root -g root -m 0644 \
  "$packaging_dir/commanddeck-package-helper.service" \
  "$unit_dir/commanddeck-package-helper.service"
run sudo install -D -o root -g root -m 0644 \
  "$packaging_dir/org.commanddeck.Package1.service" \
  "$dbus_service_dir/org.commanddeck.Package1.service"
run sudo install -D -o root -g root -m 0644 \
  "$packaging_dir/org.commanddeck.Package1.conf" \
  "$dbus_policy_dir/org.commanddeck.Package1.conf"
run sudo install -D -o root -g root -m 0644 \
  "$packaging_dir/org.commanddeck.package.policy" \
  "$polkit_action_dir/org.commanddeck.package.policy"
run sudo install -D -o root -g root -m 0644 \
  "$packaging_dir/commanddeck-package-helper.conf" \
  "$tmpfiles_dir/commanddeck-package-helper.conf"

run sudo systemctl daemon-reload
run sudo systemd-tmpfiles --create "$tmpfiles_dir/commanddeck-package-helper.conf"

echo
echo "Installed. The helper starts on demand through D-Bus activation."
echo "Verify it with:"
echo "  busctl --system status org.commanddeck.Package1"
echo "  busctl --system introspect org.commanddeck.Package1 /org/commanddeck/Package1"
