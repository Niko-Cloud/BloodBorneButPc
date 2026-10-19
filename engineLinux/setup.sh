#!/usr/bin/env bash
# Makes the Linux engine in engineLinux/bloodborne_pc (or the folder given): DarkIzuku's engine at
# v0.4.0-darkizuku-preview.1 (that tag's commit: a tag can be moved, a commit cannot), Awakenchan's
# engine/bbhost-changes.patch as the Windows build takes it, the files that patch turns to CRLF line
# endings back to LF (bash and Python refuse CRLF), then linux-changes.patch from this folder. Build
# it with its build.sh, or let packaging/appimage.sh.
#
#   bash engineLinux/setup.sh [folder]
set -euo pipefail
here=$(cd -- "$(dirname -- "$0")" && pwd)
repo=$(dirname -- "$here")
dest=${1:-$here/bloodborne_pc}
[[ ! -e $dest ]] || { echo "$dest already exists: remove it to set it up again" >&2; exit 1; }

commit=8ecba9a4abdd54747e31d7fee54982e422b5f8e2 # v0.4.0-darkizuku-preview.1
git clone --no-checkout https://github.com/DarkIzuku/bloodborne_pc "$dest"
cd -- "$dest"
git checkout --quiet --detach "$commit"
git submodule update --init --recursive
git apply "$repo/engine/bbhost-changes.patch"
cp "$repo/engine/win_compat.h" gpu/

# A text file with CRLF lines that had no CR at the tag: the patch's own line endings, back to LF
# (not JSON, which reads the same either way; the tested engine kept it as the patch wrote it).
python3 -I - <<'PY'
import subprocess
from pathlib import Path
names = subprocess.run(['git', 'ls-files', '-co', '--exclude-standard', '-z'], capture_output=True, check=True).stdout
for name in filter(None, names.decode().split('\0')):
    path = Path(name)
    if not path.is_file() or path.is_symlink() or path.suffix == '.json':
        continue
    data = path.read_bytes()
    if b'\r\n' not in data or b'\0' in data:
        continue
    before = subprocess.run(['git', 'show', f'HEAD:{name}'], capture_output=True)
    if before.returncode == 0 and b'\r' in before.stdout:
        continue
    path.write_bytes(data.replace(b'\r\n', b'\n'))
PY

git apply --whitespace=nowarn "$here/linux-changes.patch"
echo "Ready: $dest (build it with: cd $dest && bash build.sh)"
