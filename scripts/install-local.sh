#!/usr/bin/env bash
set -euo pipefail

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
rid=${1:-linux-x64}
source_dir="$repo_dir/artifacts/publish/$rid"
install_dir=${XDG_DATA_HOME:-$HOME/.local/share}/commanddeck
bin_dir=${HOME}/.local/bin

test -x "$source_dir/commanddeck" || {
  echo "Publish first: ./scripts/publish-aot.sh $rid" >&2
  exit 2
}

mkdir -p "$install_dir" "$bin_dir"
install -m755 "$source_dir/commanddeck" "$install_dir/commanddeck"
if [[ -f $source_dir/commanddeck.schema.json ]]; then
  install -m644 "$source_dir/commanddeck.schema.json" "$install_dir/commanddeck.schema.json"
fi
while IFS= read -r asset; do
  install -m755 "$asset" "$install_dir/$(basename -- "$asset")"
done < <(find "$source_dir" -maxdepth 1 -type f -name '*.so' -print)
# The published picker directory is the delivered inventory; copy it whole and
# drop assets an earlier version delivered.
test -f "$source_dir/picker/shell.qml" || {
  echo "The publish output has no picker; republish: ./scripts/publish-aot.sh $rid" >&2
  exit 2
}
mkdir -p "$install_dir/picker"
find "$install_dir/picker" -maxdepth 1 -type f -delete
while IFS= read -r asset; do
  install -m644 "$asset" "$install_dir/picker/$(basename -- "$asset")"
done < <(find "$source_dir/picker" -maxdepth 1 -type f -print)
ln -sfn "$install_dir/commanddeck" "$bin_dir/commanddeck"

echo "Installed CommandDeck in $install_dir"
echo "Run: commanddeck config init && commanddeck doctor && commanddeck reindex"
