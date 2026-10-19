# Bloodborne PC

Bloodborne on Windows and Linux: [Awakenchan](https://github.com/Awakenchan)'s launcher and engine
([Awakenchan/BloodBorneButPc](https://github.com/Awakenchan/BloodBorneButPc)), built on bbport and shadPS4,
with a native Linux build: the engine built and run on Linux, and the launcher ported to Avalonia.

**No game files are included.** You need your own decrypted Bloodborne dump (CUSA03173) with update 1.09.

## Install

### Windows

1. Download `BloodbornePC-<version>-win64.zip` from [Awakenchan's Releases](https://github.com/Awakenchan/BloodBorneButPc/releases) and extract it anywhere.
2. Run `Bloodborne Launcher.exe` and pick your game folder (the one with `eboot.bin`).
3. Press **Play**. The launcher updates itself.

### Linux

**AppImage** (one file, nothing to install):

1. Download `BloodbornePC-x86_64.AppImage` from [Releases](https://github.com/Niko-Cloud/BloodBorneButPc/releases).
2. `chmod +x BloodbornePC-x86_64.AppImage` and run it (or double-click it).
3. Pick your game folder on the Play page and press **Play**. The launcher offers new releases; **Update** installs them,
   downloading only what changed.

It runs on 2024 and newer distributions (Ubuntu 24.04, Debian 13, Fedora 40, SteamOS 3.7, Arch and so on) and needs a
Vulkan driver and Python 3, which gaming setups have. Settings, saves and mods are kept in
`~/.local/share/bloodborne-pc`. For co-op behind a firewall (ufw is on by default on some distributions), allow
the game's port once: `sudo ufw allow 3658/udp`.

**Build the AppImage yourself**, the way the releases are built (needs only Docker or Podman):

```bash
git clone https://github.com/Niko-Cloud/BloodBorneButPc
cd BloodBorneButPc
bash packaging/container.sh   # dist/BloodbornePC-x86_64.AppImage and its .zsync
```

It builds in an Ubuntu 24.04 container ([packaging/Dockerfile](packaging/Dockerfile)), so the AppImage runs on every
distribution above and any x86-64 CPU. `bash packaging/appimage.sh` builds it on your own system instead (with the
engine's build packages from [engineLinux/README.md](engineLinux/README.md) and the .NET 10 SDK): quicker to try,
but it only runs on systems at least as new as yours.

**Releases** are built by GitHub Actions ([.github/workflows/linux-appimage.yml](.github/workflows/linux-appimage.yml)):
push a tag and the release with its AppImage follows.

```bash
git tag v1.0.0 && git push origin v1.0.0   # v1.0.0-beta.1: a pre-release
```

**Run from source** (for working on it):

```bash
bash engineLinux/setup.sh
cd engineLinux/bloodborne_pc && bash build.sh && cd ../..
cd launcher/linux && dotnet run
```

The launcher finds the engine in `engineLinux/bloodborne_pc` by itself. Without the launcher:
`BB_GAME_DIR=/path/to/CUSA03173 bash run.sh` in `engineLinux/bloodborne_pc`.

The folders: `launcher/windows` and `engine` are the Windows launcher and engine, as in Awakenchan's repository;
`launcher/linux` is the Linux launcher (Avalonia, sharing the Windows launcher's `Core`), and `engineLinux` the
Linux engine's changes on top of `engine`'s (see its README).

## Credits

| | |
|---|---|
| Launcher and engine changes | [Awakenchan/BloodBorneButPc](https://github.com/Awakenchan/BloodBorneButPc) |
| Engine starting point | [DarkIzuku/bloodborne_pc](https://github.com/DarkIzuku/bloodborne_pc) (built on [bbport](https://github.com/deadinside28/bloodborne_pc) and [shadPS4](https://github.com/shadps4-emu/shadPS4)) |
| Linux port (engine build, Avalonia launcher) | [Niko-Cloud](https://github.com/Niko-Cloud) |
| VRAM cleanup | adapted from [IFreemz/shadPS4-Bloodborne-DLSS-FSR](https://github.com/IFreemz/shadPS4-Bloodborne-DLSS-FSR) |
| Crash and controller fixes | adapted from [AwesomeObserver](https://github.com/AwesomeObserver/bloodborne_pc), [Supermedo](https://github.com/Supermedo/bloodborne_pc) and [deadinside28](https://github.com/deadinside28/bloodborne_pc) |
| Cheats and occlusion culling research | [droogie/bbhost](https://github.com/droogie/bbhost) |
| Controller mappings | [SDL_GameControllerDB](https://github.com/mdqinc/SDL_GameControllerDB) |
| Trophy list | [pyL1nx/bloodborne_pc](https://github.com/pyL1nx/bloodborne_pc) |
| UI libraries | [WPF-UI](https://github.com/lepoco/wpfui) (Windows), [Avalonia](https://github.com/AvaloniaUI/Avalonia) and [FluentIcons](https://github.com/davidxuang/FluentIcons) (Linux) |

## License

The launcher is GPL-2.0-or-later (see [LICENSE](LICENSE)). The engine is GPL-3.0-or-later; its licenses are in the package's `licenses/` folder.
