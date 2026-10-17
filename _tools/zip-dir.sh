#!/usr/bin/env bash
# _tools/zip-dir.sh <dir> <out.zip>
# Zip a directory into out.zip. Uses python3 because runners and WSL images
# do not always ship a zip binary. Pure ASCII.
set -euo pipefail

DIR="$1"
OUT="$2"

python3 - "$DIR" "$OUT" <<'PYEOF'
import os
import sys
import zipfile

root, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    for dirpath, _, files in os.walk(root):
        for name in files:
            full = os.path.join(dirpath, name)
            z.write(full, os.path.relpath(full, root))
PYEOF

echo "wrote $OUT"
