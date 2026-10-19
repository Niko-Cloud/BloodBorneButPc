using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using BloodborneLauncher.Core;
using FluentIcons.Common;

namespace BloodborneLauncher.Views
{
    /// <summary>The launcher itself: where the engine is, how the game is started, and updates.</summary>
    public sealed class SettingsPage : LauncherPage
    {
        private readonly TextBlock _portHint;
        private readonly StackPanel _customRows = new();
        private readonly TextBlock _updateHint;
        private readonly UiButton _updateNow;
        private readonly ComboBox _versions = new() { Width = 300, VerticalAlignment = VerticalAlignment.Center };
        private readonly UiButton _installVersion;
        private readonly TextBlock _versionHint;
        private IReadOnlyList<Updater.Release>? _releases;
        private bool _versionsLoading;

        public SettingsPage() : base("Settings", "The Bloodborne PC engine, how the game is started, and keeping this launcher up to date.")
        {
            Group("Appearance");
            Control layout = null!;
            var reverting = false;
            layout = ComboRow(Symbol.PanelLeft, "Layout", "Modern: tabs and compact rows. Classic: the sidebar and one card per setting",
                new[] { new Choice("Modern", "modern"), new Choice("Classic", "classic") },
                () => S.Str("ui_layout") == "classic" ? "classic" : "modern", v => S.Set("ui_layout", v), changed: () =>
                {
                    if (reverting || RestartForLayout()) return;
                    // Cancelled: put back the layout that is showing, so the next start doesn't switch either.
                    reverting = true;
                    var shown = SettingRow.Classic ? "classic" : "modern";
                    S.Set("ui_layout", shown);
                    Ws.SaveNow();
                    var index = shown == "classic" ? 1 : 0;
                    if (layout is Segmented segmented) segmented.Select(index, notify: false);
                    else if (layout is ComboBox box) box.SelectedIndex = index;
                    reverting = false;
                });

            Group("Bloodborne PC");
            Add(Card(Symbol.FolderLink, "Engine folder", "", Row(
                SmallButton("Browse", Symbol.FolderOpen, (_, _) => ChoosePort()),
                IconButton(Symbol.Open, "Open the folder", (_, _) => Paths.OpenFolder(Paths.PortDir)),
                IconButton(Symbol.ArrowUndo, "Detect automatically", (_, _) =>
                {
                    S.Set("port_dir", "");
                    Ws.ResolvePort();
                    Ws.SaveSoon();
                    Refresh();
                })), out _portHint));

            Group("Launch");
            SettingCombo(Symbol.Rocket, "Start the game with", null, Choices.LaunchMethods, "launch_method", changed: () => { ShowMethod(); Ws.SaveSoon(); });

            TextRow(Symbol.Code, "Command",
                OperatingSystem.IsWindows()
                    ? "A program to run instead of Bloodborne.exe --run, with the settings as environment variables"
                    : "A program to run instead of run.sh, with the settings as environment variables",
                OperatingSystem.IsWindows() ? @"C:\path\to\game.exe" : "/path/to/program",
                () => S.Str("custom_command"), v => S.Set("custom_command", v), _customRows, 380);
            TextRow(Symbol.TextDescription, "Arguments", null, "Optional",
                () => S.Str("custom_args"), v => S.Set("custom_args", v), _customRows, 380);
            Body.Children.Add(_customRows);

            Group("Performance");
            SettingCombo(Symbol.Flow, "Two-stage GPU pipeline", "20 to 30% faster; turn off if the game is unstable",
                Choices.DrawPipe, "draw_pipe");
            SettingCombo(Symbol.ArrowSwap, "CPU reads of GPU data", "Relaxed is the default", Choices.Readbacks, "readbacks");
            SettingToggle(Symbol.Speaker2, "Sound threads first",
                OperatingSystem.IsWindows()
                    ? "For CPUs with many slower cores (older Xeons and similar): the game's sound runs before its other threads, against late or stuttering sound. Off by default"
                    : "For CPUs with many slower cores: the game's sound runs before its other threads, against late or stuttering sound. " +
                      "Linux allows it up to the system's limit (ulimit -e). Off by default",
                "audio_priority");
            // DirectSound is Windows' (SDL picks PipeWire or PulseAudio on Linux).
            if (OperatingSystem.IsWindows())
                SettingCombo(Symbol.Speaker2, "Sound output",
                    "If the sound jitters or crackles, try DirectSound. The larger buffer is steadier but the sound comes a little later",
                    Choices.SoundOutput, "sound_output");

            Group("Diagnostics");
            SettingToggle(Symbol.TextBulletListSquare, "Frame statistics in the log", "BB_FRAME_STATS", "frame_stats");
            SettingToggle(Symbol.DeveloperBoard, "FSR 4 profile in the log", "BB_FSR4_PROFILE", "fsr_profile");
            SettingToggle(Symbol.Speaker2, "Audio statistics in the log", "BB_AUDIO_STATS", "audio_stats");
            SettingToggle(Symbol.HourglassHalf, "Loading profile in the log",
                "BB_SAMPLE_THREADS: where the game's main, render and file threads spend their time, every 5 seconds. For slow loading; costs a little speed", "load_profile");
            SettingToggle(Symbol.Timer, "Render stage timings in the log", "BB_SECTIONS: where each frame's CPU time goes (with frame statistics). It costs FPS, so it applies to the next game only", "section_stats");
            TextRow(Symbol.Code, "Extra variables", "NAME=value pairs separated by spaces, passed to the game", "BB_EXAMPLE=1",
                () => S.Str("extra_env"), v => S.Set("extra_env", v.Trim()), width: 300);

            Group("Files");
            Add(Card(Symbol.DocumentSettings, "Launcher settings", Paths.ConfigFile,
                IconButton(Symbol.Open, "Open the folder", (_, _) => Paths.OpenFolder(Paths.ConfigDir)), out _));
            Add(Card(Symbol.DocumentSettings, "Port settings (bbport.ini)", Paths.IniPath,
                IconButton(Symbol.Open, "Open the folder", (_, _) => Paths.OpenFolder(Path.GetDirectoryName(Paths.IniPath)!)), out _));
            Add(Card(Symbol.Broom, "Shader cache",
                "The game's compiled shaders, kept between starts (a new engine version clears them itself). Clear them if " +
                "something draws wrong in every save, such as missing skin or hair; the next start compiles them again and stutters for a while",
                SmallButton("Clear", Symbol.Broom, (_, _) => ClearShaderCache()), out _));

            Group("Share settings");
            Add(Card(Symbol.Share, "Share your settings",
                "Graphics, effects, camera, controls, cheats and engine options, as a code to paste in Discord or a file. " +
                "Not included: display and HDR, the online account, folders and languages",
                Row(SmallButton("Copy code", Symbol.Copy, (_, _) => CopySettingsCode()),
                    SmallButton("Save as file", Symbol.Save, (_, _) => SaveSettingsFile())), out _));
            Add(Card(Symbol.ArrowImport, "Import settings", "Someone's code or file. You see what changes before anything is applied",
                Row(SmallButton("Paste code", Symbol.ClipboardPaste, (_, _) => ImportSettingsCode()),
                    SmallButton("Open file", Symbol.FolderOpen, (_, _) => ImportSettingsFile())), out _));

            Group("Community");
            Add(Card(Symbol.PeopleCommunity, "Discord", "Help, news, co-op partners and beta feedback",
                SmallButton("Join", Symbol.Open, (_, _) => Ui.Open(LauncherSettings.Discord)), out _));

            Group("Updates");
            _updateNow = SmallButton(OperatingSystem.IsWindows() ? "Update now" : "See release", Symbol.ArrowDownload,
                async (_, _) => await MainWindow.Instance!.InstallUpdateAsync(), ControlAppearance.Primary);
            Add(Card(Symbol.ArrowDownload, "Launcher updates", "", Row(
                SmallButton("Check now", Symbol.ArrowClockwise, async (_, _) => await Ws.CheckForUpdatesAsync(quiet: false)),
                _updateNow,
                IconButton(Symbol.Open, "Open the releases page", (_, _) => Ui.Open(Updater.ReleasesPage))), out _updateHint));
            ComboRow(Symbol.Branch, "Update channel",
                "Stable: releases that have been tested. Beta: every change as soon as it is done, before it is tested",
                new[] { new Choice("Stable", "stable"), new Choice("Beta", "beta") },
                () => Updater.BetaChannel(S) ? "beta" : "stable",
                v =>
                {
                    if (v == (Updater.BetaChannel(S) ? "beta" : "stable")) return;
                    S.Set("update_channel", v);
                    Ws.SaveSoon();
                    Ws.Log(v == "beta" ? "Update channel: beta." : "Update channel: stable.", LogLevel.Step);
                    _ = Ws.CheckForUpdatesAsync(quiet: false);
                });
            SettingToggle(Symbol.Alert, "Check for updates at start", $"Looks at github.com/{Updater.Repository}/releases", "check_updates");
            _installVersion = SmallButton("Install", Symbol.ArrowDownload, async (_, _) => await InstallChosenVersionAsync());
            _installVersion.IsEnabled = false;
            _versions.SelectionChanged += (_, _) =>
                _installVersion.IsEnabled = _versions.SelectedItem is ComboBoxItem { Tag: Updater.Release r } && !r.IsCurrent;
            // Releases carry the Windows package only, so there is nothing to switch to on Linux.
            if (OperatingSystem.IsWindows())
                Add(Card(Symbol.History, "Version",
                    "Every published release, stable and beta, newest first. Install any of them, older ones too, to go back if a " +
                    "version gives you trouble. Your settings, saves, mods and profiles stay",
                    Row(_versions, _installVersion,
                        IconButton(Symbol.ArrowClockwise, "Reload the list", async (_, _) => await LoadVersionsAsync(force: true))),
                    out _versionHint));
            else
                _versionHint = new TextBlock();
            SetHint(_versionHint, $"You have {Updater.CurrentLabel}");
            Ws.UpdateStateChanged += UpdateUpdates;
            UpdateUpdates();

            Ws.PortChanged += Refresh;
        }

