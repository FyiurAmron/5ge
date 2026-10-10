#!/usr/bin/env bash
# build_linux.sh - native Linux x86_64 export.
# Usage: bash build_linux.sh [godot-binary]
# Output: build/linux/EepchGodot.x86_64 + .pck + data_* (see export_presets.cfg).
# Uses the same dotnet sanitize shim as build_windows.sh (see that file).
# Pure ASCII.
set -euo pipefail
REPO="$(dirname "$(readlink -f "$0")")"
GODOT="${1:-$(command -v godot || echo "$HOME/.local/bin/godot")}"
[ -x "$GODOT" ] || { echo "no Godot editor found: $GODOT" >&2; exit 2; }
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$REPO/tools/dotnet-sanitize:$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
echo "==> dotnet: $(which dotnet)"
dotnet --list-sdks
echo "==> Exporting Linux x86_64..."
mkdir -p "$REPO/build/linux"
"$GODOT" --headless --path "$REPO" --export-release "Linux x86_64"
ls -la "$REPO/build/linux"
