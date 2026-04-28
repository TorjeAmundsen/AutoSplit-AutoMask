using System.Text;
using Avalonia;

namespace AutoSplit_AutoMask;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // For self-contained AOT publishes there is no console attached and no debugger to
        // observe a crash; without these handlers, fatal exceptions produce a silent exit.
        // Crash logs land in %LOCALAPPDATA%\AutoMask\crashes (or the XDG equivalent on Linux)
        // so users can attach one to a bug report.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void LogCrash(Exception? ex, string source)
    {
        if (ex is null)
        {
            return;
        }

        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoMask",
                "crashes");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{source}.log");
            string body = $"Timestamp: {DateTime.Now:O}\n" +
                          $"Source:    {source}\n" +
                          $"Version:   {Utils.AutoMaskVersion}\n\n" +
                          ex.ToString();
            File.WriteAllText(path, body, Encoding.UTF8);
        }
        catch
        {
            // Nothing more to do — the process is already on its way out for AppDomain.
        }
    }
}
