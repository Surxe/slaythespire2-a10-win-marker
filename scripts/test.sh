#!/usr/bin/env bash
# Run the core unit tests. Needs no game files.
set -euo pipefail
cd "$(dirname "$0")/.."
exec dotnet test tests/A10WinMarker.Core.Tests "$@"
