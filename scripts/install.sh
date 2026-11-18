#!/usr/bin/env bash
# Copy the staged mod into a game install's mods/ folder. Run by the player (the account that
# owns the game install), not by dev: it writes <game-dir>/mods/A10WinMarker/.
#   scripts/install.sh "<path to Slay the Spire 2>"
set -euo pipefail
id=A10WinMarker
src="$(cd "$(dirname "$0")/.." && pwd)/dist/$id"
game="${1:?usage: install.sh <game-dir>}"
[[ -f "$src/$id.dll" && -f "$src/$id.json" ]] || { echo "Run scripts/build.sh first ($src missing)" >&2; exit 1; }
[[ -d "$game" ]] || { echo "Not a directory: $game" >&2; exit 1; }
dest="$game/mods/$id"
mkdir -p "$dest"
cp "$src/$id.dll" "$src/$id.json" "$dest/"
echo "Installed $(cat "$src/BUILD_CONFIG" 2>/dev/null || echo '?') build to $dest"
