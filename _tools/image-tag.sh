#!/usr/bin/env bash
# _tools/image-tag.sh - print the builder image tag for the current tree.
#
# The tag spells out the toolchain (.NET + Godot) and ends with a short
# Dockerfile hash, so a Dockerfile change always yields a new tag and an
# existing tag always means "image with exactly this content exists".
# Versions come from install_deps.sh - the single source of truth.
# Pure ASCII.
set -euo pipefail
TOOLS="$(dirname "$(readlink -f "$0")")"
ROOT="$(readlink -f "$TOOLS/..")"
eval "$(bash "$TOOLS/install_deps.sh" --print-versions)"
DHASH="$(sha256sum "$ROOT/Dockerfile" | cut -c1-12)"
printf 'net%s-%s-godot%s-d%s\n' \
    "$DOTNET_CHANNEL" "$DOTNET_QUALITY" "$GODOT_TAG" "$DHASH" \
    | tr '[:upper:]' '[:lower:]'
