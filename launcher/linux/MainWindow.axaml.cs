using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using BloodborneLauncher.Core;
using BloodborneLauncher.Views;
using FluentIcons.Common;

namespace BloodborneLauncher
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<LogItem> _log = new();
        private readonly Dictionary<Type, LauncherPage> _pages = new();
        private bool _logExpanded;
        private bool _closing;

        public static MainWindow? Instance { get; private set; }

        /// <summary>Where Prompt draws its dialogs, over the whole window.</summary>
        public Panel DialogHost => DialogHostPanel;

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            LogList.ItemsSource = _log;
            var ws = Workspace.Current;
            ws.Logged += OnLogged;
            ws.Saved += () => SavedText.Text = $"Saved {DateTime.Now:HH:mm:ss}";
            ws.GameStateChanged += UpdatePlayButton;
            ws.GameCrashed += OnGameCrashed;
            ws.Initialise();

            // Layout, read once at start (Settings > Layout restarts the launcher to switch).
            var args = Environment.GetCommandLineArgs();
            var forced = Array.IndexOf(args, "--layout") is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;
            SettingRow.Classic = (forced ?? ws.Settings.Str("ui_layout")) == "classic";
            ScreenshotDir = Array.IndexOf(args, "--screenshot") is var shot and >= 0 && shot + 1 < args.Length ? args[shot + 1] : null;
            // Tall enough to show the longest page whole (--screenshot-height to choose).
            if (ScreenshotDir != null)
                Height = Array.IndexOf(args, "--screenshot-height") is var tall and >= 0 && tall + 1 < args.Length &&
                         double.TryParse(args[tall + 1], out var height) ? height : 3200;
            // --play (the Steam shortcut): the game straight away with the saved settings, the
            // launcher out of sight meanwhile (its prompts show on their own).
            _directPlay = args.Contains("--play");
            if (_directPlay)
                WindowState = WindowState.Minimized;
            Sidebar.IsVisible = SettingRow.Classic;
            TabStrip.IsVisible = !SettingRow.Classic;
            if (!SettingRow.Classic)
                ApplyGlass();

            VersionText.Text = Updater.CurrentLabel;

            ws.UpdateStateChanged += () =>
            {
                TitleUpdate.IsVisible = ws.Update != null;
                if (ws.Update != null) TitleUpdateText.Text = $"Update {ws.Update.Tag}";
            };

            Opened += async (_, _) =>
            {
                Updater.CleanUp();
                // Running a beta means choosing beta: kept after updating to a stable release.
                if (Updater.CurrentBeta > 0 && ws.Settings.Str("update_channel").Length == 0)
                {
                    ws.Settings.Set("update_channel", "beta");
                    ws.SaveSoon();
                }
                if (ws.Settings.Bool("check_updates") && ScreenshotDir == null)
                    _ = ws.CheckForUpdatesAsync(quiet: true);
                await Task.Yield();
                (SettingRow.Classic ? ClassicPlay : PlayItem).IsChecked = true;
                UpdatePlayButton();
                // --test-trophy: a sample notification, to check how it looks over the game.
                if (args.Contains("--test-trophy"))
                    TrophyToast.Announce(TrophyTracker.ById[14]);
                ws.Log(Paths.IsPortDir(Paths.PortDir)
                        ? $"Bloodborne Launcher ready. Engine: {Paths.PortDir}"
                        : "Bloodborne Launcher ready. Choose the engine folder in Settings.",
                    Paths.IsPortDir(Paths.PortDir) ? LogLevel.Good : LogLevel.Warn);
                // --test-update <url>: installs the AppImage at url over $APPIMAGE as an update would
                // (no prompt), the outcome on standard output.
                if (Array.IndexOf(args, "--test-update") is var update and >= 0 && update + 1 < args.Length)
                    Dispatcher.UIThread.Post(async () =>
                    {
                        var release = new Updater.Release(new Version(99, 0, 0), 0, "v99.0.0", "Test", "", "", null, null, null, 0, 0,
                            AppImageUrl: args[update + 1]);
                        try
                        {
                            await Updater.InstallAsync(release, package: false);
                            Console.WriteLine("update: installed");
                        }
                        catch (Exception e) { Console.WriteLine("update failed: " + e.Message); }
                        Close();
                    }, DispatcherPriority.Background);
                // --test-picker: the folder picker once, its answer on standard output.
                else if (args.Contains("--test-picker"))
                    Dispatcher.UIThread.Post(() =>
                    {
                        try { Console.WriteLine("picker: " + (Ui.PickFolder("Test", Paths.PortDir) ?? "(none)")); }
                        catch (Exception e) { Console.WriteLine("picker failed: " + e); }
                        Close();
                    }, DispatcherPriority.Background);
                else if (ScreenshotDir != null)
                    Dispatcher.UIThread.Post(() => _ = SaveScreenshotsAsync(), DispatcherPriority.Background);
                else if (_directPlay)
                    Dispatcher.UIThread.Post(() => _ = PlayDirectAsync(), DispatcherPriority.Background);
                // Once: the settings that suit this PC, shown before anything changes.
                else if (!ws.Settings.Bool("hardware_offered") && Paths.IsPortDir(Paths.PortDir))
                    Dispatcher.UIThread.Post(() => OfferBestSettings(firstRun: true), DispatcherPriority.Background);
            };
            Closing += OnClosing;
        }

        private bool _directPlay, _directCrashed;

        /// <summary>--play: starts the game; the launcher closes when it ends normally, and opens when
        /// the start did not happen or the game crashed (the crash dialog first).</summary>
        private async Task PlayDirectAsync()
        {
            var ws = Workspace.Current;
            Hide();
            await PlayOrStopAsync();
            if (!ws.Game.Running)
            {
                _directPlay = false;
                ShowLauncher();
                return;
            }
            ws.GameCrashed += (_, _) => _directCrashed = true;
            ws.GameStateChanged += () =>
            {
                if (!_directPlay || ws.Game.Running) return;
                _directPlay = false;
                if (_directCrashed)
                    ShowLauncher();
                else
                    Close();
            };
        }

        private void ShowLauncher()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>Shows what would change to suit this PC, and applies it on Apply.</summary>
        public void OfferBestSettings(bool firstRun)
        {
            var ws = Workspace.Current;
            ws.Settings.Set("hardware_offered", true);
            ws.SaveSoon();
            var hw = Hardware.Detect();
            var changes = hw.Recommend(ws.Settings, ws.Ini);
            if (changes.Count == 0)
            {
                if (!firstRun) Prompt.Tell("Best settings for this PC", $"{hw.Summary}\n\nYour settings already suit this PC.");
                return;
            }
            var list = string.Join("\n", changes.Select(c => $"{c.Label}: {c.From} → {c.To}"));
            if (!Prompt.Confirm(firstRun ? "Settings for this PC" : "Best settings for this PC",
                    $"{hw.Summary}\n\n{list}\n\nYou can change any of them later.", "Apply"))
                return;
            foreach (var change in changes) change.Apply();
            ws.SaveSoon();
            ws.Log("Settings for this PC applied: " + string.Join(", ", changes.Select(c => $"{c.Label.ToLowerInvariant()} {c.To}")) + ".", LogLevel.Good);
            ReloadPages();
        }

        /// <summary>Builds the pages again after settings changed underneath them, staying on the current one.</summary>
        public void ReloadPages()
        {
            var current = PageHost.Content as LauncherPage;
            _pages.Clear();
            if (current == null) return;
            var page = _pages[current.GetType()] = (LauncherPage)Activator.CreateInstance(current.GetType())!;
            PageHost.Content = page;
            page.OnShown();
        }

        /// <summary>The game ended with an error by itself: what happened, safe mode, a report to copy.</summary>
        private void OnGameCrashed(int code, SessionStats session)
        {
            var ws = Workspace.Current;
            var choice = Prompt.Choose("The game crashed",
                $"{CrashReport.Explain(code)} It ran for {SessionStats.Duration(session.Length)}.\n\n" +
                "Safe mode starts the next run with the experimental engine options and mods off. " +
                "Copy report puts the details and the end of the log on the clipboard, for a bug report.",
                "Close", "Copy report", "Use safe mode");
            if (choice == 1)
            {
                try
                {
                    Ui.CopyText(CrashReport.Build(ws.Settings, ws.Ini, code));
                    ws.Log("Crash report copied to the clipboard.", LogLevel.Good);
                }
                catch (Exception e)
                {
                    ws.Log("Could not copy the report: " + e.Message, LogLevel.Warn);
                }
            }
            else if (choice == 2)
            {
                ws.Settings.Set("safe_mode_next", true);
                ws.SaveSoon();
                ws.Log("The next start uses safe mode (once).", LogLevel.Step);
            }
        }

        /// <summary>
        /// Modern layout: glass panes over the game's own artwork. Glass.axaml recolours the shared
        /// brushes; the backdrop is the dump's sce_sys/pic1.png (or pic0), else the bundled art.
        /// </summary>
        private void ApplyGlass()
        {
            if (Application.Current is { } app)
                app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://bloodborne-launcher/"))
                {
                    Source = new Uri("avares://bloodborne-launcher/Themes/Glass.axaml"),
                });
            try
            {
                var game = GameEnvironment.GameDir(Workspace.Current.Settings);
                var art = new[] { "pic1.png", "pic0.png" }
                    .Select(n => System.IO.Path.Combine(game, "sce_sys", n))
                    .FirstOrDefault(System.IO.File.Exists);
                using var stream = art != null
                    ? System.IO.File.OpenRead(art)
                    : AssetLoader.Open(new Uri("avares://bloodborne-launcher/Assets/backdrop.jpg"));
                // Decoded tiny and scaled up smoothly: a soft blur on every renderer, effects or not.
                BackdropImage.Source = Bitmap.DecodeToWidth(stream, 64, BitmapInterpolationMode.HighQuality);
                RenderOptions.SetBitmapInterpolationMode(BackdropImage, BitmapInterpolationMode.HighQuality);
                BackdropImage.IsVisible = true;
                BackdropShade.IsVisible = true;
            }
            catch
            {
                // No artwork: the glass sits on the plain dark background.
            }
        }

        private void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            Workspace.Current.SaveNow();
            if (_closing || !Workspace.Current.Game.Running)
                return;

            e.Cancel = true;
            // As an AppImage the game runs from the launcher's mounted files, which go away with it.
            if (Updater.SelfAppImage != null)
            {
                if (Prompt.Choose("The game is still running", "The game runs from this AppImage: closing the launcher stops it too.",
                        "Cancel", "Stop and close") == 1)
                {
                    _closing = true;
                    _ = StopAndCloseAsync();
                }
                return;
            }
            var answer = Prompt.Choose("The game is still running", "Stop it and close the launcher?",
                "Cancel", "Leave it running", "Stop and close");
            if (answer == 1)
            {
                _closing = true;
                Close();
            }
            else if (answer == 2)
            {
                _closing = true;
                _ = StopAndCloseAsync();
            }
        }

        private async Task StopAndCloseAsync()
        {
            await Workspace.Current.StopGameAsync();
            Close();
        }

        // ---------------------------------------------------------------- navigation

        private void NavItem_Checked(object? sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton { IsChecked: true, Tag: Type pageType } item)
                return;

            if (!_pages.TryGetValue(pageType, out var page))
                _pages[pageType] = page = (LauncherPage)Activator.CreateInstance(pageType)!;
            PageHost.Content = page;
            page.OnShown();

            var name = item.Content as string ??
                       (item.Content as Panel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
            TitlePage.Text = string.IsNullOrEmpty(name) ? "" : $"  ·  {name}";
        }

        /// <summary>Shows a page: a tab, or one of More's ("online", "output", "mods", "dump", "advanced").</summary>
        public void Show(string page)
        {
            var item = SettingRow.Classic
                ? page switch
                {
                    "output" => ClassicOutput,
                    "online" => ClassicOnline,
                    "mods" => ClassicMods,
                    "dump" => ClassicDump,
                    "controls" => ClassicControls,
                    _ => ClassicPlay,
                }
                : page switch
                {
                    "output" or "online" or "mods" or "dump" or "advanced" => MoreItem,
                    "controls" => ControlsItem,
                    _ => PlayItem,
                };
            if (item.IsChecked == true)
                NavItem_Checked(item, new RoutedEventArgs());
            item.IsChecked = true;
            if (item == MoreItem && _pages.TryGetValue(typeof(MorePage), out var more))
                ((MorePage)more).ShowSection(page);
        }

        // ---------------------------------------------------------------- play

        private async void TitlePlay_Click(object? sender, RoutedEventArgs e) => await PlayOrStopAsync();

        private async void TitleUpdate_Click(object? sender, RoutedEventArgs e) => await InstallUpdateAsync();

        /// <summary>Asks, downloads the newer launcher, swaps it in and restarts: the exe or package on
        /// Windows, the AppImage on Linux (else the release's page opens).</summary>
        public async Task InstallUpdateAsync()
        {
            var ws = Workspace.Current;
            if (ws.Update is not { } release) return;
            if (!OperatingSystem.IsWindows())
            {
                if (Updater.CanInstallAppImage(release))
                    await InstallAsync(release, package: false);
                else
                    LinuxReleaseNotice(release);
                return;
            }
            await InstallAsync(release, await Updater.NeedsPackageAsync(release));
        }

        /// <summary>An older package's engine: download the latest package and update it in place.</summary>
        public async Task UpdateEngineAsync()
        {
            var ws = Workspace.Current;
            var release = ws.Latest ?? await Updater.LatestAsync(Updater.BetaChannel(ws.Settings));
            if (release?.ZipUrl == null)
            {
                Prompt.Tell("Update engine", $"No package is published yet. See {Updater.ReleasesPage}");
                return;
            }
            if (!OperatingSystem.IsWindows())
            {
                LinuxReleaseNotice(release);
                return;
            }
            await InstallAsync(release, package: true);
        }

        /// <summary>A release the player picked in Settings > Updates > Version, older ones included.</summary>
        public async Task InstallVersionAsync(Updater.Release release)
        {
            if (release.IsCurrent)
            {
                Prompt.Tell("Already installed", $"You already have {Updater.CurrentLabel}.");
                return;
            }
            if (!OperatingSystem.IsWindows())
            {
                if (Updater.CanInstallAppImage(release))
                    await InstallAsync(release, package: false, chosen: true);
                else
                    LinuxReleaseNotice(release);
                return;
            }
            await InstallAsync(release, await Updater.SwitchNeedsPackageAsync(release), chosen: true);
        }

        /// <summary>A release this launcher cannot install itself: why, and its page.</summary>
        private static void LinuxReleaseNotice(Updater.Release release)
        {
            var why = Updater.SelfAppImage == null
                ? "This launcher is built from the repository, not the AppImage: update with git pull (and the engine submodule) and build again."
                : "This release has no AppImage attached: download it by hand from its page.";
            if (Prompt.Choose($"{release.Name} is out", why + " The release notes are on its page.",
                    "Close", "Open the release page") == 1)
                Ui.Open(release.Page);
        }

        private async Task InstallAsync(Updater.Release release, bool package, bool chosen = false)
        {
            var ws = Workspace.Current;
            if (package && ws.Game.Running)
            {
                Prompt.Tell("Close the game first", "This update replaces the engine, so the game has to be closed.");
                return;
            }
            var notes = release.Notes.Trim();
            if (notes.Length > 1200) notes = notes[..1200] + "…";
            var size = package ? release.ZipSize : OperatingSystem.IsWindows() ? release.ExeSize : release.AppImageSize;
            // The game runs from the AppImage's files, which go away when this launcher closes.
            if (!OperatingSystem.IsWindows() && ws.Game.Running)
            {
                Prompt.Tell("Close the game first", "The game runs from this AppImage, which the update replaces.");
                return;
            }
            var older = !release.IsNewerThan(Updater.Current, Updater.CurrentBeta);
            var body = $"You have {Updater.CurrentLabel}. " +
                       (package
                           ? $"{(chosen ? "This version has a different engine" : "This update includes a new engine")}, so the whole package is downloaded ({size / 1048576} MB) and replaced in place."
                           : $"The launcher downloads {release.Tag} ({size / 1048576} MB), replaces itself and restarts.") +
                       " Your settings, saves, mods and profiles stay." +
                       (chosen && older
                           ? "\n\nThis is an older version: settings it doesn't know are kept but not used, and the launcher may offer the newest version again at start."
                           : "") +
                       (notes.Length > 0 ? $"\n\n{(chosen && older ? "This version's notes" : "What's new")}:\n" + notes : "");
            var verb = chosen ? "Switch" : "Update";
            if (!Prompt.Confirm($"{verb} to {release.Name}?", body, $"{verb} now"))
                return;

            TitleUpdate.IsVisible = true;
            TitleUpdate.IsEnabled = false;
            var progress = new Progress<double>(p => TitleUpdateText.Text = $"Downloading {p:P0}");
            try
            {
                ws.SaveNow();
                var skipped = await Updater.InstallAsync(release, package, progress);
                if (skipped.Count > 0)
                    Prompt.Tell("Updated, with files left out",
                        "These could not be replaced, so the old ones were kept:\n" + string.Join("\n", skipped.Take(5)) +
                        $"\n\nUpdate again, or take them from {Updater.ReleasesPage}");
                Close();
            }
            catch (Exception ex)
            {
                TitleUpdate.IsEnabled = true;
                TitleUpdateText.Text = $"Update {release.Tag}";
                ws.Log("Update failed: " + ex.GetBaseException().Message, LogLevel.Bad);
                Prompt.Tell("Update failed", ex.GetBaseException().Message + $"\n\nYou can download it by hand from {Updater.ReleasesPage}");
            }
        }

        /// <summary>The one place the game is started from, so the tool bar and the Play page agree.</summary>
        public async Task PlayOrStopAsync()
        {
            var ws = Workspace.Current;
            if (ws.Game.Running)
            {
                await ws.StopGameAsync();
                return;
            }

            var s = ws.Settings;
            var ini = ws.Ini;
            // A connected Hunter's Dream account plays in its own co-op pool (the engine's thd backend);
            // decided here, not only when the Online page is opened.
            ini.Set("online_pool", s.HuntersDreamToken.Length > 0 && s.Str("hd_name").Length > 0 ? "huntersdream" : "");
            if (!HuntersDream.OnlinePaused && ini.Get("online_enabled") == "1" && ini.Get("online_pool") != "huntersdream")
            {
                // The engine refuses these at start; say so here instead.
                var problem = ini.Get("online_username").Contains('@') ? "The account name is the name registered on the server, not an email address."
                    : ini.Get("online_username").Trim().Length == 0 ? "Enter the account name before playing online."
                    : s.OnlinePassword.Length == 0 ? "Enter the account password before playing online."
                    : null;
                if (problem != null)
                {
                    Prompt.Tell("Online", problem + "\n\nOr turn \"Play online\" off to play offline.");
                    Show("online");
                    return;
                }
            }

            // No engine folder yet: look for it again, then let the player point at it right here.
            if (!GameRunner.Custom(s) && !Paths.IsPortDir(Paths.PortDir))
            {
                ws.ResolvePort();
                if (!Paths.IsPortDir(Paths.PortDir))
                {
                    if (!Prompt.Confirm("Where is the engine?",
                            "The launcher needs the Bloodborne PC engine: the folder with run.sh, scripts/run_game.py and a built " +
                            "out/bb-probe (bash build.sh builds it).", "Choose folder"))
                        return;
                    var folder = Ui.PickFolder("Engine folder (with run.sh)", "");
                    if (folder == null) return;
                    if (!Paths.IsPortDir(folder))
                    {
                        Prompt.Tell("Not the engine folder", "That folder has no run.sh with scripts/run_game.py and a built out/bb-probe.");
                        return;
                    }
                    s.Set("port_dir", folder);
                    ws.ResolvePort();
                    ws.SaveNow();
                }
            }

            // A patch that changes the game's region (Unlock Game Region) leaves the game without a
            // Bloodborne server: online, ask whether to leave it out for this session.
            GameEnvironment.RegionPatchOnline = null;
            if (!HuntersDream.OnlinePaused && ini.Get("online_enabled") == "1" && ini.Get("online_connected", "1") == "1" &&
                Patches.RegionChanging(GameEnvironment.PatchesDir(s)) is { Count: > 0 } region)
            {
                var names = string.Join(", ", region.Select(p => $"\"{p.Name}\""));
                var choice = Prompt.Choose("Play online with " + names + "?",
                    names + " moves the game to a region that has no Bloodborne server, so Play online fails with " +
                    "\"Failure to acquire information from the Bloodborne server\".\n\n" +
                    "Leave it out for this session? It stays on for offline play.",
                    "Cancel", "Keep it on", "Leave it out");
                if (choice <= 0) return;
                GameEnvironment.RegionPatchOnline = choice == 1 ? "keep" : "skip";
                ws.Log(choice == 1 ? $"{names}: kept on while online; Play online will not reach the server."
                    : $"{names}: left out while online; it applies again with online off.", LogLevel.Step);
            }

            // A third-party resolution patch while the output resolution sets the game's resolution
            // and UI itself: it would keep its own shape (black bars) and stop the widescreen hooks.
            GameEnvironment.ResolutionPatchChoice = null;
            var (outW, outH) = Choices.OutputSize(ini.Get("output_res", "1920x1080"));
            // With a 1920 x 1080 output, such a patch makes the game draw at its size itself: the
            // upscaler then runs at 1:1 (DLAA whatever the preset) and in-game preset changes crashed
            // (Discord, 3440 x 1440). Its size as the output resolution keeps the shape and lets the
            // presets work: offered once per set of patches.
            if ((outW, outH) == (1920, 1080) &&
                Patches.ResolutionSetting(GameEnvironment.PatchesDir(s)) is { Count: > 0 } native &&
                native.Select(p => System.Text.RegularExpressions.Regex.Match(p.Name, @"(\d{3,5})x(\d{3,5})"))
                    .FirstOrDefault(m => m.Success) is { } size &&
                (int.Parse(size.Groups[1].Value), int.Parse(size.Groups[2].Value)) is var (patchW, patchH) &&
                (patchW, patchH) != (1920, 1080))
            {
                var names = string.Join(", ", native.Select(p => $"\"{p.Name}\""));
                if (s.Str("resolution_patch_native") != names)
                {
                    var choice = Prompt.Choose($"Use {patchW} × {patchH} as the output resolution?",
                        names + $" makes the game draw at {patchW} × {patchH} by itself, so the upscaler preset can't lower " +
                        "the render resolution (the upscaler runs as DLAA / native AA).\n\n" +
                        $"Setting the output resolution to {patchW} × {patchH} instead gives the same picture and shape, " +
                        "and the presets (Quality, Balanced, Performance) work. The patch is then left out while that output is set.",
                        "Cancel", $"Use {patchW} × {patchH}", "Keep the patch");
                    if (choice <= 0) return;
                    if (choice == 1)
                    {
                        ini.Set("output_res", $"{patchW}x{patchH}");
                        ini.Save();
                        (outW, outH) = (patchW, patchH);
                        GameEnvironment.ResolutionPatchChoice = "skip";
                        ws.Log($"Output resolution {patchW} × {patchH}; {names} left out (presets work).", LogLevel.Step);
                    }
                    else
                    {
                        s.Set("resolution_patch_native", names);
                        ws.SaveNow();
                    }
                }
            }
            if ((outW, outH) != (1920, 1080) && GameEnvironment.ResolutionPatchChoice is null &&
                Patches.ResolutionSetting(GameEnvironment.PatchesDir(s)) is { Count: > 0 } sizing)
            {
                var names = string.Join(", ", sizing.Select(p => $"\"{p.Name}\""));
                // "Always on" is remembered for this set of patches (#18); another set asks again.
                var choice = s.Str("resolution_patch_always") == names ? 1 : Prompt.Choose("Play with " + names + "?",
                    $"The output resolution ({outW} × {outH}) already sets the game's resolution and widens the picture and UI to fill the screen. " +
                    names + " sets its own on top: the picture keeps the patch's shape, with black bars.\n\n" +
                    "Leave it out? It still applies with a 1920 × 1080 output. \"Always on\" keeps it and does not ask again " +
                    "until the resolution patches switched on in Mods change.",
                    "Cancel", "Keep on", "Leave out", "Always on");
                if (choice <= 0) return;
                if (choice == 3)
                {
                    s.Set("resolution_patch_always", names);
                    ws.SaveNow();
                    choice = 1;
                }
                GameEnvironment.ResolutionPatchChoice = choice == 1 ? "keep" : "skip";
                ws.Log(choice == 1 ? $"{names}: kept on with the {outW} × {outH} output."
                    : $"{names}: left out; the output resolution sets the picture.", LogLevel.Step);
            }

            // A game left running in the background (an earlier start that never ended, or another
            // launcher's) keeps the game's memory and its controllers.
            var leftovers = System.Diagnostics.Process.GetProcessesByName("bb-probe");
            if (leftovers.Length > 0)
            {
                var choice = Prompt.Choose("The game is still running in the background",
                    $"{Paths.EngineExe} from an earlier start is still running and holds the game's memory, so a new start would fail. " +
                    "Close it and start?", "Cancel", "Close it and start");
                if (choice <= 0) return;
                foreach (var leftover in leftovers)
                {
                    try
                    {
                        leftover.Kill(entireProcessTree: true);
                        leftover.WaitForExit(5000);
                    }
                    catch { /* already gone */ }
                    leftover.Dispose();
                }
                ws.Log("Closed the game left running in the background.", LogLevel.Step);
            }

            if (!s.Bool("memory_check_off") && MemoryCheck.Problem(ws.Ini) is { } memory)
            {
                var choice = Prompt.Choose("The PC may run out of memory", memory, "Cancel", "Don't ask again", "Start anyway");
                if (choice <= 0) return;
                if (choice == 1) s.Set("memory_check_off", true);
            }

            var error = ws.StartGame();
            if (error != null)
            {
                Prompt.Tell("Could not start the game", error);
                return;
            }
            if (s.Bool("switch_to_output"))
                Show("output");
        }

        private void UpdatePlayButton()
        {
            var running = Workspace.Current.Game.Running;
            TitlePlayText.Text = running ? "Stop" : "Play";
            TitlePlayIcon.Symbol = running ? Symbol.Stop : Symbol.Play;
            TitlePlayIcon.Foreground = Ui.Brush(running ? "DangerTextBrush" : "AccentTextFillColorPrimaryBrush");
            TitlePlay.SetTip(running ? "Stop the game" : "Start the game");
            foreach (var page in _pages.Values)
                (page as PlayPage)?.UpdateRunning();
        }

        // ---------------------------------------------------------------- log

        private void OnLogged(string message, LogLevel level)
        {
            _log.Add(new LogItem { Time = DateTime.Now.ToString("HH:mm:ss"), Message = message, Color = BrushFor(level) });
            LogCount.Text = $"{_log.Count} events";
            LogScroller.ScrollToEnd();

            StatusText.Text = message;
            StatusText.SetTip(message);
            StatusDot.Fill = level == LogLevel.Info ? Ui.Brush("StatusDotIdleBrush") : BrushFor(level);
        }

        private static IBrush BrushFor(LogLevel level) => Ui.Brush(level switch
        {
            LogLevel.Good => "GoodTextBrush",
            LogLevel.Bad => "DangerTextBrush",
            LogLevel.Warn => "WarningTextBrush",
            LogLevel.Step => "AccentTextFillColorPrimaryBrush",
            _ => "TextFillColorSecondaryBrush",
        });

        private void ClearLog_Click(object? sender, RoutedEventArgs e)
        {
            _log.Clear();
            LogCount.Text = "";
            StatusText.Text = "Ready";
            StatusText.SetTip(null);
            StatusDot.Fill = Ui.Brush("StatusDotIdleBrush");
        }

        private void ToggleLog_Click(object? sender, RoutedEventArgs e) => ToggleLog();
        private void StatusBar_Click(object? sender, PointerReleasedEventArgs e) => ToggleLog();

        private void ToggleLog()
        {
            _logExpanded = !_logExpanded;
            LogPanel.IsVisible = _logExpanded;
            if (_logExpanded)
                LogScroller.ScrollToEnd();
        }

        // ---------------------------------------------------------------- screenshots

        /// <summary>--screenshot &lt;folder&gt;: a picture of every page, then the launcher closes.</summary>
        private static string? ScreenshotDir;

        private async Task SaveScreenshotsAsync()
        {
            System.IO.Directory.CreateDirectory(ScreenshotDir!);
            var items = SettingRow.Classic
                ? Sidebar.GetLogicalDescendants().OfType<RadioButton>().ToList()
                : TabStrip.GetLogicalDescendants().OfType<RadioButton>().ToList();
            foreach (var item in items)
            {
                item.IsChecked = true;
                await Task.Delay(400);
                Save(item.Tag is Type type ? type.Name : "page");
                if (PageHost.Content is not MorePage more) continue;
                foreach (var key in more.SectionKeys.ToList())
                {
                    more.ShowSection(key);
                    await Task.Delay(400);
                    Save("More-" + key);
                }
            }
            Close();
        }

        /// <summary>The window as drawn now, into the screenshot folder.</summary>
        public void Save(string name)
        {
            if (ScreenshotDir == null) return;
            var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var bitmap = new RenderTargetBitmap(size);
            bitmap.Render(this);
            bitmap.Save(System.IO.Path.Combine(ScreenshotDir, name + ".png"));
        }
    }

    public class LogItem
    {
        public string Time { get; set; } = "";
        public string Message { get; set; } = "";
        public IBrush Color { get; set; } = Brushes.White;
    }
}
