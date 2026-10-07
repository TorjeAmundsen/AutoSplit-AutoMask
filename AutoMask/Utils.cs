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
    /// can make it read-only. There they live in ~/Library/Application Support/AutoMask. The
    /// bundled presets and splits are copied there on first launch, and files a new version
    /// adds are copied on its first launch. Files already there are kept, except the JSON schemas.
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
        Directory.CreateDirectory(dataDir);
        // A second instance (open -n, or the executable started directly) would otherwise copy
        // into the same .partial folder and can leave a folder with files missing
        using (AcquireSeedLock(Path.Combine(dataDir, ".seed.lock")))
        {
            // Only after an update, so presets the user deleted don't come back on every launch
            string versionFile = Path.Combine(dataDir, ".bundled-version");
            bool newVersion = !File.Exists(versionFile) || File.ReadAllText(versionFile) != AutoMaskVersion;
            foreach (string folder in new[] { "presets", "splits" })
            {
                string target = Path.Combine(dataDir, folder);
                string source = Path.Combine(resources, folder);
                if (!Directory.Exists(source))
                {
                    continue;
                }
                if (Directory.Exists(target))
                {
                    if (newVersion)
                    {
                        CopyMissingFiles(source, target);
                        // The JSON schemas are the app's, not the user's, so they follow the version
                        foreach (string schema in Directory.EnumerateFiles(source, "*-schema.json"))
                        {
                            File.Copy(schema, Path.Combine(target, Path.GetFileName(schema)), overwrite: true);
                        }
                    }
                    continue;
                }

                // Copied under a temporary name first, so an interrupted copy is redone next launch
                string partial = target + ".partial";
                if (Directory.Exists(partial))
                {
                    Directory.Delete(partial, recursive: true);
                }
                CopyMissingFiles(source, partial);
                Directory.Move(partial, target);
            }
            if (newVersion)
            {
                File.WriteAllText(versionFile, AutoMaskVersion);
            }
        }
        return dataDir;
    }

    // FileShare.None locks the file for other processes too (flock on macOS and Linux), and the
    // lock goes away with the process if it dies mid-copy.
    private static FileStream AcquireSeedLock(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static void CopyMissingFiles(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            string destination = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(destination))
            {
                File.Copy(file, destination);
            }
        }
        foreach (string dir in Directory.EnumerateDirectories(source))
        {
            CopyMissingFiles(dir, Path.Combine(target, Path.GetFileName(dir)));
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
