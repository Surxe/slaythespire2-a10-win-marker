#!/usr/bin/env bash
# Build the mod and stage the loader layout in dist/A10WinMarker/.
#   scripts/build.sh            Release (no dev-console command)
#   scripts/build.sh Debug      Debug, includes the a10mark dev-console command
# Needs local.props (see local.props.example) pointing at the game's managed DLLs.
set -euo pipefail
cd "$(dirname "$0")/.."
id=A10WinMarker
config="${1:-Release}"
[[ "$config" == Release || "$config" == Debug ]] || { echo "Config must be Release or Debug, got '$config'" >&2; exit 1; }
out=dist/$id
dotnet build "src/$id" -c "$config" -nologo -v quiet
rm -rf "$out"
mkdir -p "$out"
cp "src/$id/bin/$config/net9.0/$id.dll" "src/$id/bin/$config/net9.0/$id.json" "$out/"
echo "$config" > "$out/BUILD_CONFIG"
echo "Staged $config build in $(pwd)/$out:"
ls -1 "$out"
