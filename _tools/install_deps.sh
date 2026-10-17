#!/usr/bin/env bash
# install_deps.sh - dev-dependency installer: .NET SDK + Godot .NET edition.
# Usage:  bash install_deps.sh [--system | --link-dir DIR]
#         bash install_deps.sh --print-versions
#
# The toolchain versions live here and nowhere else: the Dockerfile takes
# them as build args and _tools/image-tag.sh derives the image tag from
# them, so local, CI and the builder image can never drift apart.
#
# The Godot editor is installed as `godot` under --link-dir ($HOME/.local/bin
# by default), so shells find it by name. Nothing else has to know where it
# went: --print-versions reports the resolved paths, the build scripts use
# those, and every caller verifies the version before exporting.
# Pure ASCII.
set -euo pipefail

DOTNET_CHANNEL="11.0"
DOTNET_QUALITY="preview"   # no stable .NET 11 yet (stable lands Nov 2026); use "10.0"/stable for LTS
DOTNET_DIR="$HOME/.dotnet"
GODOT_TAG="4.8-dev7"
GODOT_ZIP="Godot_v${GODOT_TAG}_mono_linux_x86_64.zip"
GODOT_URL="https://github.com/godotengine/godot-builds/releases/download/${GODOT_TAG}/${GODOT_ZIP}"
GODOT_TEMPLATES_TPZ="Godot_v${GODOT_TAG}_mono_export_templates.tpz"
GODOT_TEMPLATES_URL="https://github.com/godotengine/godot-builds/releases/download/${GODOT_TAG}/${GODOT_TEMPLATES_TPZ}"
# Where Godot itself looks for export templates. Not ours to choose.
GODOT_TEMPLATE_DIR="${GODOT_TEMPLATE_DIR:-${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/$(printf '%s' "$GODOT_TAG" | tr '-' '.').mono}"
# Where `godot` goes. --system sets this to /usr/local/bin; an inherited
# GODOT_LINK_DIR also wins, which is how the image asks for /usr/local/bin
# without needing a second mechanism for it.
GODOT_LINK_DIR="${GODOT_LINK_DIR:-$HOME/.local/bin}"

usage() {
    # The header comment block, without the leading '# '.
    awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
}

while [ $# -gt 0 ]; do
    case "$1" in
        --system)
            GODOT_LINK_DIR="/usr/local/bin"
            ;;
        --link-dir)
            shift || { echo "--link-dir needs a directory" >&2; exit 2; }
            GODOT_LINK_DIR="$1"
            ;;
        --print-versions)
            MODE=print
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
    shift
done

GODOT_BIN="$GODOT_LINK_DIR/godot"

if [ "${MODE:-}" = print ]; then
    printf 'DOTNET_CHANNEL=%s\n' "$DOTNET_CHANNEL"
    printf 'DOTNET_QUALITY=%s\n' "$DOTNET_QUALITY"
    printf 'DOTNET_DIR=%s\n' "$DOTNET_DIR"
    printf 'GODOT_TAG=%s\n' "$GODOT_TAG"
    printf 'GODOT_URL=%s\n' "$GODOT_URL"
    printf 'GODOT_TEMPLATES_URL=%s\n' "$GODOT_TEMPLATES_URL"
    printf 'GODOT_LINK_DIR=%s\n' "$GODOT_LINK_DIR"
    printf 'GODOT_BIN=%s\n' "$GODOT_BIN"
    printf 'GODOT_TEMPLATE_DIR=%s\n' "$GODOT_TEMPLATE_DIR"
    exit 0
fi

# The binary prints its version with dots where the tag has dashes
# (4.8-dev7 -> 4.8.dev7.mono.official.abc1234), and the .NET edition is
# marked .mono in the same string. Both must match.
version_matches() {
    local bin="$1" want
    want="$(printf '%s' "$GODOT_TAG" | tr '-' '.')"
    "$bin" --version 2>/dev/null | grep -qF -- "$want" &&
        "$bin" --version 2>/dev/null | grep -qF -- "mono"
}

need() { command -v "$1" >/dev/null 2>&1 || return 1; }

# --system needs a writable dir; without usable sudo that is a downgrade to
# the user-local one, not a failure: the build resolves GODOT_BIN by path.
ensure_link_dir() {
    if mkdir -p "$GODOT_LINK_DIR" 2>/dev/null && [ -w "$GODOT_LINK_DIR" ]; then
        return 0
    fi
    if need sudo && sudo -n true 2>/dev/null; then
        sudo mkdir -p "$GODOT_LINK_DIR" && return 0
        echo "    sudo mkdir failed for $GODOT_LINK_DIR"
    else
        echo "    no writable $GODOT_LINK_DIR and no passwordless sudo"
    fi
    echo "    continuing in $HOME/.local/bin"
    GODOT_LINK_DIR="$HOME/.local/bin"
    GODOT_BIN="$GODOT_LINK_DIR/godot"
    mkdir -p "$GODOT_LINK_DIR"
}

# The editor loads its .NET assemblies from a GodotSharp directory beside the
# executable: the binary alone aborts with ".NET: Assemblies not found". So
# install GodotSharp beside the executable; never install the binary alone.
install_godot_sharp() {
    local source="$1"
    [ -d "$source" ] || {
        echo "ERROR: GodotSharp assemblies are missing from the editor archive" >&2
        return 1
    }
    mv "$source" "$GODOT_LINK_DIR/GodotSharp"
}

