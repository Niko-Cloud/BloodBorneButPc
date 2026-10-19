using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BloodborneLauncher.Core;
using FluentIcons.Common;

namespace BloodborneLauncher.Views
{
    /// <summary>
    /// A game folder from the player's own packages (Dump/GameDump.cs): the base game PKG, the 1.09
    /// update PKG and a folder to put it in. Once made, it becomes the game folder.
    /// </summary>
    public sealed class DumpPage : LauncherPage
    {
        private readonly TextBlock _gameHint, _updateHint, _targetHint, _status;
        private readonly UiButton _gameBrowse, _updateBrowse, _targetBrowse, _start, _cancel;
        private readonly InfoBar _problem;
        private readonly ProgressBar _bar;
        private PkgInfo? _game, _update;
        private string? _result; // the last dump's outcome, until a choice changes
        private CancellationTokenSource? _running;

        public DumpPage() : base("Dump game", "")
        {
            var intro = new InfoBar
            {
                IsOpen = true,
                Title = "Your own copy is needed",
                Message = "This makes the game folder from your own dump of Bloodborne: the base game package and the 1.09 " +
                          "update package, both decrypted (fake-signed) .pkg files. No game files come with the launcher, " +
                          "and a console's encrypted store package cannot be read.",
                Margin = new Thickness(0, 14, 0, 0),
            };
            Body.Children.Add(intro);

            // ==================== Packages ====================
            Group("Your packages");
            _gameBrowse = SmallButton("Browse", Symbol.FolderOpen, (_, _) => ChoosePackage(update: false));
            Add(Card(Symbol.Box, "Game package", "The base game: a Bloodborne .pkg of any region (the Game of the Year edition is CUSA03173)",
                _gameBrowse, out _gameHint));
            _updateBrowse = SmallButton("Browse", Symbol.FolderOpen, (_, _) => ChoosePackage(update: true));
            Add(Card(Symbol.ArrowSync, "Update package", "Update 1.09 for the same region as the game",
                _updateBrowse, out _updateHint));

            // ==================== Destination ====================
            Group("Where the dump goes");
            _targetBrowse = SmallButton("Browse", Symbol.FolderOpen, (_, _) => ChooseTarget());
            Add(Card(Symbol.Folder, "Save to",
                "The game folder is made inside this folder, named after the game (for example CUSA03173), and becomes the launcher's game folder",
                _targetBrowse, out _targetHint));

            // ==================== Make ====================
            _problem = new InfoBar { IsClosable = false, IsOpen = false, Margin = new Thickness(0, 14, 0, 0) };
            Body.Children.Add(_problem);

            var pane = new Border
            {
                Margin = new Thickness(0, 14, 0, 0),
                Padding = new Thickness(18, 16, 18, 16),
                CornerRadius = new CornerRadius(SettingRow.Classic ? 8 : 16),
                BorderThickness = new Thickness(1),
            };
            pane.SetResourceReference(Border.BackgroundProperty, "GlassPaneBrush");
            pane.SetResourceReference(Border.BorderBrushProperty, "GlassPaneStrokeBrush");
            var stack = new StackPanel { Spacing = 12 };

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            _start = new UiButton
            {
                Appearance = ControlAppearance.Primary,
                Label = "Make the dump",
                Glyph = Symbol.ArrowDownload,
                Padding = new Thickness(22, 10, 22, 10),
                LabelWeight = FontWeight.SemiBold,
            };
            _start.Click += async (_, _) => await StartAsync();
            actions.Children.Add(_start);
            _cancel = SmallButton("Cancel", Symbol.Dismiss, (_, _) => _running?.Cancel());
            _cancel.IsVisible = false;
            _cancel.VerticalAlignment = VerticalAlignment.Center;
            actions.Children.Add(_cancel);
            stack.Children.Add(actions);

            _bar = new ProgressBar { Height = 5, MinHeight = 5, Minimum = 0, Maximum = 1, CornerRadius = new CornerRadius(2.5), IsVisible = false };
            _bar.SetResourceReference(ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            _bar.SetResourceReference(BackgroundProperty, "PillTrackBrush");
            stack.Children.Add(_bar);

            _status = Text("", "Muted");
            _status.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(_status);
            pane.Child = stack;
            Body.Children.Add(pane);

            _game = Remembered("dump_game_pkg");
            _update = Remembered("dump_update_pkg");
            Refresh();
        }

        public override void OnShown()
        {
            if (_running == null) Refresh();
        }

        /// <summary>A package chosen before, if it is still there and still reads as a PKG.</summary>
        private static PkgInfo? Remembered(string key)
        {
            var path = S.Str(key);
            if (path.Length == 0 || !File.Exists(path)) return null;
            try { return GameDump.Inspect(path); }
            catch (Exception) { return null; }
        }

        private static string Describe(PkgInfo? pkg) => pkg == null
            ? "Not chosen"
            : $"{pkg.Path}  ·  {pkg.TitleId} {pkg.Version}, {pkg.Kind}, {GameDump.Gb(pkg.Size)}";

        private void Refresh()
        {
            SetHint(_gameHint, Describe(_game));
            SetHint(_updateHint, Describe(_update));

            var folder = S.Str("dump_target");
            string? problem = GameDump.Problem(_game, _update);
            if (folder.Length == 0)
            {
                SetHint(_targetHint, "Not chosen");
                problem ??= "Choose where the dump goes";
            }
            else
            {
                var target = _game != null ? GameDump.Target(folder, _game) : Path.Combine(folder, "<game id>");
                var free = FreeSpace(folder);
                SetHint(_targetHint, free >= 0 ? $"{target}  ·  {GameDump.Gb(free)} free" : target);
                if (!Directory.Exists(folder))
                    problem ??= "The chosen folder no longer exists";
                else if (_game != null && Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                    problem ??= $"{target} already exists: choose another folder, or remove it first";
                else if (_game != null && free >= 0 && free < _game.Size + (_update?.Size ?? 0))
                    problem ??= $"Not enough free space there: about {GameDump.Gb(_game.Size + (_update?.Size ?? 0))} is needed";
            }

            _problem.IsOpen = problem != null && (_game != null || _update != null || folder.Length > 0);
            _problem.Severity = InfoBarSeverity.Warning;
            _problem.Title = "Not ready yet";
            _problem.Message = problem;
            _start.IsEnabled = problem == null;
            _status.Text = _result ?? (problem == null ? "Ready: the dump takes a few minutes and about as much space as the game package" : "");
            _status.IsVisible = _status.Text.Length > 0;
        }

        private static long FreeSpace(string folder)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace; }
            catch (Exception) { return -1; }
        }

