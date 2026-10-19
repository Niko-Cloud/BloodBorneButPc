#!/usr/bin/env bash
# Builds dist/BloodbornePC-x86_64.AppImage (and its .zsync) as the releases are: with
# packaging/appimage.sh in the Ubuntu 24.04 build image (packaging/Dockerfile), so it runs on any
# 2024 or newer distribution and any x86-64 CPU, whatever this system is.
#
#   bash packaging/container.sh
#
# Needs Docker or Podman (BB_DOCKER picks one). The image is built the first time (a few minutes,
# then cached; BB_IMAGE: one already built, used as it is). The engine it builds, ccache's cache and
# the NuGet packages stay in build/ for the next time; the engine is set up again when engineLinux,
# engine/ or the Dockerfile changes. engineLinux/bloodborne_pc is this system's build and is not used.
# BB_VERSION and BB_UPDATE_REPO go to appimage.sh.
set -euo pipefail
repo=$(cd -- "$(dirname -- "$0")/.." && pwd)
docker=${BB_DOCKER:-$(command -v docker || command -v podman || true)}
[[ -n $docker ]] || { echo 'Needs Docker or Podman' >&2; exit 1; }

image=${BB_IMAGE:-}
if [[ -z $image ]]; then
    image=bloodborne-pc-build
    "$docker" build -t "$image" "$repo/packaging"
fi

# What the engine is made from, and with: when it changes, the engine is set up again (ccache
# keeps the rebuild short).
stamp=$(cat "$repo/engineLinux/setup.sh" "$repo/engineLinux/linux-changes.patch" \
            "$repo/engine/bbhost-changes.patch" "$repo/engine/win_compat.h" "$repo/packaging/Dockerfile" |
        sha256sum | cut -d' ' -f1)
engine=$repo/build/engine
if [[ -e $engine && $(cat "$engine/.bb-setup" 2>/dev/null) != "$stamp" ]]; then
    echo "Engine sources changed: setting $engine up again"
    rm -rf -- "$engine"
fi
mkdir -p "$repo/build/home"

# The files it writes belong to this user, not root.
user=(--user "$(id -u):$(id -g)")
[[ $(basename -- "$docker") == podman ]] && user=(--userns=keep-id)
run() {
    "$docker" run --rm "${user[@]}" -v "$repo:/src" -w /src \
        -e HOME=/src/build/home -e CCACHE_DIR=/src/build/ccache -e CCACHE_MAXSIZE -e NUGET_PACKAGES=/src/build/nuget \
        -e BB_ENGINE=/src/build/engine -e BB_VERSION -e BB_UPDATE_REPO "$image" "$@"
}
if [[ ! -e $engine ]]; then
    run bash engineLinux/setup.sh build/engine
    printf '%s' "$stamp" > "$engine/.bb-setup"
fi
run bash packaging/appimage.sh
