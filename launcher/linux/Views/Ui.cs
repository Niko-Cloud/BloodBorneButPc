using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace BloodborneLauncher.Views
{
    /// <summary>WPF-UI's button looks: the main action, a plain one, a borderless one, a destructive one.</summary>
    public enum ControlAppearance { Primary, Secondary, Transparent, Danger }

    public enum InfoBarSeverity { Informational, Success, Warning, Error }

    /// <summary>
    /// What the Windows launcher had from WPF and WPF-UI and Avalonia does differently: resource
    /// references set in code, and synchronous pickers, clipboard and dialogs (a nested dispatcher
    /// frame waits for Avalonia's asynchronous ones, as WPF's ShowDialog does).
    /// </summary>
    public static class Ui
    {
        /// <summary>WPF's SetResourceReference: the property follows the resource, theme changes included.</summary>
        public static void SetResourceReference(this StyledElement target, AvaloniaProperty property, string key) =>
            target.Bind(property, target.GetResourceObservable(key));

        public static Window? MainWindow =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        public static IBrush Brush(string key) =>
            Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true && value is IBrush brush
                ? brush
                : Brushes.White;

        /// <summary>Waits for a task on the UI thread, which keeps handling input and drawing meanwhile.</summary>
        public static T Wait<T>(Task<T> task)
        {
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
                Dispatcher.UIThread.PushFrame(frame);
            }
            return task.GetAwaiter().GetResult();
        }

        public static void Wait(Task task) => Wait(task.ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }, TaskScheduler.Default));

        private static IStorageProvider? Storage => MainWindow?.StorageProvider;

        private static IStorageFolder? Folder(IStorageProvider storage, string path) =>
            !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? Wait(storage.TryGetFolderFromPathAsync(path)) : null;

        /// <summary>A folder, or null when cancelled.</summary>
        public static string? PickFolder(string title, string current) =>
            Dialog(storage =>
            {
                var picked = Wait(storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                    SuggestedStartLocation = Folder(storage, current),
                }));
                return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            }, title, current, "--directory", Directory.Exists(current) ? $"--filename={current.TrimEnd('/')}/" : null);

        /// <summary>A file to open, or null. filter is WPF's: "Name (*.a;*.b)|*.a;*.b|All files|*.*".</summary>
        public static string? OpenFile(string title, string filter = "", string start = "") =>
            Dialog(storage =>
            {
                var picked = Wait(storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                    FileTypeFilter = Types(filter),
                    SuggestedStartLocation = Folder(storage, start),
                }));
                return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            }, title, start, ZenityFilters(filter).Prepend(Directory.Exists(start) ? $"--filename={start.TrimEnd('/')}/" : null).ToArray());

        /// <summary>Where to save a file, or null.</summary>
        public static string? SaveFile(string title, string fileName, string filter = "") =>
            Dialog(storage =>
            {
                var picked = Wait(storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = title,
                    SuggestedFileName = fileName,
                    FileTypeChoices = Types(filter),
                    DefaultExtension = Path.GetExtension(fileName).TrimStart('.'),
                    ShowOverwritePrompt = true,
                }));
                return picked?.TryGetLocalPath();
            }, title, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fileName),
                ZenityFilters(filter).Prepend($"--filename={fileName}").Prepend("--save").ToArray());

        /// <summary>The desktop's file dialog (xdg-desktop-portal on Linux) failed at once: zenity from then on.</summary>
        private static bool _portalBroken;

        /// <summary>
        /// The desktop's own file dialog. On Linux it goes through xdg-desktop-portal, which can fail
        /// without showing anything (GNOME's portal on niri, whose Nautilus hand-off drops): an empty
        /// answer sooner than anyone could press Cancel means that, and zenity is used instead, then
        /// a path typed into a prompt when there is no zenity either.
        /// </summary>
        private static string? Dialog(Func<IStorageProvider, string?> portal, string title, string current, params string?[] zenity)
        {
            if (!_portalBroken || OperatingSystem.IsWindows())
            {
                if (Storage is not { } storage) return null;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                string? picked = null;
                try
                {
                    picked = portal(storage);
                }
                catch (Exception e)
                {
                    Core.Workspace.Current.Log("The file dialog failed: " + e.Message, Core.LogLevel.Warn);
                }
                if (picked != null || OperatingSystem.IsWindows() || watch.ElapsedMilliseconds > 600) return picked;
                _portalBroken = true;
                Core.Workspace.Current.Log("The desktop's file dialog did not open (xdg-desktop-portal); using zenity.", Core.LogLevel.Warn);
            }
            return Zenity(title, zenity) is var (ran, path) && ran
                ? path
                : Prompt.Ask(title, "No file dialog is available on this desktop (install zenity for one). Type the path instead.",
                    new Prompt.Field("Path", current)) is [var typed] && typed.Trim().Length > 0 ? typed.Trim() : null;
        }

        /// <summary>zenity --file-selection: (true, the path or null when cancelled), or (false, null)
        /// when there is no zenity.</summary>
        private static (bool Ran, string? Path) Zenity(string title, string?[] arguments)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("zenity") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                info.ArgumentList.Add("--file-selection");
                info.ArgumentList.Add($"--title={title}");
                foreach (var argument in arguments.Where(a => a != null)) info.ArgumentList.Add(argument!);
                using var process = System.Diagnostics.Process.Start(info)!;
                var output = process.StandardOutput.ReadToEndAsync();
                Wait(process.WaitForExitAsync());
                var path = Wait(output).Trim();
                return (true, process.ExitCode == 0 && path.Length > 0 ? path : null);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return (false, null);
            }
        }

        /// <summary>WPF's filter as zenity's: "Name | *.a *.b" for each pair.</summary>
        private static IEnumerable<string> ZenityFilters(string filter)
        {
            var parts = filter.Split('|');
            for (var i = 0; i + 1 < parts.Length; i += 2)
                yield return $"--file-filter={parts[i]} | {parts[i + 1].Replace(';', ' ')}";
        }

        private static List<FilePickerFileType>? Types(string filter)
        {
            var parts = filter.Split('|');
            if (parts.Length < 2) return null;
            var types = new List<FilePickerFileType>();
            for (var i = 0; i + 1 < parts.Length; i += 2)
                types.Add(new FilePickerFileType(parts[i]) { Patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries) });
            return types;
        }

        public static void CopyText(string text)
        {
            if (MainWindow?.Clipboard is { } clipboard)
                Wait(clipboard.SetTextAsync(text));
        }

        public static string? PasteText() => MainWindow?.Clipboard is { } clipboard ? Wait(clipboard.TryGetTextAsync()) : null;

        /// <summary>A web page or folder in the desktop's own program.</summary>
        public static void Open(string target)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception e)
            {
                Core.Workspace.Current.Log($"Could not open {target}: {e.Message}", Core.LogLevel.Warn);
            }
        }

        /// <summary>This launcher as a program to start: the .AppImage file it runs from (its mount
        /// point changes every start), else its own executable (the apphost next to the assembly),
        /// also when it was started as "dotnet bloodborne-launcher.dll".</summary>
        public static string? LauncherExecutable
        {
            get
            {
                if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appimage && File.Exists(appimage))
                    return appimage;
                var apphost = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "bloodborne-launcher.exe" : "bloodborne-launcher");
                return File.Exists(apphost) ? apphost : Environment.ProcessPath;
            }
        }

        /// <summary>Starts this launcher again and closes this one.</summary>
        public static void Restart()
        {
            Core.Workspace.Current.SaveNow();
            if (LauncherExecutable is { } self)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(self)
                    { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(self)! })?.Dispose();
            Shutdown();
        }

        public static void Shutdown() =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

        public static void SetTip(this Control control, object? tip)
        {
            ToolTip.SetTip(control, tip);
            ToolTip.SetShowDelay(control, 400);
        }

        public static SymbolIcon Icon(Symbol symbol, double size = 16) => new() { Symbol = symbol, FontSize = size };
    }

    /// <summary>WPF-UI's Button: a label with an icon, in one of four looks.</summary>
    public class UiButton : Button
    {
        private readonly SymbolIcon _icon = new() { FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal, Spacing = 6 };

        public UiButton()
        {
            _panel.Children.Add(_icon);
            _panel.Children.Add(_label);
            Content = _panel;
            _icon.IsVisible = false;
            _label.IsVisible = false;
            VerticalContentAlignment = VerticalAlignment.Center;
            Appearance = ControlAppearance.Secondary;
        }

        protected override Type StyleKeyOverride => typeof(Button);

        public string? Label
        {
            get => _label.Text;
            set
            {
                _label.Text = value;
                _label.IsVisible = !string.IsNullOrEmpty(value);
            }
        }

        public Symbol? Glyph
        {
            get => _icon.IsVisible ? _icon.Symbol : null;
            set
            {
                if (value is { } symbol) _icon.Symbol = symbol;
                _icon.IsVisible = value != null;
            }
        }

        private ControlAppearance _appearance;

        public ControlAppearance Appearance
        {
            get => _appearance;
            set
            {
                _appearance = value;
                Classes.Set("accent", value == ControlAppearance.Primary);
                Classes.Set("Transparent", value == ControlAppearance.Transparent);
                Classes.Set("Danger", value == ControlAppearance.Danger);
            }
        }

        public FontWeight LabelWeight
        {
            get => _label.FontWeight;
            set => _label.FontWeight = value;
        }
    }

    /// <summary>WPF-UI's InfoBar: a tinted box with an icon, a title and a message; IsOpen shows it.</summary>
    public sealed class InfoBar : Border
    {
        private readonly SymbolIcon _icon = new() { FontSize = 18, Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, FontSize = 13.5 };
        private readonly TextBlock _message = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        private readonly StackPanel _text = new();

        public InfoBar()
        {
            CornerRadius = new CornerRadius(8);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(14, 10, 14, 10);
            _text.Children.Add(_title);
            _text.Children.Add(_message);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grid.Children.Add(_icon);
            Grid.SetColumn(_text, 1);
            grid.Children.Add(_text);
            Child = grid;
            _title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            _message.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            Severity = InfoBarSeverity.Informational;
            IsOpen = false;
        }

        public bool IsOpen { get => IsVisible; set => IsVisible = value; }

        /// <summary>Kept for the Windows code's sake; this bar has no close button.</summary>
        public bool IsClosable { get; set; }

        public string? Title
        {
            get => _title.Text;
            set { _title.Text = value; _title.IsVisible = !string.IsNullOrEmpty(value); }
        }

        public string? Message
        {
            get => _message.Text;
            set { _message.Text = value; _message.IsVisible = !string.IsNullOrEmpty(value); }
        }

        private InfoBarSeverity _severity;

        public InfoBarSeverity Severity
        {
            get => _severity;
            set
            {
                _severity = value;
                var (symbol, color) = value switch
                {
                    InfoBarSeverity.Error => (Symbol.ErrorCircle, Color.Parse("#FFFF6B6B")),
                    InfoBarSeverity.Warning => (Symbol.Warning, Color.Parse("#FFF2B84B")),
                    InfoBarSeverity.Success => (Symbol.CheckmarkCircle, Color.Parse("#FF5FD38A")),
                    _ => (Symbol.Info, Color.Parse("#FF4C9DFF")),
                };
                _icon.Symbol = symbol;
                _icon.Foreground = new SolidColorBrush(color);
                Background = new SolidColorBrush(color, 0.12);
                BorderBrush = new SolidColorBrush(color, 0.35);
            }
        }
    }
}