        public override void OnShown()
        {
            Refresh();
            ShowMethod();
            if (OperatingSystem.IsWindows())
                _ = LoadVersionsAsync(force: false);
        }

        /// <summary>Fills the version selector from the releases on GitHub (once, or again on reload).</summary>
        private async Task LoadVersionsAsync(bool force)
        {
            if (_versionsLoading || (_releases != null && !force)) return;
            _versionsLoading = true;
            _versions.IsEnabled = false;
            _installVersion.IsEnabled = false;
            _versions.Items.Clear();
            _versions.Items.Add(new ComboBoxItem { Content = "Loading\u2026" });
            _versions.SelectedIndex = 0;
            try
            {
                _releases = await Updater.AllAsync();
                _versions.Items.Clear();
                ComboBoxItem? current = null;
                foreach (var release in _releases)
                {
                    var label = Updater.Label(release.Version, release.Beta) +
                                (release.Beta > 0 ? "" : "  \u00b7 stable") +
                                (release.IsCurrent ? "  (installed)" : "");
                    var item = new ComboBoxItem { Content = label, Tag = release };
                    _versions.Items.Add(item);
                    if (release.IsCurrent) current = item;
                }
                _versions.SelectedItem = current ?? (_versions.Items.Count > 0 ? _versions.Items[0] : null);
                _versions.IsEnabled = _versions.Items.Count > 0;
                SetHint(_versionHint, _releases.Count == 0
                    ? $"You have {Updater.CurrentLabel}; no release is published yet"
                    : $"You have {Updater.CurrentLabel}. {_releases.Count} versions to choose from");
            }
            catch (Exception e)
            {
                _releases = null;
                _versions.Items.Clear();
                SetHint(_versionHint, $"You have {Updater.CurrentLabel}. The list could not be loaded: {e.GetBaseException().Message}");
            }
            finally
            {
                _versionsLoading = false;
            }
        }

