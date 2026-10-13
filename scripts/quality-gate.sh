#!/usr/bin/env bash
set -euo pipefail

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
output_path=${1:-"$repo_dir/build/quality-metrics.json"}

quality_args=(--quality-benchmark --quality-output "$output_path")
quality_mode=${COMMANDDECK_QUALITY_LATENCY_MODE:-enforce}
case "$quality_mode" in
  enforce) ;;
  observe) quality_args+=(--quality-observe-latency) ;;
  *) printf 'Invalid COMMANDDECK_QUALITY_LATENCY_MODE: %s\n' "$quality_mode" >&2; exit 2 ;;
esac

dotnet run \
  --project "$repo_dir/tests/CommandDeck.Specs/CommandDeck.Specs.csproj" \
  --configuration Release \
  --no-build \
  -- "${quality_args[@]}"
