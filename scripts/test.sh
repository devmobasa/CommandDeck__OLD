#!/usr/bin/env bash
set -euo pipefail

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_dir"

dotnet run \
  --project tests/CommandDeck.Specs/CommandDeck.Specs.csproj \
  --configuration Release
