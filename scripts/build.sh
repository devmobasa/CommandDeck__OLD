#!/usr/bin/env bash
set -euo pipefail

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_dir"

dotnet restore CommandDeck.slnx --locked-mode
dotnet build CommandDeck.slnx --configuration Release --no-restore
