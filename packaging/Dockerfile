# The AppImage's build system (packaging/container.sh, and the release workflow): Ubuntu 24.04, so
# the AppImage needs glibc 2.39 (2024 distributions: Ubuntu 24.04, Debian 13, Fedora 40, SteamOS
# 3.7 and newer) and every library it bundles is built for any x86-64 CPU, not only the build PC's.
# What Ubuntu 24.04 lacks or has too old for the engine is built here from the release the engine
# was tested with.
FROM ubuntu:24.04

ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential gcc-14 g++-14 cmake ninja-build pkg-config git curl ca-certificates \
        python3 perl file xz-utils zsync ccache \
        libvulkan-dev libboost-dev libxxhash-dev nlohmann-json3-dev \
        libavcodec-dev libavformat-dev libavutil-dev libswscale-dev libswresample-dev \
        libssl-dev zlib1g-dev libx11-dev \
        libxext-dev libxrandr-dev libxcursor-dev libxi-dev libxss-dev libxfixes-dev libxtst-dev \
        libxkbcommon-dev libwayland-dev wayland-protocols libdecor-0-dev libegl-dev libgl-dev \
        libdrm-dev libgbm-dev libpulse-dev libpipewire-0.3-dev libasound2-dev libdbus-1-dev \
        libudev-dev libibus-1.0-dev libusb-1.0-0-dev \
    && rm -rf /var/lib/apt/lists/*
ENV CC=gcc-14 CXX=g++-14

# Static where it can be (nothing more to bundle), with -fPIC: libbbgpu.so links them. SDL3, the
# one shared library, goes where the system's are: from /usr/local, CMake would give libbbgpu.so a
# RUNPATH, which makes the loader ignore bb-probe's RPATH (the AppImage's libraries) for it.
ARG CMAKE_ARGS="-G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=/usr/local -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DBUILD_SHARED_LIBS=OFF"
RUN set -eux; \
    build() { name=$1 url=$2 tag=$3; shift 3; \
        git clone --depth 1 --branch "$tag" --recurse-submodules --shallow-submodules "$url" "/tmp/$name"; \
        cmake -S "/tmp/$name/${src:-}" -B "/tmp/$name/build" $CMAKE_ARGS "$@"; \
        cmake --build "/tmp/$name/build"; cmake --install "/tmp/$name/build"; rm -rf "/tmp/$name"; }; \
    build vulkan-headers https://github.com/KhronosGroup/Vulkan-Headers v1.4.363; \
    build glslang https://github.com/KhronosGroup/glslang vulkan-sdk-1.4.363.0 -DENABLE_OPT=OFF -DGLSLANG_TESTS=OFF; \
    build spirv-cross https://github.com/KhronosGroup/SPIRV-Cross vulkan-sdk-1.4.363.0 -DSPIRV_CROSS_ENABLE_TESTS=OFF; \
    build fmt https://github.com/fmtlib/fmt 12.1.0 -DFMT_TEST=OFF -DFMT_DOC=OFF; \
    build zycore https://github.com/zyantific/zycore-c v1.5.2; \
    build zydis https://github.com/zyantific/zydis v4.1.1 -DZYAN_SYSTEM_ZYCORE=ON -DZYDIS_BUILD_TOOLS=OFF -DZYDIS_BUILD_EXAMPLES=OFF -DZYDIS_BUILD_MAN=OFF; \
    build robin-map https://github.com/Tessil/robin-map v1.4.1; \
    src=miniupnpc; build miniupnpc https://github.com/miniupnp/miniupnp miniupnpc_2_3_3 -DUPNPC_BUILD_SHARED=OFF -DUPNPC_BUILD_TESTS=OFF -DUPNPC_BUILD_SAMPLE=OFF; src=; \
    build protobuf https://github.com/protocolbuffers/protobuf v21.12 -Dprotobuf_BUILD_TESTS=OFF; \
    build sdl3 https://github.com/libsdl-org/SDL release-3.4.18 -DBUILD_SHARED_LIBS=ON -DSDL_STATIC=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF -DSDL_RPATH=OFF \
        -DCMAKE_INSTALL_PREFIX=/usr -DCMAKE_INSTALL_LIBDIR=lib/x86_64-linux-gnu; \
    ldconfig

# .NET 10 SDK for the launcher.
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /opt/dotnet \
    && rm /tmp/dotnet-install.sh && ln -s /opt/dotnet/dotnet /usr/local/bin/dotnet
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_ROOT=/opt/dotnet
