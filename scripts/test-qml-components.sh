#!/usr/bin/env bash
set -euo pipefail

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
# The baseline is a repository-owned data file; the validation command does
# not use shellcheck -x, so it is checked through its consuming script only.
# shellcheck disable=SC1091
source "$repo_dir/scripts/validation-baseline.env"

# Quickshell uses Qt 6; Arch may also expose Qt 5's tools on PATH.
qt6_bin=/usr/lib/qt6/bin
if ! command -v qs >/dev/null; then
  printf 'error: qs is missing; install quickshell.\n' >&2
  exit 1
fi
for qt6_tool in qmllint qmltestrunner; do
  if [[ ! -x $qt6_bin/$qt6_tool ]]; then
    printf 'error: %s is missing; install qt6-declarative.\n' "$qt6_bin/$qt6_tool" >&2
    exit 1
  fi
done

# The component harness quits itself once every stage has run; the limit only
# stops a hung Quickshell. A passing run takes a few seconds and grows with the
# specs, and shared CI runners are slower, so the default leaves wide headroom.
component_timeout=${COMMANDDECK_QML_COMPONENT_TIMEOUT:-60}
if [[ ! $component_timeout =~ ^[1-9][0-9]*$ ]]; then
  printf 'error: COMMANDDECK_QML_COMPONENT_TIMEOUT must be a whole number of seconds.\n' >&2
  exit 2
fi

# qml/omarchy/ThemeTokens.qml imports qs.Commons from the Omarchy shell.
omarchy_path=${OMARCHY_PATH:-/usr/share/omarchy}
qml_lint_args=(-W 0 -I "$omarchy_path/shell")
commons_qmldir="$omarchy_path/shell/Commons/qmldir"
if [[ -f $commons_qmldir ]]; then
  qml_lint_args+=(-i "$commons_qmldir")
fi
lint_count=0
lint_status=0
while IFS= read -r qml_file; do
  if ! "$qt6_bin/qmllint" "${qml_lint_args[@]}" "$qml_file"; then
    printf 'error: qmllint rejected %s; structured diagnostics follow.\n' "$qml_file" >&2
    "$qt6_bin/qmllint" "${qml_lint_args[@]}" --json - "$qml_file" || lint_status=$?
    # Preserve failure even if the diagnostic rerun succeeds.
    lint_status=${lint_status:-1}
    if ((lint_status == 0)); then lint_status=1; fi
  fi
  lint_count=$((lint_count + 1))
done < <(find "$repo_dir/qml" -type f -name '*.qml' -print | sort)
test "$lint_count" -gt 0
if ((lint_status != 0)); then
  exit "$lint_status"
fi

spec_dir=$(mktemp -d)
trap 'rm -rf -- "$spec_dir"' EXIT
cp "$repo_dir"/qml/common/*.qml "$repo_dir"/qml/common/*.js "$spec_dir/"
cp "$repo_dir/tests/CommandDeck.QmlSpecs/shell.qml" "$spec_dir/shell.qml"
cp "$repo_dir"/tests/CommandDeck.QmlSpecs/Assertions/*.qml "$spec_dir/"
cp -R "$repo_dir/qml/common" "$repo_dir/qml/omarchy" "$spec_dir/"

component_started=$SECONDS
output=$(timeout "${component_timeout}s" qs --no-color -p "$spec_dir" 2>&1) || {
  status=$?
  printf '%s\n' "$output" >&2
  if ((status == 124)); then
    printf 'error: the Quickshell component specs did not finish within %s s; the output above is partial.\n' \
      "$component_timeout" >&2
  fi
  exit "$status"
}

printf '%s\n' "$output"
printf 'Quickshell component specs finished in %s s (limit %s s).\n' \
  "$((SECONDS - component_started))" "$component_timeout"
grep -Fq "QML specs passed." <<<"$output"
component_count=$(grep -c '^ DEBUG qml: PASS ' <<<"$output")

event_output=$("$qt6_bin/qmltestrunner" \
  -platform offscreen \
  -input "$repo_dir/tests/CommandDeck.QmlSpecs" \
  -import "$repo_dir/qml/common" 2>&1) || {
  event_status=$?
  printf '%s\n' "$event_output" >&2
  exit "$event_status"
}
printf '%s\n' "$event_output"
event_count=$(grep -c '^PASS   : qmltestrunner::.*::test_' <<<"$event_output")

# The floors reject zero discovery and accidental test removal.
if ((component_count < QML_COMPONENT_SPEC_MINIMUM || event_count < QML_EVENT_SPEC_MINIMUM)); then
  printf 'error: %s component assertions and %s event tests passed; the minimums are %s and %s.\n' \
    "$component_count" "$event_count" "$QML_COMPONENT_SPEC_MINIMUM" "$QML_EVENT_SPEC_MINIMUM" >&2
  exit 1
fi
printf 'QML_COUNT lint=%s component=%s event=%s\n' "$lint_count" "$component_count" "$event_count"
