using Avalonia;
using Avalonia.Headless;

namespace BloodborneLauncher
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args) =>
            (args.Contains("--screenshot") ? BuildHeadless() : BuildAvaloniaApp())
                .StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        /// <summary>--screenshot &lt;folder&gt;: no window on the desktop; Skia draws the pages off-screen.</summary>
        private static AppBuilder BuildHeadless() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .WithInterFont()
                .LogToTrace();
    }
}
