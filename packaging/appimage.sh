#!/usr/bin/env bash
# Builds dist/BloodbornePC-x86_64.AppImage: the Linux launcher (launcher/linux, self-contained
# .NET: nothing to install) and the Linux engine (engineLinux/bloodborne_pc, made by
# engineLinux/setup.sh when it is missing, or BB_ENGINE; built first with its build.sh),
# with the libraries the engine needs that a desktop may not have (SDL3, FFmpeg and its codecs,
# protobuf, ...). The system provides glibc, the Vulkan loader and GPU drivers, X11 and Wayland,
# the desktop's GUI libraries (GLib, fonts, Cairo, Pango) and Python 3 for the start scripts.
#
#   bash packaging/appimage.sh
#
# It sets the engine up and builds it first when that is not done (or it was built before packages
# could carry their libraries), and uses appimagetool from PATH, APPIMAGETOOL, or downloads release
# 1.9.1 into ~/.cache/bloodborne-pc (checked against its SHA-256). Needs: the engine's build
# packages (engineLinux/README.md), the .NET 10 SDK, curl.
#
# The AppImage runs where this system's glibc (and newer) is, on the CPUs its libraries are built
# for: packaging/container.sh builds it on Ubuntu 24.04 instead, for any 2024 or newer distribution
# and any x86-64 CPU (the releases are built so).
#
# BB_VERSION: the launcher's version (the release tag without its v; default: the .csproj's).
# BB_UPDATE_REPO: the GitHub repository whose latest release updates it (default:
# Niko-Cloud/BloodBorneButPc; empty: none). It is written into the AppImage for AppImage update
# tools, and dist/BloodbornePC-x86_64.AppImage.zsync made beside it: a release carries both, and an
# update downloads only what changed.
#
# Running the AppImage opens the launcher; --game starts the game alone with the saved settings.
# Data (bbport.ini, saves, mods, generated files, DLSS): ~/.local/share/bloodborne-pc (BB_DATA_DIR).
set -euo pipefail
repo=$(cd -- "$(dirname -- "$0")/.." && pwd)
engine=$(realpath -m "${BB_ENGINE:-$repo/engineLinux/bloodborne_pc}")
if [[ ! -e $engine ]]; then
    echo '[0/4] Engine setup (engineLinux/setup.sh)'
    bash "$repo/engineLinux/setup.sh" "$engine"
fi
[[ -f $engine/build.sh ]] || { echo "No engine in $engine" >&2; exit 1; }
command -v dotnet > /dev/null || { echo 'Needs the .NET 10 SDK (dotnet)' >&2; exit 1; }

# The engine finds the bundled libraries through its RPATH ($ORIGIN/../lib, build.sh); patchelf
# would break the non-PIE game binary, so an engine built before that is built again.
built() {
    for f in out/bb-probe out/gpu/libbbgpu.so out/bb-gpu-capabilities out/bb-engine-assets; do
        [[ -f $engine/$f ]] || return 1
    done
    readelf -d "$engine/out/bb-probe" | grep -q 'RPATH.*\$ORIGIN/\.\./lib'
}
# A RUNPATH in the GPU library would make the loader ignore bb-probe's RPATH for its libraries
# (FFmpeg, SDL3): the game would not start where the system lacks them.
no_runpath() {
    ! readelf -d "$engine/out/gpu/libbbgpu.so" | grep -q RUNPATH ||
        { echo "$engine/out/gpu/libbbgpu.so has a RUNPATH (a library outside the system's folders?)" >&2; exit 1; }
}
if ! built; then
    echo '[0/4] Engine build (bash build.sh: the first one takes a while)'
    (cd "$engine" && bash build.sh)
    built || { echo "$engine/out/bb-probe has no \$ORIGIN/../lib RPATH: the engine's build.sh is too old" >&2; exit 1; }
fi
no_runpath

# FSR 4 (the upscaler's default): its model and shaders, downloaded from the commit the engine
# names (tools/fetch_fsr4_assets.sh: files already there are kept), as the Windows package has them.
# Without them the game falls back to FSR 3.1.
echo '[0/4] FSR 4 assets'
(cd "$engine" && bash tools/fetch_fsr4_assets.sh > "$engine/out/fsr4-assets.log" 2>&1) ||
    { tail -20 "$engine/out/fsr4-assets.log" >&2; exit 1; }

# appimagetool: PATH, APPIMAGETOOL, else its 1.9.1 release, downloaded once and checked.
appimagetool=${APPIMAGETOOL:-$(command -v appimagetool || true)}
if [[ -z $appimagetool ]]; then
    cache=${XDG_CACHE_HOME:-$HOME/.cache}/bloodborne-pc
    appimagetool=$cache/appimagetool-1.9.1-x86_64.AppImage
    if [[ ! -x $appimagetool ]]; then
        mkdir -p "$cache"
        curl -fsSL --retry 3 -o "$appimagetool.part" \
            https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage
        echo "ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0  $appimagetool.part" | sha256sum -c --quiet ||
            { rm -f "$appimagetool.part"; echo 'appimagetool download does not match its checksum' >&2; exit 1; }
        chmod +x "$appimagetool.part"
        mv "$appimagetool.part" "$appimagetool"
    fi
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
app=$work/AppDir
e=$app/engine/bloodborne_pc

echo '[1/4] Launcher (dotnet publish)'
# ReadyToRun: compiled ahead of time, so the launcher opens without compiling itself first.
dotnet publish "$repo/launcher/linux" -c Release -r linux-x64 --self-contained -p:PublishReadyToRun=true \
    -p:InvariantGlobalization=true -p:DebugType=none ${BB_VERSION:+-p:Version="$BB_VERSION"} -o "$app/launcher" > "$work/publish.log" ||
    { tail -20 "$work/publish.log" >&2; exit 1; }

