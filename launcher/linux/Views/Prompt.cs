using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BloodborneLauncher.Views
{
    /// <summary>
    /// A small modal dialog in the launcher's style: a title, text, optional fields, and buttons.
    /// It is drawn over the launcher itself (a dimmed layer with the card in the middle), which
    /// also works on Wayland, where a program cannot place a window over another. The calls wait
    /// for the answer, as WPF's ShowDialog did, so callers read it on the next line.
    /// </summary>
    public sealed class Prompt : Border
    {
        public sealed record Field(string Label, string Value = "", string Placeholder = "", bool Multiline = false, bool Password = false);

        private readonly List<TextBox> _boxes = new();
        private readonly DispatcherFrame _frame = new();
        private readonly Border _card;
        private Window? _window;

        /// <summary>True for OK (or a choice); false for Cancel, Escape or closing.</summary>
        private bool _result;

        /// <summary>Choose: the index of the button pressed; -1 when closed with Escape.</summary>
        private int _chosen = -1;

        private Prompt(string title, string? message, IReadOnlyList<Field> fields, string ok, bool showCancel, string[]? choices = null)
        {
            var stack = new StackPanel();
            stack.Children.Add(LauncherPage.Text(title, "PageTitle"));
            if (!string.IsNullOrEmpty(message))
            {
                var text = LauncherPage.Text(message, "Muted");
                text.FontSize = 13;
                text.Margin = new Thickness(0, 8, 0, 4);
                // A long message (an import's list of changes) scrolls instead of pushing the buttons
                // out of a small window.
                stack.Children.Add(new ScrollViewer
                {
                    Content = text,
                    MaxHeight = 300,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                });
            }
            foreach (var field in fields)
            {
                var label = LauncherPage.Text(field.Label, "StatLabel");
                label.Margin = new Thickness(0, 12, 0, 4);
                stack.Children.Add(label);
                var box = new TextBox
                {
                    Text = field.Value,
                    Watermark = field.Placeholder,
                    AcceptsReturn = field.Multiline,
                    TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    MinHeight = field.Multiline ? 70 : 0,
                    // A pasted code is one long line: it wraps and scrolls inside the box, which keeps
                    // the dialog's buttons on screen in a small window.
                    MaxHeight = field.Multiline ? 160 : double.PositiveInfinity,
                    PasswordChar = field.Password ? '•' : default,
                };
                _boxes.Add(box);
                stack.Children.Add(box);
            }

            // Equal-width buttons along the bottom, the action on the right.
            var buttons = new UniformGrid { Rows = 1, Margin = new Thickness(0, 22, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            UiButton? main = null;
            if (choices != null)
            {
                // Several actions: equal widths, the last one the main action.
                buttons.HorizontalAlignment = HorizontalAlignment.Stretch;
                for (var i = 0; i < choices.Length; i++)
                {
                    var index = i;
                    var last = i == choices.Length - 1;
                    var button = new UiButton
                    {
                        Label = choices[i],
                        Margin = new Thickness(0, 0, last ? 0 : 8, 0),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        Appearance = last ? ControlAppearance.Primary : ControlAppearance.Secondary,
                    };
                    button.Click += (_, _) => { _chosen = index; Finish(true); };
                    buttons.Children.Add(button);
                    if (last) main = button;
                }
            }
            else if (showCancel)
            {
                var cancel = new UiButton
                {
                    Label = "Cancel", Margin = new Thickness(0, 0, 8, 0), MinWidth = 110,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                cancel.Click += (_, _) => Finish(false);
                buttons.Children.Add(cancel);
            }
            if (choices == null)
            {
                main = new UiButton
                {
                    Label = ok, Appearance = ControlAppearance.Primary, MinWidth = 110,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                main.Click += (_, _) => Finish(true);
                buttons.Children.Add(main);
            }
            stack.Children.Add(buttons);

            _card = new Border
            {
                Width = 440,
                Padding = new Thickness(24, 20, 24, 20),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = stack,
                BoxShadow = BoxShadows.Parse("0 8 40 0 #8C000000"),
                RenderTransform = new ScaleTransform(0.96, 0.96),
                Transitions = new Transitions
                {
                    new TransformOperationsTransition
                    {
                        Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new CubicEaseOut(),
                    },
                },
            };
            _card.SetResourceReference(Border.BackgroundProperty, "DialogBackgroundBrush");
            _card.SetResourceReference(Border.BorderBrushProperty, "DialogStrokeBrush");

            // The dimmed layer over the launcher, fading in.
            this.SetResourceReference(BackgroundProperty, "DialogScrimBrush");
            Child = _card;
            Opacity = 0;
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new CubicEaseOut() },
            };
            Focusable = true;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Finish(false);
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && main != null && !(e.Source is TextBox { AcceptsReturn: true }))
                {
                    // Enter is the main action, as WPF's IsDefault button.
                    if (choices != null) _chosen = choices.Length - 1;
                    Finish(true);
                    e.Handled = true;
                }
            };
        }

        private void Finish(bool result)
        {
            _result = result;
            _frame.Continue = false;
            _window?.Close();
        }

        /// <summary>Shows the dialog and waits for the answer.</summary>
        private bool Run()
        {
            AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                Opacity = 1;
                _card.RenderTransform = new ScaleTransform(1, 1);
                if (_boxes.Count > 0)
                {
                    _boxes[0].Focus();
                    _boxes[0].SelectAll();
                }
                else
                    Focus();
            }, DispatcherPriority.Background);

            if (MainWindow.Instance is { IsVisible: true, WindowState: not WindowState.Minimized } main)
            {
                main.DialogHost.Children.Add(this);
                Dispatcher.UIThread.PushFrame(_frame);
                main.DialogHost.Children.Remove(this);
            }
            else
            {
                // The launcher is hidden (Play straight from Steam): a small window of its own.
                Background = null;
                _card.Margin = new Thickness(16);
                _window = new Window
                {
                    Content = this,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    CanResize = false,
                    Title = "Bloodborne Launcher",
                    Background = Ui.Brush("WindowBaseBrush"),
                };
                _window.Closed += (_, _) => _frame.Continue = false;
                _window.Show();
                Dispatcher.UIThread.PushFrame(_frame);
            }
            return _result;
        }

        /// <summary>The fields' values, or null if cancelled.</summary>
        public static string[]? Ask(string title, string? message, params Field[] fields)
        {
            var dialog = new Prompt(title, message, fields, "OK", showCancel: true);
            return dialog.Run() ? dialog._boxes.Select(b => b.Text ?? "").ToArray() : null;
        }

        public static bool Confirm(string title, string message, string ok) =>
            new Prompt(title, message, Array.Empty<Field>(), ok, showCancel: true).Run();

        /// <summary>One of several actions (the last is the main one): its index, or -1 if dismissed.</summary>
        public static int Choose(string title, string message, params string[] choices)
        {
            var dialog = new Prompt(title, message, Array.Empty<Field>(), "", showCancel: false, choices);
            return dialog.Run() ? dialog._chosen : -1;
        }

        public static void Tell(string title, string message) =>
            new Prompt(title, message, Array.Empty<Field>(), "OK", showCancel: false).Run();
    }
}