        private async Task InstallChosenVersionAsync()
        {
            if (_versions.SelectedItem is not ComboBoxItem { Tag: Updater.Release release }) return;
            await MainWindow.Instance!.InstallVersionAsync(release);
        }

        private void Refresh()
        {
            SetHint(_portHint, Paths.IsPortDir(Paths.PortDir)
                ? Paths.PortDir + (Paths.DetectPortDir() == Paths.PortDir ? "  \u00b7  the package this launcher is in" : "") +
                  (OperatingSystem.IsWindows()
                      ? "\nThe engine that runs your game: out\\bb-probe.exe, its scripts and Python. Your game files stay where they are"
                      : "\nThe engine that runs your game: run.sh, its scripts and out/bb-probe. Your game files stay where they are")
                : OperatingSystem.IsWindows()
                    ? "Not found. It is normally the folder this launcher is in (with bin\\bb-probe.exe and run.py)"
                    : "Not found. Choose the engine's folder: the one with run.sh, scripts/run_game.py and a built out/bb-probe");
        }

        private void UpdateUpdates()
        {
            var update = Ws.Update;
            var latest = Ws.Latest;
            _updateNow.IsVisible = update != null;
            if (!OperatingSystem.IsWindows())
                _updateNow.Label = update != null && Updater.CanInstallAppImage(update) ? "Update now" : "See release";
            SetHint(_updateHint,
                update != null ? $"{update.Name} is available (you have {Updater.CurrentLabel})" +
                                 (update.IsNewerThan(Updater.Current, Updater.CurrentBeta) ? "" : ": back to stable")
                : Ws.CheckedAt == null ? $"You have {Updater.CurrentLabel}"
                : latest == null ? $"You have {Updater.CurrentLabel}; no release is published yet"
                : $"You have {Updater.CurrentLabel}, the latest ({latest.Tag})");
        }

