using System.IO;
using BloodborneLauncher.Build;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// Where things live. The engine folder holds out\bb-probe.exe, scripts\run_windows.py and a
    /// bundled Python; bbport.ini, mods.json and patches.json live in its data folder (BB_DATA_DIR,
    /// by default the engine folder itself), as run_windows.py expects them.
    /// Launcher settings are per-user, in %APPDATA%\bbport-launcher\settings.json.
    /// On Linux the engine folder holds run.sh, scripts/run_game.py and out/bb-probe (bin/bb-probe
    /// in a package), and the settings are in ~/.config/bbport-launcher/settings.json.
    /// </summary>
    public static class Paths
    {
        // DoNotVerify: the folder's path even before it exists (a new account's ~/.config); without
        // it the path is empty, and the settings went to a bbport-launcher folder wherever the
        // launcher was started. Saving creates it.
        public static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "bbport-launcher");

        public static readonly string ConfigFile = Path.Combine(ConfigDir, "settings.json");

        /// <summary>The port folder in use: the "port_dir" setting, else one found by ResolvePort.</summary>
        public static string PortDir { get; set; } = "";

        public static string DataDir =>
            Environment.GetEnvironmentVariable("BB_DATA_DIR") is { Length: > 0 } dir ? dir : PortDir;

        public static string IniPath =>
            Environment.GetEnvironmentVariable("BB_CONFIG") is { Length: > 0 } path
                ? path
                : Path.Combine(DataDir, "bbport.ini");

        /// <summary>The engine and its DLLs: out, or bin in a packaged Linux build (the AppImage).</summary>
        public static string BinariesDir =>
            !OperatingSystem.IsWindows() && !File.Exists(Path.Combine(PortDir, "out", EngineExe)) &&
            File.Exists(Path.Combine(PortDir, "bin", EngineExe))
                ? Path.Combine(PortDir, "bin")
                : Path.Combine(PortDir, "out");

        /// <summary>Where NVIDIA's DLSS runtime is looked for and downloaded to: BB_DLSS_DIR (the
        /// AppImage's data folder, as its engine folder is read-only), else beside the engine.</summary>
        public static string DlssDir =>
            Environment.GetEnvironmentVariable("BB_DLSS_DIR") is { Length: > 0 } dir ? dir : BinariesDir;

        /// <summary>NVIDIA's DLSS runtime beside the engine (out\nvngx_dlss.dll; on Linux
        /// out/libnvidia-ngx-dlss.so.&lt;version&gt;, tools/fetch_dlss.sh), or null.</summary>
        public static string? DlssLibrary
        {
            get
            {
                if (OperatingSystem.IsWindows())
                    return File.Exists(Path.Combine(BinariesDir, "nvngx_dlss.dll")) ? Path.Combine(BinariesDir, "nvngx_dlss.dll") : null;
                try
                {
                    return Directory.Exists(DlssDir)
                        ? Directory.GetFiles(DlssDir, "libnvidia-ngx-dlss.so.*").OrderDescending().FirstOrDefault()
                        : null;
                }
                catch (IOException)
                {
                    return null;
                }
            }
        }

        /// <summary>The engine's executable: bb-probe.exe on Windows, bb-probe elsewhere.</summary>
        public static string EngineExe => OperatingSystem.IsWindows() ? "bb-probe.exe" : "bb-probe";

        /// <summary>The Bloodborne PC engine: scripts\run_windows.py and out\bb-probe.exe; on Linux
        /// run.sh, scripts/run_game.py and out/bb-probe (or bin/bb-probe).</summary>
        public static bool IsPortDir(string? dir) =>
            !string.IsNullOrWhiteSpace(dir) && (OperatingSystem.IsWindows()
                ? File.Exists(Path.Combine(dir, "scripts", "run_windows.py")) &&
                  File.Exists(Path.Combine(dir, "out", "bb-probe.exe"))
                : File.Exists(Path.Combine(dir, "run.sh")) &&
                  File.Exists(Path.Combine(dir, "scripts", "run_game.py")) &&
                  (File.Exists(Path.Combine(dir, "out", "bb-probe")) || File.Exists(Path.Combine(dir, "bin", "bb-probe"))));

        public static string Python => OperatingSystem.IsWindows() ? Path.Combine(PortDir, "tools", "python", "python.exe") : "python3";

        /// <summary>The port next to the game folder (its usual layout: &lt;port&gt;\CUSA03173).</summary>
        public static string? PortBesideGame(string gameDir)
        {
            try
            {
                var parent = string.IsNullOrWhiteSpace(gameDir) ? null : Directory.GetParent(gameDir)?.FullName;
                return IsPortDir(parent) ? parent : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A port among the folders beside the game folder (E:\games\CUSA03173 next to
        /// E:\games\bbport-windows), the one with the newest bb-probe.exe.</summary>
        public static string? PortNearGame(string gameDir)
        {
            try
            {
                var parent = string.IsNullOrWhiteSpace(gameDir) ? null : Directory.GetParent(gameDir)?.FullName;
                if (parent == null) return null;
                return Directory.GetDirectories(parent)
                    .Where(IsPortDir)
                    .OrderByDescending(dir => new[] { "bin", "out" }
                        .Select(b => new FileInfo(Path.Combine(dir, b, EngineExe)))
                        .Where(f => f.Exists).Select(f => f.LastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max())
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A port folder above this exe (the launcher copied into the port), or the Linux
        /// engine beside it: engineLinux/bloodborne_pc in the checkout it was built in
        /// (engineLinux/setup.sh), engine/bloodborne_pc in the AppImage (packaging/appimage.sh).</summary>
        public static string? DetectPortDir()
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    if (IsPortDir(dir.FullName))
                        return dir.FullName.TrimEnd(Path.DirectorySeparatorChar);
                    foreach (var linux in new[] { Path.Combine(dir.FullName, "engineLinux", "bloodborne_pc"),
                                                  Path.Combine(dir.FullName, "engine", "bloodborne_pc") })
                        if (IsPortDir(linux))
                            return linux;
                }
            return null;
        }

        /// <summary>launcher/windows in the source checkout, when this copy was built from it.</summary>
        public static string? SourceRoot => SourceStamp.LocateSources(AppContext.BaseDirectory);

        public static string Expand(string path) =>
            Environment.ExpandEnvironmentVariables(path.StartsWith("~")
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..]
                : path);

        public static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                if (OperatingSystem.IsWindows())
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"")
                        { UseShellExecute = true });
                else
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open") { ArgumentList = { path } })?.Dispose();
            }
            catch
            {
                // Nothing useful to do if Explorer will not open.
            }
        }
    }
}
