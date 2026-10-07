using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AutoSplit_AutoMask;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string dataDirectory;
            try
            {
                dataDirectory = Utils.GetDataDirectory();
                Directory.CreateDirectory(Path.Combine(dataDirectory, "presets"));
                Directory.CreateDirectory(Path.Combine(dataDirectory, "config"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ShowDataDirectoryErrorAndQuit(desktop, ex);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            desktop.MainWindow = new MainWindow(dataDirectory);

            // Catches uncaught exceptions raised on the UI dispatcher (including async-void
            // event handlers' continuations). Setting Handled = true keeps the app running
            // instead of bubbling to AppDomain.UnhandledException and terminating; the user
            // gets a MessageBox and can retry the action that failed.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Utils.LogCrashToDisk(e.Exception, "Dispatcher.UnhandledException");

                try
                {
                    if (desktop.MainWindow is { IsVisible: true } mainWindow)
                    {
                        _ = MessageBox.Show(mainWindow, "Something went wrong", e.Exception.Message);
                    }
                }
                catch
                {
                    // Avalonia's documentation explicitly warns against allocating or
                    // performing resource-heavy work in this handler - secondary failures
                    // here can't be reported anywhere useful.
                }

                e.Handled = true;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowDataDirectoryErrorAndQuit(IClassicDesktopStyleApplicationLifetime desktop, Exception ex)
    {
        // Quit when the box closes, also if it fails to show
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            await MessageBox.Show(null, "Error",
                $"AutoMask couldn't set up the folder for its presets and splits:\n\n{ex.Message}");
        }
        finally
        {
            desktop.Shutdown(1);
        }
    }
}