        /// <summary>Restarts into the chosen layout; false if the player cancelled.</summary>
        private static bool RestartForLayout()
        {
            if (!Prompt.Confirm("Switch layout", "The launcher restarts to change its layout.", "Restart now")) return false;
            Ui.Restart();
            return true;
        }

        /// <summary>Deletes the engine's shader and pipeline caches (user/cache beside the engine;
        /// scripts/run_game.py also clears them when the engine changes).</summary>
        private static void ClearShaderCache()
        {
            if (Process.GetProcessesByName("bb-probe").Length > 0)
            {
                Prompt.Tell("Shader cache", "Close the game first: it is using the cache.");
                return;
            }
            var cache = Path.Combine(Paths.PortDir, "user", "cache");
            var failed = 0;
            if (Directory.Exists(cache))
            {
                foreach (var entry in new DirectoryInfo(cache).EnumerateFileSystemInfos())
                {
                    if (entry.Name == "engine.txt") continue;
                    try
                    {
                        if (entry is DirectoryInfo directory) directory.Delete(recursive: true);
                        else entry.Delete();
                    }
                    catch (Exception) { failed++; }
                }
            }
            Ws.Log(failed == 0 ? "Shader cache cleared: the next start compiles the shaders again."
                               : $"Shader cache: {failed} entries could not be removed ({cache}).",
                   failed == 0 ? LogLevel.Good : LogLevel.Warn);
        }

        // ---------------------------------------------------------------- shared settings

        private void CopySettingsCode()
        {
            Ui.CopyText(SharedSettings.ToCode(SharedSettings.Take(Ini, S)));
            Ws.Log("Settings code copied. Paste it in Discord; others import it in Settings > Share settings.", LogLevel.Good);
            Prompt.Tell("Settings code copied", "Paste it in Discord or anywhere else. Others use Import settings > Paste code.");
        }

        private void SaveSettingsFile()
        {
            var file = Ui.SaveFile("Save settings", "Bloodborne PC settings.json", "Settings|*.json");
            if (file == null) return;
            try
            {
                File.WriteAllText(file, SharedSettings.ToJson(SharedSettings.Take(Ini, S)));
                Ws.Log("Settings saved: " + file, LogLevel.Good);
            }
            catch (Exception e)
            {
                Ws.Log("Could not save the settings: " + e.Message, LogLevel.Bad);
            }
        }

        private void ImportSettingsCode()
        {
            var clipboard = Ui.PasteText() ?? "";
            var values = Prompt.Ask("Import settings", $"Paste the code someone shared (it starts with {SharedSettings.CodePrefix}).",
                new Prompt.Field("Code", clipboard.Contains(SharedSettings.CodePrefix, StringComparison.OrdinalIgnoreCase) ? clipboard.Trim() : "",
                    SharedSettings.CodePrefix + "\u2026", Multiline: true));
            if (values == null || values[0].Trim().Length == 0) return;
            OfferSettings(values[0]);
        }

