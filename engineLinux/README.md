# Linux engine

The engine for Linux: DarkIzuku's [bloodborne_pc](https://github.com/DarkIzuku/bloodborne_pc) at
`v0.4.0-darkizuku-preview.1` with Awakenchan's `engine/bbhost-changes.patch` (as the Windows build),
then `linux-changes.patch`: the GCC build, one start script for both systems (`scripts/run_game.py`),
trophies recorded by the engine, thread priorities, `output_res = auto`, the FPS counter's CPU and GPU
use, and the library path an AppImage needs.

```bash
bash engineLinux/setup.sh          # engineLinux/bloodborne_pc
cd engineLinux/bloodborne_pc && bash build.sh
BB_GAME_DIR=/path/to/CUSA03173 bash run.sh
```

Build packages on Arch: `vulkan-headers boost robin-map spirv-cross zydis spirv-headers miniz nlohmann-json
miniupnpc sdl3 ffmpeg glslang xxhash cmake ninja`. `bash packaging/appimage.sh` does all of this and
makes the AppImage.