install_editor() {
    local tmp src editor_dir
    ensure_link_dir
    if [ -e "$GODOT_BIN" ]; then
        if [ ! -x "$GODOT_BIN" ] || ! version_matches "$GODOT_BIN"; then
            echo "ERROR: $GODOT_BIN exists but is not Godot ${GODOT_TAG} (.NET)" >&2
            return 1
        fi
        if [ ! -d "$GODOT_LINK_DIR/GodotSharp" ]; then
            echo "ERROR: GodotSharp assemblies are missing beside $GODOT_BIN" >&2
            return 1
        fi
        echo "==> Godot already installed: $GODOT_BIN"
        return 0
    fi
    if [ -e "$GODOT_LINK_DIR/GodotSharp" ]; then
        echo "ERROR: $GODOT_LINK_DIR/GodotSharp exists without $GODOT_BIN" >&2
        return 1
    fi
    echo "==> Downloading Godot ${GODOT_TAG} (.NET edition) -> $GODOT_BIN"
    tmp="$(mktemp -d)"
    trap 'rm -rf "$tmp"' EXIT
    curl -sSL -o "$tmp/godot.zip" "$GODOT_URL"
    unzip -q -o "$tmp/godot.zip" -d "$tmp/editor"
    src="$(find "$tmp/editor" -maxdepth 2 -type f -name 'Godot_v*mono_linux*.x86_64' | head -n 1)"
    [ -n "$src" ] || { echo "    no editor binary in $GODOT_URL" >&2; exit 1; }
    editor_dir="$(dirname "$src")"
    install_godot_sharp "$editor_dir/GodotSharp"
    mv "$src" "$GODOT_BIN"
    chmod 0755 "$GODOT_BIN"
    rm -rf "$tmp"
    trap - EXIT
}

# Templates live where Godot looks for them.
install_templates() {
    local tmp
    if [ -n "$(ls -A "$GODOT_TEMPLATE_DIR" 2>/dev/null)" ]; then
        echo "==> Export templates already installed: $GODOT_TEMPLATE_DIR"
        return 0
    fi
    tmp="$(mktemp -d)"
    trap 'rm -rf "$tmp"' EXIT
    echo "==> Downloading export templates ${GODOT_TAG}"
    curl -sSL -o "$tmp/templates.tpz" "$GODOT_TEMPLATES_URL"
    echo "==> Installing export templates -> $GODOT_TEMPLATE_DIR"
    unzip -q -o "$tmp/templates.tpz" -d "$tmp/tpz"
    mkdir -p "$GODOT_TEMPLATE_DIR"
    mv "$tmp"/tpz/templates/* "$GODOT_TEMPLATE_DIR"/
    rmdir "$tmp"/tpz/templates
    rm -rf "$tmp"
    trap - EXIT
}

echo "==> Checking helpers (curl, unzip)..."
missing=""
need curl || missing="$missing curl"
need unzip || missing="$missing unzip"
if [ -n "$missing" ]; then
  echo "    installing:$missing"
  if need sudo; then sudo apt update && sudo apt install -y $missing
  elif [ "$(id -u)" = "0" ]; then apt update && apt install -y $missing
  else echo "ERROR: need$missing but no sudo. Install them and re-run." >&2; exit 1; fi
fi

echo "==> Installing .NET SDK ${DOTNET_CHANNEL} (${DOTNET_QUALITY}) -> ${DOTNET_DIR} ..."
if [ ! -x "$DOTNET_DIR/dotnet" ]; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --quality "$DOTNET_QUALITY" --install-dir "$DOTNET_DIR"
  rm -f /tmp/dotnet-install.sh
else
  echo "    already installed, skipping."
fi

for line in 'export DOTNET_ROOT="'$DOTNET_DIR'"' 'export PATH="'$DOTNET_DIR':'$DOTNET_DIR'/tools:$PATH"'; do
  grep -qxF "$line" ~/.bashrc || echo "$line" >> ~/.bashrc
done
export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_DIR:$DOTNET_DIR/tools:$PATH"

echo "==> Installed SDKs:"
dotnet --list-sdks

install_editor
install_templates

echo "==> Verifying Godot..."
version_matches "$GODOT_BIN" || {
    echo "ERROR: $GODOT_BIN is not Godot ${GODOT_TAG} (.NET): $("$GODOT_BIN" --version 2>&1 | head -n 1)" >&2
    exit 1
}
"$GODOT_BIN" --version

case ":$PATH:" in
    *":$GODOT_LINK_DIR:"*)
        echo "Godot is on this shell's PATH already."
        ;;
    *)
        echo "Note: shells started from now on find it ($GODOT_LINK_DIR is on the login PATH)."
        echo "      this shell needs: export PATH=\"$GODOT_LINK_DIR:\$PATH\""
        ;;
esac

echo
echo "DONE. Restart your shell (or: source ~/.bashrc)."
echo "Godot: $GODOT_BIN"
echo "Export templates: $GODOT_TEMPLATE_DIR"
