namespace AutoSplit_AutoMask.Capture;

public sealed class CamDeviceInfo
{
    public string Name { get; init; } = "";
    public int Index { get; init; }
}

// The webcam backend for this OS: DirectShow through OpenCV on Windows, AVFoundation on
// macOS, V4L2 on Linux.
public static class CaptureDevices
{
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    public static async Task<List<CamDeviceInfo>> EnumerateAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return [.. await WebcamCapture.EnumerateDevicesAsync()];
        }
        if (OperatingSystem.IsMacOS())
        {
            return await AvFoundationCapture.EnumerateDevicesAsync();
        }
        if (OperatingSystem.IsLinux())
        {
            return await V4L2Capture.EnumerateDevicesAsync();
        }
        return [];
    }

    public static ICaptureSource Create(CamDeviceInfo device)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WebcamCapture(device);
        }
        if (OperatingSystem.IsMacOS())
        {
            return new AvFoundationCapture(device);
        }
        if (OperatingSystem.IsLinux())
        {
            return new V4L2Capture(device);
        }
        throw new PlatformNotSupportedException("Webcam capture isn't supported on this OS.");
    }
}
