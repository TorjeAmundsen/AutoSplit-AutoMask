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
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", path);
        }
        else if (OperatingSystem.IsLinux())
        {
            Process.Start("xdg-open", path);
        }
    }

    /// <summary>
    /// Where presets/, splits/ and config/ live: next to the executable, except inside the
    /// macOS app bundle, where writing would break the bundle's signature and App Translocation
    /// can make it read-only. There they live in ~/Library/Application Support/AutoMask, and the
    /// bundled presets and splits are copied there when a folder doesn't exist yet.
    /// </summary>
    public static string GetDataDirectory()
    {
        // AppContext.BaseDirectory is the canonical app root; works under NativeAOT
        // self-contained where Process.MainModule may be platform-specific or null.
        string appDir = AppContext.BaseDirectory;
        bool inAppBundle = Path.TrimEndingDirectorySeparator(appDir)
            .EndsWith(Path.Combine("Contents", "MacOS"), StringComparison.Ordinal);
        if (!OperatingSystem.IsMacOS() || !inAppBundle)
        {
            return appDir;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dataDir = Path.Combine(home, "Library", "Application Support", "AutoMask");
        string resources = Path.GetFullPath(Path.Combine(appDir, "..", "Resources"));
        foreach (string folder in new[] { "presets", "splits" })
        {
            string target = Path.Combine(dataDir, folder);
            string source = Path.Combine(resources, folder);
            if (Directory.Exists(target) || !Directory.Exists(source))
            {
                continue;
            }

            // Copied under a temporary name first, so an interrupted copy is redone next launch
            string partial = target + ".partial";
            if (Directory.Exists(partial))
            {
                Directory.Delete(partial, recursive: true);
            }
            CopyDirectory(source, partial);
            Directory.Move(partial, target);
        }
        return dataDir;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        foreach (string dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    [Conditional("DEBUG")]
    public static void DebugLog(string message) => Console.WriteLine(message);

    [Conditional("DEBUG")]
    public static void LogError(string message) => Console.Error.WriteLine(message);

    /// <summary>
    /// Best-effort write of an exception to <c>%LOCALAPPDATA%\AutoMask\crashes</c> (XDG
    /// equivalent on Linux, <c>~/Library/Logs/AutoMask/crashes</c> on macOS). Used by the AppDomain, TaskScheduler, and Dispatcher
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
            string dir = OperatingSystem.IsMacOS()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Logs",
                    "AutoMask",
                    "crashes")
                : Path.Combine(
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
            // Nothing more to do - secondary failures during crash logging are not actionable.
        }
    }
}
