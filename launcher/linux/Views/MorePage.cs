using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BloodborneLauncher.Core;

namespace BloodborneLauncher.Views
{
    /// <summary>The less used pages behind one tab: mods, online, the game dump, advanced settings and the game's log.</summary>
    public sealed class MorePage : LauncherPage
    {
        // Cheats only once the engine applies them.
        private static readonly (string Key, string Label, Type Page)[] Sections = new (string, string, Type)[]
        {
            ("mods", "Mods", typeof(ModsPage)),
            ("cheats", "Cheats", typeof(CheatsPage)),
            ("online", "Online", typeof(OnlinePage)),
            ("dump", "Dump game", typeof(DumpPage)),
            ("advanced", "Advanced", typeof(SettingsPage)),
            ("output", "Log", typeof(OutputPage)),
        }.Where(s => s.Item1 != "cheats" || CheatsPage.Supported).ToArray();

        private readonly Dictionary<Type, LauncherPage> _pages = new();
        private readonly ContentControl _frame = new();
        private readonly Segmented _tabs;
        private int _current = -1;

        public MorePage() : base("More", "")
        {
            _tabs = new Segmented(Sections.Select(s => new Choice(s.Label, s.Key)).ToArray())
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = Sections.Length * 104,
                Margin = new Thickness(0, 6, 0, 2),
            };
            _tabs.Changed += Open;

            var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            root.Children.Add(_tabs);
            Grid.SetRow(_frame, 1);
            root.Children.Add(_frame);
            Content = root;
            Open(0);
        }

        /// <summary>The section shown now (for --screenshot).</summary>
        public IEnumerable<string> SectionKeys => Sections.Select(s => s.Key);

        public void ShowSection(string key)
        {
            var index = Array.FindIndex(Sections, s => s.Key == key);
            if (index >= 0) Open(index);
        }

        private void Open(int index)
        {
            _tabs.Select(index, notify: false);
            var type = Sections[index].Page;
            if (!_pages.TryGetValue(type, out var page))
                _pages[type] = page = (LauncherPage)Activator.CreateInstance(type)!;
            if (_current != index) _frame.Content = page;
            _current = index;
            page.OnShown();
        }

        public override void OnShown()
        {
            if (_current >= 0 && _pages.TryGetValue(Sections[_current].Page, out var page))
                page.OnShown();
        }
    }
}
