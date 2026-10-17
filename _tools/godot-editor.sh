# _tools/godot-editor.sh - resolve the pinned Godot editor and verify it.
# Sourced by build_linux.sh and build_windows.sh.
#
# Resolution order: an explicit path, then GODOT_BIN (what
# install_deps.sh --print-versions reports), then godot on PATH. PATH is the
# last resort on purpose: a distro package or another Godot version may sit
# there, and the version check below is what stops a wrong engine from
# producing a build.
#
# The version check is not optional: the binary prints dots where the tag has
# dashes (4.8-dev7 -> 4.8.dev7.mono.official.abc1234), so it matches the tag
# normalized with tr '-' '.', plus the .mono marker of the .NET edition.
# Pure ASCII.

# Echoes the editor path, or fails with the reason on stderr.
godot_editor() {
    local candidate="$1" tag="$2" want have
    want="$(printf '%s' "$tag" | tr '-' '.')"
    candidate="${candidate:-$(command -v godot 2>/dev/null || true)}"
    if [ -z "$candidate" ]; then
        echo "no Godot editor found: install one with 'bash install_deps.sh', or set GODOT_BIN" >&2
        return 2
    fi
    if [ ! -x "$candidate" ]; then
        echo "Godot editor is not executable: $candidate" >&2
        return 2
    fi
    have="$("$candidate" --version 2>/dev/null | head -n 1)"
    case "$have" in
        *"$want"*mono*) printf '%s\n' "$candidate"; return 0 ;;
        *) echo "wrong Godot: $candidate reports '$have', want $tag (.NET edition)" >&2
           return 2 ;;
    esac
}
