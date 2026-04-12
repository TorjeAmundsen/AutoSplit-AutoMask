using System.Diagnostics;

namespace AutoSplit_AutoMask;

public static class Utils
{
    public const string AutoMaskSemVer = "0.4.0";
    public const string VersionSuffix = "alpha";

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
}
