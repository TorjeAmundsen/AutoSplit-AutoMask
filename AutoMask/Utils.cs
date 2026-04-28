using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace AutoSplit_AutoMask;

public static class Utils
{
    public static readonly string AutoMaskVersion =
        typeof(Utils).Assembly
                     .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                     ?.InformationalVersion ?? "unknown";

    public static void OpenInFileManager(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start("explorer.exe", $"\"{path}\"");
        }
        else if (OperatingSystem.IsLinux())
        {
            Process.Start("xdg-open", path);
        }
    }

    [Conditional("DEBUG")]
    public static void DebugLog(string message) => Console.WriteLine(message);

    [Conditional("DEBUG")]
    public static void LogError(string message) => Console.Error.WriteLine(message);

    /// <summary>
    /// Best-effort write of an exception to <c>%LOCALAPPDATA%\AutoMask\crashes</c> (XDG
    /// equivalent on Linux). Used by the AppDomain, TaskScheduler, and Dispatcher
    /// unhandled-exception hooks so a self-contained AOT publish has something to attach
    /// to a bug report when there's no console or debugger available.
    /// </summary>
    public static void LogCrashToDisk(Exception? ex, string source)
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
                          $"Version:   {AutoMaskVersion}\n\n" +
                          ex.ToString();
            File.WriteAllText(path, body, Encoding.UTF8);
        }
        catch
        {
            // Nothing more to do — secondary failures during crash logging are not actionable.
        }
    }
}
