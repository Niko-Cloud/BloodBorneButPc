using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BloodborneLauncher.Core;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace BloodborneLauncher.Views
{
    /// <summary>
    /// One setting: its title on the left (descriptions in an (i) tooltip), its control on the right,
    /// or below it for wide controls such as segmented buttons. Hint is a line under the title for
    /// live status (a path, "not available on this GPU"); it stays hidden until something is set.
    /// </summary>
    public sealed class SettingRow : Border
    {
        public SymbolIcon Icon { get; }
        public TextBlock Hint { get; }

        public bool ShowIcon
        {
            get => Icon.IsVisible;
            set => Icon.IsVisible = value;
        }

        /// <summary>Classic layout: one card per setting with its icon and description, as before 0.9.4.</summary>
        public static bool Classic { get; set; }

        public SettingRow(Symbol icon, string title, string? info, Control? control, bool wide)
        {
            Padding = Classic ? new Thickness(16, 12, 16, 12) : new Thickness(14, 9, 14, 9);
            MinHeight = Classic ? 64 : 42;
            BorderThickness = Classic ? new Thickness(1) : new Thickness(0, 1, 0, 0);
            this.SetResourceReference(BorderBrushProperty, Classic ? "CardStrokeColorDefaultBrush" : "GlassDividerBrush");
            if (Classic)
            {
                CornerRadius = new CornerRadius(6);
                Margin = new Thickness(0, 0, 0, 4);
                this.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
            }

            Icon = new SymbolIcon
            {
                Symbol = icon, FontSize = Classic ? 20 : 18, Margin = new Thickness(0, 0, Classic ? 16 : 12, 0),
                VerticalAlignment = VerticalAlignment.Center, IsVisible = Classic,
            };

            var name = new TextBlock { Text = title, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
            titleLine.Children.Add(name);
            if (!string.IsNullOrWhiteSpace(info) && !Classic)
            {
                var mark = new SymbolIcon
                {
                    Symbol = Symbol.Info, FontSize = 12, Margin = new Thickness(6, 1, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Help),
                    Background = Brushes.Transparent,
                };
                mark.SetResourceReference(SymbolIcon.ForegroundProperty, "TextFillColorTertiaryBrush");
                ToolTip.SetTip(mark, info);
                ToolTip.SetShowDelay(mark, 150);
                titleLine.Children.Add(mark);
            }

            Hint = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), IsVisible = false };
            Hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(titleLine);
            if (Classic && !string.IsNullOrWhiteSpace(info))
            {
                // Classic shows the description under the title instead of in a tooltip.
                var description = new TextBlock { Text = info, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                description.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                text.Children.Add(description);
            }

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            grid.Children.Add(Icon);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (control != null)
            {
                if (wide)
                {
                    // Title on its own line, the control across the row, the hint under it.
                    grid.RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
                    control.Margin = new Thickness(0, 8, 0, 0);
                    Grid.SetRow(control, 1);
                    Grid.SetColumn(control, 1);
                    Grid.SetColumnSpan(control, 2);
                    grid.Children.Add(control);
                    Grid.SetRow(Hint, 2);
                    Grid.SetColumn(Hint, 1);
                    Grid.SetColumnSpan(Hint, 2);
                    grid.Children.Add(Hint);
                }
                else
                {
                    text.Children.Add(Hint);
                    control.Margin = new Thickness(12, 0, 0, 0);
                    control.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(control, 2);
                    grid.Children.Add(control);
                }
            }
            else
                text.Children.Add(Hint);

            Child = grid;
        }
    }

    /// <summary>A row of options where one is chosen, like the PS4/macOS segmented control.</summary>
    public sealed class Segmented : Border
    {
        private readonly List<Border> _items = new();
        public int SelectedIndex { get; private set; } = -1;
        public event Action<int>? Changed;

        public Segmented(IReadOnlyList<Choice> choices, Func<Choice, bool>? enabled = null)
        {
            CornerRadius = new CornerRadius(14);
            Padding = new Thickness(3);
            this.SetResourceReference(BackgroundProperty, "PillTrackBrush");

            var grid = new Grid();
            for (var i = 0; i < choices.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                var index = i;
                var choice = choices[i];
                var label = new TextBlock
                {
                    Text = Short(choice.Label),
                    FontSize = 12.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                var item = new Border
                {
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(8, 5, 8, 5),
                    Child = label,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Background = Brushes.Transparent,
                };
                if (choice.Label != Short(choice.Label) || choice.Note != null)
                    item.SetTip(string.Join("\n", new[] { choice.Label, choice.Note }.Where(t => t != null)));
                var on = enabled?.Invoke(choice) ?? true;
                item.IsEnabled = on;
                if (!on) label.Opacity = 0.35;
                item.PointerReleased += (_, e) =>
                {
                    if (e.InitialPressMouseButton == MouseButton.Left) Select(index, notify: true);
                };
                item.PointerEntered += (_, _) => { if (index != SelectedIndex) item.SetResourceReference(BackgroundProperty, "SidebarItemHoverBrush"); };
                item.PointerExited += (_, _) => { if (index != SelectedIndex) item.Background = Brushes.Transparent; };
                Grid.SetColumn(item, i);
                grid.Children.Add(item);
                _items.Add(item);
            }
            Child = grid;
        }

        /// <summary>"TAA (native anti-aliasing)" becomes "TAA": the full label is in the tooltip.</summary>
        public static string Short(string label) => label.Split(" (")[0];

        public void Select(int index, bool notify)
        {
            if (index < 0 || index >= _items.Count || !_items[index].IsEnabled) return;
            SelectedIndex = index;
            for (var i = 0; i < _items.Count; i++)
            {
                var label = (TextBlock)_items[i].Child!;
                if (i == index)
                {
                    _items[i].SetResourceReference(BackgroundProperty, "SegmentSelectedBrush");
                    label.Foreground = Brushes.White;
                    label.FontWeight = FontWeight.SemiBold;
                }
                else
                {
                    _items[i].Background = Brushes.Transparent;
                    label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                    label.FontWeight = FontWeight.Normal;
                }
            }
            if (notify) Changed?.Invoke(index);
        }
    }

    /// <summary>
    /// A settings page: groups of compact rows, each group one rounded box (Sectionize puts the
    /// rows of a group into it once the page is built). The helpers bind a control straight to a
    /// settings.json or bbport.ini key and save on change.
    /// </summary>
    public abstract class LauncherPage : UserControl
    {
        protected static Workspace Ws => Workspace.Current;
        protected static LauncherSettings S => Ws.Settings;
        protected static IniFile Ini => Ws.Ini;

        protected StackPanel Body { get; }
        protected ScrollViewer Scroller { get; }
        private bool _sectioned;

        protected LauncherPage(string title, string subtitle)
        {
            Background = Brushes.Transparent;
            this.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

            // The tab says which page this is; no page title.
            Body = new StackPanel { Margin = new Thickness(20, 6, 20, 24), MaxWidth = 880 };
            Scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = Body,
            };
            Content = Scroller;
            PageScroll.Attach(Scroller);
            if (SettingRow.Classic) Body.MaxWidth = double.PositiveInfinity;
            Loaded += (_, _) =>
            {
                if (_sectioned || SettingRow.Classic) return;
                _sectioned = true;
                Sectionize();
            };
        }

        /// <summary>Called each time the page is shown; refresh anything read from disk.</summary>
        public virtual void OnShown() { }

        /// <summary>
        /// Puts the rows that follow each group header into one rounded box. Rows, panels of rows
        /// and short captions go in; anything else (an info bar, a table, a summary card) ends the box.
        /// </summary>
        private void Sectionize()
        {
            var children = Body.Children.ToList();
            Body.Children.Clear();
            StackPanel? box = null;
            foreach (var child in children)
            {
                if (child is TextBlock { Tag: "group" })
                {
                    Body.Children.Add(child);
                    box = new StackPanel { Margin = new Thickness(0, -1, 0, 0) };
                    // ClipToBounds follows the corner radius, so the first row's divider stays inside it.
                    var frame = new Border
                    {
                        CornerRadius = new CornerRadius(16),
                        BorderThickness = new Thickness(1),
                        ClipToBounds = true,
                        Child = box,
                    };
                    frame.SetResourceReference(Border.BackgroundProperty, "GlassPaneBrush");
                    frame.SetResourceReference(Border.BorderBrushProperty, "GlassPaneStrokeBrush");
                    Body.Children.Add(frame);
                    continue;
                }
                var fits = child is SettingRow || child is StackPanel || child is WrapPanel ||
                           (child is TextBlock && box != null);
                if (box != null && fits)
                {
                    if (child is TextBlock caption) caption.Margin = new Thickness(14, 10, 14, 6);
                    if (child is WrapPanel wrap) wrap.Margin = new Thickness(10, 4, 10, 10);
                    box.Children.Add(child);
                }
                else
                {
                    box = null;
                    Body.Children.Add(child);
                }
            }
        }

        // ---------------------------------------------------------------- building blocks

        public static TextBlock Text(string text, string style)
        {
            var block = new TextBlock { Text = text };
            block.Classes.Add(style);
            return block;
        }

        protected TextBlock Group(string text, Panel? into = null)
        {
            var header = Text(text, "GroupHeader");
            header.Tag = "group";
            (into ?? Body).Children.Add(header);
            return header;
        }

        public static SymbolIcon Icon(Symbol symbol) => new() { Symbol = symbol };

        /// <summary>A setting row. info goes in the (i) tooltip; hintBlock is a visible status line, empty until set.</summary>
        public static SettingRow Card(Symbol icon, string title, string? info, Control? control, out TextBlock hintBlock, bool wide = false)
        {
            var row = new SettingRow(icon, title, info, control, wide);
            hintBlock = row.Hint;
            return row;
        }

        public static void SetHint(TextBlock hint, string? text)
        {
            hint.Text = text ?? "";
            hint.IsVisible = !string.IsNullOrEmpty(text);
        }

        protected SettingRow Add(SettingRow row, Panel? into = null)
        {
            (into ?? Body).Children.Add(row);
            return row;
        }

        public static UiButton SmallButton(string text, Symbol icon, EventHandler<RoutedEventArgs> click, ControlAppearance look = ControlAppearance.Secondary)
        {
            var button = new UiButton
            {
                Label = text,
                Glyph = icon,
                Appearance = look,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(10, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Center,
            };
            button.Click += click;
            return button;
        }

        public static UiButton IconButton(Symbol icon, string tooltip, EventHandler<RoutedEventArgs> click)
        {
            var button = new UiButton
            {
                Glyph = icon,
                Appearance = ControlAppearance.Transparent,
                Padding = new Thickness(6),
                Margin = new Thickness(2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            button.SetTip(tooltip);
            button.Click += click;
            return button;
        }

        public static StackPanel Row(params Control[] children)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var child in children)
                panel.Children.Add(child);
            return panel;
        }

        // ---------------------------------------------------------------- bound rows

        /// <summary>A choice: segmented buttons for a few short options, a drop-down otherwise.
        /// The chosen option's note shows under the row.</summary>
        protected Control ComboRow(Symbol icon, string title, string? hint, IReadOnlyList<Choice> choices,
            Func<string> get, Action<string> set, Panel? into = null, double width = 240, Action? changed = null,
            Func<Choice, bool>? enabled = null)
        {
            TextBlock note = null!;
            void ShowNote(int index) => SetHint(note, index >= 0 ? choices[index].Note : null);
            void Picked(int index)
            {
                set(choices[index].Value);
                ShowNote(index);
                Ws.SaveSoon();
                changed?.Invoke();
            }

            var shortLabels = choices.Sum(c => Segmented.Short(c.Label).Length);
            if (!SettingRow.Classic && choices.Count is >= 2 and <= 6 && shortLabels <= 72)
            {
                var segmented = new Segmented(choices, enabled);
                var row = Add(Card(icon, title, hint, segmented, out note, wide: true), into);
                segmented.Select(Choices.IndexOf(choices, get()), notify: false);
                ShowNote(segmented.SelectedIndex);
                segmented.Changed += Picked;
                row.Tag = note;
                return segmented;
            }

            var box = new ComboBox { Width = width, VerticalAlignment = VerticalAlignment.Center };
            foreach (var choice in choices)
                box.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value, IsEnabled = enabled?.Invoke(choice) ?? true });
            var card = Add(Card(icon, title, hint, box, out note), into);
            box.SelectedIndex = Choices.IndexOf(choices, get());
            ShowNote(box.SelectedIndex);
            box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) Picked(box.SelectedIndex); };
            card.Tag = note;
            return box;
        }

        protected Control SettingCombo(Symbol icon, string title, string? hint, IReadOnlyList<Choice> choices,
            string key, Panel? into = null, Action? changed = null) =>
            ComboRow(icon, title, hint, choices, () => S.Str(key), v => S.Set(key, v), into, changed: changed);

        protected Control IniCombo(Symbol icon, string title, string? hint, IReadOnlyList<Choice> choices,
            string key, Panel? into = null, Action? changed = null, Func<Choice, bool>? enabled = null) =>
            ComboRow(icon, title, hint, choices, () => Ini.Get(key), v => Ini.Set(key, v), into, changed: changed, enabled: enabled);

        /// <summary>A switch with no On/Off text beside it, as WPF-UI's.</summary>
        public static ToggleSwitch Switch(bool on) => new()
        {
            IsChecked = on, OnContent = null, OffContent = null, MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };

        protected ToggleSwitch ToggleRow(Symbol icon, string title, string? hint, Func<bool> get, Action<bool> set,
            Panel? into = null, Action? changed = null)
        {
            var toggle = Switch(get());
            // Click: the player's own changes only (not IsChecked set from code).
            toggle.Click += (_, _) =>
            {
                set(toggle.IsChecked == true);
                Ws.SaveSoon();
                changed?.Invoke();
            };
            Add(Card(icon, title, hint, toggle, out _), into);
            return toggle;
        }

        protected ToggleSwitch SettingToggle(Symbol icon, string title, string? hint, string key, Panel? into = null, Action? changed = null) =>
            ToggleRow(icon, title, hint, () => S.Bool(key), v => S.Set(key, v), into, changed);

        protected ToggleSwitch IniToggle(Symbol icon, string title, string? hint, string key, Panel? into = null, Action? changed = null) =>
            ToggleRow(icon, title, hint, () => Ini.Flag(key), v => Ini.Set(key, v ? "1" : "0"), into, changed);

        /// <summary>A slider with its value printed beside it.</summary>
        protected Slider SliderRow(Symbol icon, string title, string? hint, double min, double max, double step,
            Func<double> get, Action<double> set, string format = "0.00", Panel? into = null, double valueWidth = 44)
        {
            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                SmallChange = step,
                LargeChange = step * 4,
                TickFrequency = step,
                IsSnapToTickEnabled = true,
                Width = 260,
                VerticalAlignment = VerticalAlignment.Center,
                Value = Math.Clamp(get(), min, max),
            };
            var value = new TextBlock
            {
                Width = valueWidth,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            value.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            value.Text = slider.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                value.Text = slider.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
                set(slider.Value);
                Ws.SaveSoon();
            };
            Add(Card(icon, title, hint, Row(value, slider), out _), into);
            return slider;
        }

        /// <summary>A text field row; saves as you type.</summary>
        protected TextBox TextRow(Symbol icon, string title, string? hint, string placeholder,
            Func<string> get, Action<string> set, Panel? into = null, double width = 300)
        {
            var box = new TextBox
            {
                Text = get(),
                Watermark = placeholder,
                Width = width,
                VerticalAlignment = VerticalAlignment.Center,
            };
            box.TextChanged += (_, _) =>
            {
                set(box.Text ?? "");
                Ws.SaveSoon();
            };
            Add(Card(icon, title, hint, box, out _), into);
            return box;
        }

        protected static string Fmt(double value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        protected static double ParseDouble(string text, double fallback) =>
            double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v
                : fallback;

        protected static string? PickFolder(string title, string current) => Ui.PickFolder(title, current);
    }

    /// <summary>
    /// Makes the mouse wheel scroll the page. A card's ComboBox or slider would otherwise take the
    /// wheel even when it has nothing to scroll; this takes it in the tunnelling pass unless the
    /// control under the pointer really can scroll further that way.
    /// </summary>
    public static class PageScroll
    {
        public static void Attach(ScrollViewer page)
        {
            page.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
            {
                if (e.Handled || Owned(e.Source as Visual, page, e.Delta.Y)) return;
                page.Offset = new Vector(page.Offset.X, Math.Clamp(page.Offset.Y - e.Delta.Y * 50, 0, Math.Max(0, page.Extent.Height - page.Viewport.Height)));
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
        }

        private static bool Owned(Visual? source, ScrollViewer page, double delta)
        {
            while (source != null && !ReferenceEquals(source, page))
            {
                if (source is ScrollViewer inner && inner.Extent.Height > inner.Viewport.Height)
                {
                    var room = delta > 0 ? inner.Offset.Y > 0 : inner.Offset.Y < inner.Extent.Height - inner.Viewport.Height;
                    if (room) return true;
                }
                if (source is ComboBox { IsDropDownOpen: true }) return true;
                // The binding capture pad takes the wheel itself.
                if (source is Control { Tag: "capture-wheel" }) return true;
                source = source.GetVisualParent();
            }
            return false;
        }
    }
}