        private void ChoosePackage(bool update)
        {
            var key = update ? "dump_update_pkg" : "dump_game_pkg";
            var start = S.Str(key) is { Length: > 0 } last ? Path.GetDirectoryName(last) ?? "" : "";
            var path = Ui.OpenFile(update ? "The 1.09 update package" : "The game package", "PS4 package|*.pkg|All files|*", start);
            if (path == null) return;
            PkgInfo info;
            try
            {
                info = GameDump.Inspect(path);
            }
            catch (Exception e)
            {
                Prompt.Tell("Not a package", $"{Path.GetFileName(path)} could not be read as a PS4 package ({e.Message}).");
                return;
            }
            if (update) _update = info; else _game = info;
            S.Set(key, path);
            Ws.SaveSoon();
            _result = null;
            Refresh();
        }

        private void ChooseTarget()
        {
            var path = PickFolder("Where the dump goes (a game folder is made inside it)", S.Str("dump_target"));
            if (path == null) return;
            S.Set("dump_target", path);
            Ws.SaveSoon();
            _result = null;
            Refresh();
        }

        private void SetBusy(bool busy)
        {
            _gameBrowse.IsEnabled = _updateBrowse.IsEnabled = _targetBrowse.IsEnabled = !busy;
            _start.IsVisible = !busy;
            _cancel.IsVisible = busy;
            _bar.IsVisible = busy;
            _problem.IsOpen = false;
        }

        private async Task StartAsync()
        {
            if (_game is not { } game || _update is not { } update || GameDump.Problem(game, update) != null) return;
            if (Ws.Game.Running)
            {
                Prompt.Tell("Dump game", "Stop the game first.");
                return;
            }
            var target = GameDump.Target(S.Str("dump_target"), game);

            _running = new CancellationTokenSource();
            SetBusy(true);
            _bar.Value = 0;
            _status.Text = "Opening the packages";
            _status.IsVisible = true;
            Ws.Log($"Dumping {game.TitleId} into {target}", LogLevel.Step);
            var progress = new Progress<DumpProgress>(p =>
            {
                if (_running == null) return;
                _bar.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;
                _status.Text = p.Step.StartsWith("Checking")
                    ? $"{p.Step}: {p.Done} of {p.Total}"
                    : $"{p.Step}: {GameDump.Gb(p.Done)} of {GameDump.Gb(p.Total)}";
            });

            string? error = null;
            var cancelled = false;
            try
            {
                var token = _running.Token;
                await Task.Run(() => GameDump.Make(game, update, target, progress, token), token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            finally
            {
                _running.Dispose();
                _running = null;
                SetBusy(false);
            }

            if (cancelled)
            {
                _result = "Cancelled: nothing was kept";
                Ws.Log("Dump cancelled", LogLevel.Warn);
            }
            else if (error != null)
            {
                _result = "The dump failed: " + error;
                Ws.Log("Dump failed: " + error, LogLevel.Bad);
                Prompt.Tell("The dump failed", error);
            }
            else
            {
                Done(target);
                return;
            }
            Refresh();
        }

        /// <summary>The new dump becomes the game folder, as Play's Browse would make it.</summary>
        private void Done(string target)
        {
            S.Set("game_dir", target);
            if (!Paths.IsPortDir(Paths.PortDir))
                Ws.ResolvePort(); // the engine may sit beside the new game folder
            Ws.SaveSoon();
            var status = GameCheck.Inspect(target);
            Ws.Log("Game folder: " + target, LogLevel.Good);
            _result = $"Done: {target} is now the game folder ({status.Summary})";
            Refresh();
            if (Prompt.Choose("The dump is ready",
                    $"{target} is now the game folder. Every game archive in it was checked and is whole.\n\n{status.Summary}.",
                    "Close", "Go to Play") == 1)
                MainWindow.Instance?.Show("play");
        }
    }
}
