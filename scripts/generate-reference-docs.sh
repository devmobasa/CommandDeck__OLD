#!/usr/bin/env bash
set -euo pipefail

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)

# Regenerates the permission matrix, the configuration option inventory, and
# schemas/commanddeck.schema.json from CommandDeckOptions / CapabilityRegistry.
# Frontend quicklink rules come from the Core validators and filter registry.
dotnet run \
  --project "$repo_dir/tests/CommandDeck.Specs" \
  -c Release \
  -- --write-reference-docs
