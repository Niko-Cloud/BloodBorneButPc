using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using BloodborneLauncher.Core;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace BloodborneLauncher.Views
{
    /// <summary>Trophies: completion, every trophy earned or locked, and the notification.</summary>
    public sealed class TrophiesPage : LauncherPage
    {
        private readonly TextBlock _percent, _count, _points, _baseText, _dlcText;
        private readonly ProgressBar _overall, _base, _dlc;
        private readonly UniformGrid _grades = new() { Rows = 1, Margin = new Thickness(0, 16, 0, 0) };
        private readonly StackPanel _list = new();
        private readonly InfoBar _support;
        private readonly TextBlock _logHint;
        private string _filter = "all";

        public TrophiesPage() : base("Trophies",
            "Bloodborne's 40 trophies, your completion rate, and the notification shown when one unlocks.")
        {
            _support = new InfoBar { IsClosable = false, Severity = InfoBarSeverity.Warning, Margin = new Thickness(0, 14, 0, 0) };
            Body.Children.Add(_support);

            // ==================== Completion ====================
            var card = new Border
            {
                Margin = new Thickness(0, 18, 0, 0),
                Padding = new Thickness(18, 16, 18, 16),
                CornerRadius = new CornerRadius(SettingRow.Classic ? 8 : 16),
                BorderThickness = new Thickness(1),
            };
            card.SetResourceReference(Border.BackgroundProperty, "GlassPaneBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "GlassPaneStrokeBrush");
            var stack = new StackPanel();

            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            _percent = new TextBlock { FontSize = 40, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            _percent.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            _percent.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            top.Children.Add(_percent);
            var summary = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            summary.Children.Add(Text("COMPLETION", "StatLabel"));
            _count = Text("", "RowTitle");
            _points = Text("", "Faint");
            summary.Children.Add(_count);
            summary.Children.Add(_points);
            Grid.SetColumn(summary, 1);
            top.Children.Add(summary);
            stack.Children.Add(top);

            _overall = Bar(10);
            _overall.Margin = new Thickness(0, 14, 0, 0);
            stack.Children.Add(_overall);

            var parts = new UniformGrid { Rows = 1, Margin = new Thickness(0, 14, 0, 0) };
            parts.Children.Add(Part("Bloodborne", out _baseText, out _base));
            parts.Children.Add(Part("The Old Hunters", out _dlcText, out _dlc));
            stack.Children.Add(parts);
            stack.Children.Add(_grades);
            card.Child = stack;
            Body.Children.Add(card);

            // ==================== The list ====================
            var header = new Grid { Margin = new Thickness(2, 24, 0, 8), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var title = Text("All trophies", "GroupHeader");
            title.Margin = new Thickness(0);
            title.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(title);
            var filter = new ComboBox { Width = 150 };
            foreach (var (label, value) in new[] { ("All", "all"), ("Earned", "earned"), ("Locked", "locked") })
                filter.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            filter.SelectedIndex = 0;
            filter.SelectionChanged += (_, _) =>
            {
                _filter = (string)((ComboBoxItem)filter.SelectedItem!).Tag!;
                Refresh();
            };
            Grid.SetColumn(filter, 1);
            header.Children.Add(filter);
            Body.Children.Add(header);
            Body.Children.Add(_list);

            // ==================== Notifications ====================
            Group("Notification");
            SettingToggle(Symbol.Alert, "Show a notification",
                OperatingSystem.IsWindows()
                    ? "A small banner in a corner of the game's screen, without taking focus"
                    : "A desktop notification when a trophy unlocks (shown by KDE, GNOME and the others over the game)",
                "trophy_notifications");
            // The desktop places its own notifications.
            if (OperatingSystem.IsWindows())
                ComboRow(Symbol.Pin, "Position", null, new[]
                {
                    new Choice("Top right", "top-right"), new Choice("Top left", "top-left"),
                    new Choice("Bottom right", "bottom-right"), new Choice("Bottom left", "bottom-left"),
                }, () => S.Str("trophy_position"), v => S.Set("trophy_position", v));
            SettingToggle(Symbol.Speaker2, "Play a sound", null, "trophy_sound");
            SliderRow(Symbol.Speaker1, "Volume", null, 0, 100, 5, () => S.Int("trophy_volume"),
                v => S.Set("trophy_volume", (int)v), "0");
            Add(Card(Symbol.MusicNote2, "Sound file", "A .wav of your own; empty uses the built-in chime", Row(
                SmallButton("Choose…", Symbol.FolderOpen, (_, _) => ChooseSound()),
                IconButton(Symbol.ArrowUndo, "Use the built-in chime", (_, _) =>
                {
                    S.Set("trophy_sound_file", "");
                    Ws.SaveSoon();
                    Ws.Log("Trophy sound: built-in chime.", LogLevel.Step);
                })), out _));
            Add(Card(Symbol.PlayCircle, "Try it", "Shows a sample notification and plays the sound",
                SmallButton("Test", Symbol.Trophy, (_, _) => TrophyToast.Announce(TrophyTracker.ById[14])), out _));
            Add(Card(Symbol.Folder, "Trophy log", "", IconButton(Symbol.Open, "Open the saves folder",
                (_, _) => Paths.OpenFolder(GameEnvironment.UserDir(S))), out _logHint));
            Add(Card(Symbol.ArrowReset, "Reset trophy progress", "Starts trophies and completion over, for a fresh run. The old log is kept as a backup",
                SmallButton("Reset…", Symbol.ArrowReset, (_, _) => ResetProgress()), out _));

            Ws.TrophiesChanged += Refresh;
        }

        private void ResetProgress()
        {
            if (Ws.Trophies.Count == 0)
            {
                Prompt.Tell("Reset trophy progress", "There is no trophy progress to reset.");
                return;
            }
            if (!Prompt.Confirm("Reset trophy progress",
                    $"All {Ws.Trophies.Count} unlocked trophies and the completion rate start over. Your saves are not touched, " +
                    "and the current trophy log is kept as a backup in the saves folder.", "Reset"))
                return;
            try
            {
                var backup = Ws.Trophies.Reset();
                Ws.Log(backup == null ? "Trophy progress reset." : "Trophy progress reset. Backup: " + backup, LogLevel.Good);
            }
            catch (Exception e)
            {
                Ws.Log("Could not reset trophy progress: " + e.Message, LogLevel.Bad);
            }
            Refresh();
        }

        private static ProgressBar Bar(double height)
        {
            var bar = new ProgressBar { Height = height, MinHeight = height, Minimum = 0, Maximum = 1, CornerRadius = new CornerRadius(height / 2) };
            bar.SetResourceReference(ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            bar.SetResourceReference(BackgroundProperty, "PillTrackBrush");
            return bar;
        }

        private static StackPanel Part(string name, out TextBlock text, out ProgressBar bar)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            var row = new Grid();
            row.Children.Add(Text(name, "Muted"));
            text = Text("", "Faint");
            text.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(text);
            panel.Children.Add(row);
            bar = Bar(5);
            bar.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(bar);
            return panel;
        }

        public override void OnShown()
        {
            var probe = Path.Combine(Paths.BinariesDir, Paths.EngineExe);
            var reports = File.Exists(probe) && TrophyTracker.RuntimeReportsTrophies(probe);
            var hasLog = File.Exists(Ws.Trophies.LogPath);
            _support.IsOpen = !reports && !hasLog;
            _support.Title = "This build of the game does not report trophies";
            _support.Message = $"Its {Paths.EngineExe} accepts unlocks silently, so nothing can be tracked. A build with trophy hooks " +
                               "writes trophies.log, and the launcher picks it up from there.";
            Refresh();
        }

        private void Refresh()
        {
            var t = Ws.Trophies;
            SetHint(_logHint, $"trophies.log in the saves folder of the {PlayerProfiles.Current(S)} profile: one line per unlock (id and time)");
            _percent.Text = $"{t.Completion * 100:0}%";
            _count.Text = $"{t.Count} of {TrophyTracker.All.Length} trophies";
            _points.Text = $"{t.EarnedPoints} of {t.TotalPoints} points (weighted by grade, as on PlayStation)";
            _overall.Value = t.Completion;

            var (b, bt) = t.Progress(dlc: false);
            var (d, dt) = t.Progress(dlc: true);
            _baseText.Text = $"{b} / {bt}";
            _base.Value = (double)b / bt;
            _dlcText.Text = $"{d} / {dt}";
            _dlc.Value = (double)d / dt;

            _grades.Children.Clear();
            foreach (var grade in new[] { Grade.Platinum, Grade.Gold, Grade.Silver, Grade.Bronze })
            {
                var all = TrophyTracker.All.Where(x => x.Grade == grade).ToList();
                var earned = all.Count(x => t.Unlocked.ContainsKey(x.Id));
                var icon = new SymbolIcon
                {
                    Symbol = Symbol.Trophy, IconVariant = IconVariant.Filled, FontSize = 18,
                    Foreground = new SolidColorBrush(TrophyToast.GradeColor(grade)), VerticalAlignment = VerticalAlignment.Center,
                };
                var value = Text($"{earned} / {all.Count}", "StatValue");
                value.Margin = new Thickness(8, 0, 0, 0);
                var label = Text(grade.ToString(), "StatLabel");
                var col = new StackPanel();
                col.Children.Add(label);
                col.Children.Add(Row(icon, value));
                _grades.Children.Add(col);
            }

            _list.Children.Clear();
            foreach (var trophy in TrophyTracker.All)
            {
                var earned = t.Unlocked.TryGetValue(trophy.Id, out var when);
                if (_filter == "earned" && !earned || _filter == "locked" && earned) continue;

                var hint = trophy.Description + (trophy.Dlc ? "  ·  The Old Hunters" : "");
                var status = earned
                    ? DateTimeOffset.FromUnixTimeSeconds(when).LocalDateTime.ToString("d MMM yyyy, HH:mm")
                    : "Locked";
                var right = Text(status, earned ? "Muted" : "Faint");
                right.VerticalAlignment = VerticalAlignment.Center;
                right.TextAlignment = TextAlignment.Right;
                right.MinWidth = 130;

                var row = Card(Symbol.Trophy, trophy.Name, hint, right, out _);
                var symbol = row.Icon;
                row.ShowIcon = true;
                SetHint(row.Hint, hint);
                symbol.IconVariant = earned ? IconVariant.Filled : IconVariant.Regular;
                if (earned)
                    symbol.Foreground = new SolidColorBrush(TrophyToast.GradeColor(trophy.Grade));
                else
                    symbol.SetResourceReference(SymbolIcon.ForegroundProperty, "TextFillColorTertiaryBrush");
                row.Opacity = earned ? 1 : 0.62;
                row.SetTip($"{trophy.Grade} · {trophy.Points} points");
                _list.Children.Add(row);
            }
            if (_list.Children.Count == 0)
                _list.Children.Add(Text(_filter == "earned" ? "No trophies earned yet." : "Every trophy is earned. Hats off!", "Faint"));
        }

        private void ChooseSound()
        {
            var file = Ui.OpenFile("Trophy sound", "WAV sound|*.wav");
            if (file == null) return;
            S.Set("trophy_sound_file", file);
            Ws.SaveSoon();
            Chime.Play(S.Int("trophy_volume"), file);
        }
    }
}
