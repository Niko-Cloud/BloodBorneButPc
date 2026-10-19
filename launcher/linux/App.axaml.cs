using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace BloodborneLauncher
{
    public partial class App : Application
    {
        private static readonly string CrashLog = Path.Combine(Core.Paths.ConfigDir, "crash.log");

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            // A UI-thread fault is recoverable; the other two are only logged, so a hard exit
            // still leaves evidence behind.
            Dispatcher.UIThread.UnhandledException += (_, args) =>
            {
                Record("Dispatcher", args.Exception);
                Views.Prompt.Tell("Unexpected error", $"{args.Exception.Message}\n\nLogged to:\n{CrashLog}");
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Record("AppDomain", args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Record("UnobservedTask", args.Exception);
                args.SetObserved();
            };

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();
            base.OnFrameworkInitializationCompleted();
        }

        private static void Record(string source, Exception? exception)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);
                var entry = new StringBuilder()
                    .AppendLine(new string('=', 70))
                    .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{source}]")
                    .AppendLine(exception?.ToString() ?? "(no exception object)")
                    .AppendLine();
                File.AppendAllText(CrashLog, entry.ToString());
            }
            catch
            {
                // Logging must never be the thing that takes the app down.
            }
        }
    }
}
