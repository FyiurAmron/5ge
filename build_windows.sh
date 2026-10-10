#!/usr/bin/env bash
# build_windows.sh - Windows x86_64 export from Linux.
# Usage: bash build_windows.sh [godot-binary]
# Output: build/windows/EepchGodot.exe + .pck + data_* (see export_presets.cfg).
# Works around a Godot 4.8-dev7 mono bug: the editor spawns `dotnet publish`
# with a broken MSBUILD_EXE_PATH (SDK dir + "MSBuild.dll" without separator),
# which breaks SDK resolution (MSB4236). The tools/dotnet-sanitize shim unsets
# the poisoned MSBuild* variables before delegating to the real dotnet.
# Pure ASCII.
set -euo pipefail
REPO="$(dirname "$(readlink -f "$0")")"
GODOT="${1:-$(command -v godot || echo "$HOME/.local/bin/godot")}"
[ -x "$GODOT" ] || { echo "no Godot editor found: $GODOT" >&2; exit 2; }
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$REPO/tools/dotnet-sanitize:$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
echo "==> dotnet: $(which dotnet)"
dotnet --list-sdks
echo "==> Exporting Windows x86_64..."
mkdir -p "$REPO/build/windows"
"$GODOT" --headless --path "$REPO" --export-release "Windows x86_64"
ls -la "$REPO/build/windows"
