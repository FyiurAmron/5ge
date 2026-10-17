#!/usr/bin/env bash
# build_linux.sh - native Linux x86_64 export.
# Usage: bash build_linux.sh [godot-binary]
# Output: build/linux/EepchGodot.x86_64 + .pck + data_* (see export_presets.cfg).
# Uses the same dotnet sanitize shim as build_windows.sh (see that file).
# The editor is resolved and version-checked through _tools/godot-editor.sh,
# so a wrong Godot stops the export instead of producing a broken build.
# Pure ASCII.
set -euo pipefail
REPO="$(dirname "$(readlink -f "$0")")"
eval "$(bash "$REPO/_tools/install_deps.sh" --print-versions)"
# shellcheck source=_tools/godot-editor.sh
. "$REPO/_tools/godot-editor.sh"
GODOT="$(godot_editor "${1:-$GODOT_BIN}" "$GODOT_TAG")"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$REPO/tools/dotnet-sanitize:$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
echo "==> dotnet: $(which dotnet)"
dotnet --list-sdks
echo "==> Exporting Linux x86_64..."
rm -rf "$REPO/build/linux"
mkdir -p "$REPO/build/linux"
"$GODOT" --headless --path "$REPO" --export-release "Linux x86_64"
ls -la "$REPO/build/linux"
