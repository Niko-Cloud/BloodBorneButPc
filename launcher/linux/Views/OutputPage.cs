using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BloodborneLauncher.Core;
using FluentIcons.Common;

namespace BloodborneLauncher.Views
{
    /// <summary>The game's own output (run.sh, run_game.py and bb-probe), live while it runs.</summary>
    public sealed class OutputPage : LauncherPage
    {
        private readonly TextBox _output;
        private readonly ToggleSwitch _follow;
        private readonly StringBuilder _text = new();

        public OutputPage() : base("Game output", "What run.sh and the game print. Copy or export it when reporting a problem.")
        {
            var bar = new Grid { Margin = new Thickness(0, 18, 0, 8), ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            _follow = new ToggleSwitch { IsChecked = true, Content = "Follow new output", OnContent = null, OffContent = null, VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(_follow);

            var buttons = Row(
                SmallButton("Copy", Symbol.Copy, (_, _) => Copy()),
                SmallButton("Export…", Symbol.Save, (_, _) => Export()),
                SmallButton("Clear", Symbol.Broom, (_, _) => Ws.ClearOutput()));
            Grid.SetColumn(buttons, 1);
            bar.Children.Add(buttons);
            Body.Children.Add(bar);

            SettingToggle(Symbol.WindowConsole, "Show this page when the game starts", null, "switch_to_output");

            _output = new TextBox { Height = 520, Margin = new Thickness(0, 4, 0, 0) };
            _output.Classes.Add("CodeBox");
            Body.Children.Add(_output);

            // One update per batch; while the page is not shown nothing is laid out, and it catches
            // up in one go when shown.
            Ws.GameOutput += batch =>
            {
                if (VisualRoot == null)
                {
                    _stale = true;
                    return;
                }
                _lines += batch.Count;
                if (_lines > Workspace.MaxOutputLines + 500)
                {
                    Rebuild();
                    return;
                }
                foreach (var line in batch) _text.Append(line).Append('\n');
                Show();
            };
            Ws.OutputCleared += () =>
            {
                _text.Clear();
                _output.Text = "";
                _lines = 0;
            };
            AttachedToVisualTree += (_, _) => { if (_stale) Rebuild(); };
            Rebuild();
        }

        private int _lines;
        private bool _stale;

        private void Rebuild()
        {
            _text.Clear();
            foreach (var line in Ws.OutputLines) _text.Append(line).Append('\n');
            _lines = Ws.OutputLines.Count;
            _stale = false;
            Show();
        }

        /// <summary>The text into the box, then (when following) the caret at its end, which scrolls there.</summary>
        private void Show()
        {
            _output.Text = _text.ToString();
            if (_follow.IsChecked == true)
                _output.CaretIndex = _output.Text.Length;
        }

        public override void OnShown()
        {
            if (_stale) Rebuild();
            else if (_follow.IsChecked == true)
                _output.CaretIndex = _output.Text?.Length ?? 0;
        }

        private void Copy()
        {
            if (string.IsNullOrEmpty(_output.Text)) return;
            Ui.CopyText(_output.Text);
            Ws.Log("Game output copied.", LogLevel.Good);
        }

        private void Export()
        {
            var file = Ui.SaveFile("Export the game output", $"bbport-{DateTime.Now:yyyyMMdd_HHmmss}.log",
                "Log files|*.log|Text files|*.txt|All files|*");
            if (file == null) return;
            try
            {
                File.WriteAllText(file, _output.Text ?? "");
                Ws.Log("Game output saved: " + file, LogLevel.Good);
            }
            catch (Exception e)
            {
                Ws.Log("Could not save the output: " + e.Message, LogLevel.Bad);
            }
        }
    }
}
