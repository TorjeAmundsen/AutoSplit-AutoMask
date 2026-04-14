using AutoSplit_AutoMask.Interop;

namespace AutoSplit_AutoMask.Capture;

public sealed class WindowCapture : BitBltCaptureBase
{
    public readonly record struct WindowHandle(IntPtr Hwnd, string Title);

    private readonly IntPtr _hwnd;
    private readonly string _title;

    private WindowCapture(IntPtr hwnd, string title)
    {
        _hwnd = hwnd;
        _title = title;
    }

    public override string DisplayName => $"Window: {_title}";

    public static WindowCapture Create(WindowHandle handle) => new(handle.Hwnd, handle.Title);

    public static IReadOnlyList<WindowHandle> ListWindows()
    {
        var result = new List<WindowHandle>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd))
            {
                return true;
            }

            int len = Win32.GetWindowTextLength(hwnd);
            if (len <= 0)
            {
                return true;
            }

            var buf = new char[len + 1];
            int copied = Win32.GetWindowText(hwnd, buf, buf.Length);
            if (copied <= 0)
            {
                return true;
            }

            var title = new string(buf, 0, copied);
            result.Add(new WindowHandle(hwnd, title));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    protected override bool AcquireSourceDc(out IntPtr sourceDc, out IntPtr owningHwnd,
        out int originX, out int originY, out int width, out int height)
    {
        sourceDc = IntPtr.Zero;
        owningHwnd = _hwnd;
        originX = 0;
        originY = 0;
        width = 0;
        height = 0;

        if (!Win32.GetClientRect(_hwnd, out var rect))
        {
            return false;
        }

        width = rect.Width;
        height = rect.Height;

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        sourceDc = Win32.GetDC(_hwnd);
        return sourceDc != IntPtr.Zero;
    }
}
