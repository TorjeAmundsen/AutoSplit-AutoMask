using Avalonia;

namespace AutoSplit_AutoMask;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Self-contained AOT publishes have no console attached and no debugger to observe
        // a crash; without these handlers a fatal exception silently exits. Crash logs land
        // in %LOCALAPPDATA%\AutoMask\crashes (XDG equivalent on Linux) for bug reports.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Utils.LogCrashToDisk(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Utils.LogCrashToDisk(e.Exception, "TaskScheduler.UnobservedTaskException");
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
}
