#!/usr/bin/env bash
# build_windows.sh - Windows x86_64 export from Linux.
# Usage: bash build_windows.sh [godot-binary]
# Output: build/windows/EepchGodot.exe + .pck + data_* (see export_presets.cfg).
# Works around a Godot 4.8-dev7 mono bug: the editor spawns `dotnet publish`
# with a broken MSBUILD_EXE_PATH (SDK dir + "MSBuild.dll" without separator),
# which breaks SDK resolution (MSB4236). The tools/dotnet-sanitize shim unsets
# the poisoned MSBuild* variables before delegating to the real dotnet.
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
echo "==> Exporting Windows x86_64..."
rm -rf "$REPO/build/windows"
mkdir -p "$REPO/build/windows"
"$GODOT" --headless --path "$REPO" --export-release "Windows x86_64"
ls -la "$REPO/build/windows"