echo '[2/4] Engine'
mkdir -p "$e/bin/gpu" "$e/lib" "$e/tools"
cp "$engine/run.sh" "$engine/LICENSE" "$e/"
cp -r "$engine/scripts" "$engine/patches" "$e/"
[[ -d $engine/licenses ]] && cp -r "$engine/licenses" "$e/"
[[ -d $engine/fsr4_shaders ]] && cp -r "$engine/fsr4_shaders" "$e/"
find "$e/scripts" -name __pycache__ -prune -exec rm -rf {} +
cp "$engine/tools/fetch_dlss.sh" "$e/tools/"
install -m755 "$engine/out/bb-probe" "$engine/out/bb-gpu-capabilities" "$engine/out/bb-engine-assets" "$e/bin/"
# Line tables are all the crash reports use; the rest of the debug info is half the library.
strip --strip-debug -o "$e/bin/gpu/libbbgpu.so" "$engine/out/gpu/libbbgpu.so"

echo '[3/4] Libraries'
# Left to the system: glibc, the C++ runtime and OpenSSL (the GPU drivers and other libraries
# loaded into the game need the system's own, as new as theirs; any 2024 system has the ones the
# build needs), the GPU stack (it must match the installed driver), the display servers' libraries
# and the desktop's GUI and font libraries, which every desktop has and whose configuration files
# belong to the system's own versions.
system='^(ld-linux.*|libc|libm|libmvec|libdl|libpthread|librt|libresolv|libutil|libanl|libnss_.*|libgcc_s|libstdc\+\+|libssl|libcrypto|libvulkan|libGL.*|libEGL|libOpenGL|libdrm|libgbm|libva.*|libvdpau|libOpenCL|libX11.*|libX[a-z]+|libxcb.*|libxkbcommon.*|libwayland-.*|libsystemd|libudev|libz|libzstd|libexpat|libffi|libfontconfig|libfreetype|libharfbuzz|libgraphite2|libglib-2\.0|libgio-2\.0|libgobject-2\.0|libgmodule-2\.0|libpcre2-8|libmount|libblkid|libselinux|libcairo|libpango.*|libpixman-1|libgdk_pixbuf-2\.0|librsvg-2|libpng16|libfribidi|libthai|libdatrie|libasound|libpulse.*|libpipewire-.*|libdbus-1)\.so'
for elf in out/bb-probe out/gpu/libbbgpu.so out/bb-gpu-capabilities out/bb-engine-assets; do
    ldd "$engine/$elf"
done | awk '$2 == "=>" && $3 ~ /^\// { print $1, $3 }' | sort -u | while read -r name path; do
    name=${name##*/} # the loader comes as a path (/lib64/ld-linux-x86-64.so.2)
    [[ $name == libbbgpu.so ]] && continue
    [[ $name =~ $system ]] && continue
    cp -L "$path" "$e/lib/$name"
done
echo "      $(ls "$e/lib" | wc -l) libraries, $(du -sh "$e/lib" | cut -f1)"

echo '[4/4] AppImage'
cat > "$app/AppRun" <<'RUN'
#!/usr/bin/env bash
# The launcher, or with --game the game alone. The engine's folder in the AppImage is read-only:
# its data (and NVIDIA's DLSS runtime, downloaded on the Graphics page) go to the data folder.
here=$(dirname -- "$(readlink -f -- "$0")")
export BB_DATA_DIR=${BB_DATA_DIR:-${XDG_DATA_HOME:-$HOME/.local/share}/bloodborne-pc}
export BB_DLSS_DIR=${BB_DLSS_DIR:-$BB_DATA_DIR/dlss}
mkdir -p -- "$BB_DATA_DIR" "$BB_DLSS_DIR"
if [[ ${1:-} == --game ]]; then
    shift
    export BB_PREBUILT=1
    cd -- "$here/engine/bloodborne_pc" && exec bash run.sh "$@"
fi
exec "$here/launcher/bloodborne-launcher" "$@"
RUN
chmod +x "$app/AppRun"
cat > "$app/bloodborne-pc.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Bloodborne PC
Comment=Bloodborne on Linux: the launcher and the engine
Exec=AppRun
Icon=bloodborne-pc
Categories=Game;
Terminal=false
DESKTOP
# The launcher's icon (app.ico) at 256 x 256.
cp "$repo/packaging/bloodborne-pc.png" "$app/bloodborne-pc.png"
ln -s bloodborne-pc.png "$app/.DirIcon"
out=$repo/dist/BloodbornePC-x86_64.AppImage
mkdir -p "$repo/dist"
rm -f -- "$out.zsync"
# An appimagetool that is an AppImage itself runs without FUSE this way (and has zsyncmake).
tool=("$appimagetool")
[[ $appimagetool == *.AppImage ]] && tool+=(--appimage-extract-and-run)
update_repo=${BB_UPDATE_REPO-Niko-Cloud/BloodBorneButPc}
[[ -z $update_repo ]] || tool+=(-u "gh-releases-zsync|${update_repo%%/*}|${update_repo#*/}|latest|${out##*/}.zsync")
# In dist/: zsyncmake writes the .zsync in the current folder.
(cd "$repo/dist" && ARCH=x86_64 "${tool[@]}" --no-appstream "$app" "$out") > "$work/appimagetool.log" 2>&1 ||
    { tail -20 "$work/appimagetool.log" >&2; exit 1; }
if [[ -n $update_repo && ! -f $out.zsync ]]; then
    tail -20 "$work/appimagetool.log" >&2; echo "appimagetool made no $out.zsync" >&2; exit 1
fi
ls -lh "$out" ${update_repo:+"$out.zsync"}