        private void ImportSettingsFile()
        {
            var file = Ui.OpenFile("Import settings", "Settings|*.json;*.txt|All files|*");
            if (file == null) return;
            try
            {
                if (new FileInfo(file).Length > 256 * 1024)
                    throw new FormatException("That file is far too large to be shared settings.");
                OfferSettings(File.ReadAllText(file));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                Prompt.Tell("Could not import", e.Message);
            }
        }

        /// <summary>Shows what imported settings change and, if accepted, applies them (the previous ones are
        /// kept in a file) and restarts the launcher so every page shows them.</summary>
        private void OfferSettings(string text)
        {
            if (Ws.Game.Running)
            {
                Prompt.Tell("Close the game first", "The game changes these settings too while it runs.");
                return;
            }
            SharedSettings.Snapshot shared;
            try
            {
                shared = SharedSettings.Parse(text);
            }
            catch (FormatException e)
            {
                Prompt.Tell("Could not import", e.Message);
                return;
            }

            var everything = new HashSet<SharedSettings.Part> { SharedSettings.Part.Settings, SharedSettings.Part.Controls, SharedSettings.Part.Cheats };
            var changes = SharedSettings.Changes(shared, Ini, S, everything);
            if (changes.Count == 0)
            {
                Prompt.Tell("Nothing to change", "These settings are the same as yours.");
                return;
            }
            var settings = changes.Count(c => c.Part == SharedSettings.Part.Settings);
            var controls = changes.Count(c => c.Part == SharedSettings.Part.Controls);
            var cheats = changes.Count(c => c.Part == SharedSettings.Part.Cheats);
            var counts = string.Join(", ", new[]
            {
                settings > 0 ? $"{settings} graphics and engine" : null,
                controls > 0 ? $"{controls} controls" : null,
                cheats > 0 ? $"{cheats} cheats" : null,
            }.Where(p => p != null));
            const int shown = 12;
            var list = string.Join("\n", changes.Take(shown).Select(SharedSettings.Describe)) +
                       (changes.Count > shown ? $"\n\u2026and {changes.Count - shown} more" : "");
            var message = $"From {shared.From}: {changes.Count} changes ({counts}).\n\n{list}\n\n" +
                          "Your current settings are saved first. The launcher restarts to show the new ones.";

            HashSet<SharedSettings.Part> parts;
            if (settings > 0 && controls + cheats > 0)
            {
                var choice = Prompt.Choose("Import settings", message, "Cancel", "Keep my controls and cheats", "Import all");
                if (choice <= 0) return;
                parts = choice == 2 ? everything : new() { SharedSettings.Part.Settings };
            }
            else if (!Prompt.Confirm("Import settings", message, "Import"))
            {
                return;
            }
            else
            {
                parts = everything;
            }

            try
            {
                var backup = Path.Combine(Paths.ConfigDir, "settings-before-import.json");
                File.WriteAllText(backup, SharedSettings.ToJson(SharedSettings.Take(Ini, S)));
                SharedSettings.Apply(shared, Ini, S, parts);
                Ws.Log($"Imported settings from {shared.From}. Your previous ones: {backup} (Import settings > Open file).", LogLevel.Good);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Prompt.Tell("Could not import", e.Message);
                return;
            }
            Ui.Restart();
        }

        private void ShowMethod() => _customRows.IsVisible = GameRunner.Custom(S);

        private void ChoosePort()
        {
            var path = PickFolder(OperatingSystem.IsWindows() ? "Engine folder (with run.py and bin\\bb-probe.exe)" : "Engine folder (with run.sh)", Paths.PortDir);
            if (path == null) return;
            if (!Paths.IsPortDir(path))
            {
                Prompt.Tell("Engine folder", OperatingSystem.IsWindows()
                    ? "That folder has no run.py with Bloodborne.exe or bin\\bb-probe.exe."
                    : "That folder has no run.sh with scripts/run_game.py and a built out/bb-probe (bash build.sh builds it).");
                return;
            }
            S.Set("port_dir", path);
            Ws.ResolvePort();
            Ws.SaveSoon();
            Refresh();
        }
    }
}
