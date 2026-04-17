using System.Runtime.Versioning;
using AutoSplit_AutoMask.Interop;

namespace AutoSplit_AutoMask.Capture;

[SupportedOSPlatform("windows")]
public sealed class RegionCapture : BitBltCaptureBase
{
    // Absolute virtual-screen coordinates.  Dimensions can be updated live via UpdateRect
    // when the user tweaks the crop spinboxes.
    private int _x, _y, _w, _h;

    public RegionCapture(int x, int y, int w, int h)
    {
        UpdateRect(x, y, w, h);
    }

    public override string DisplayName => "Screen region";

    public void UpdateRect(int x, int y, int w, int h)
    {
        _x = x;
        _y = y;
        _w = Math.Max(1, w);
        _h = Math.Max(1, h);
        SourceWidth = _w;
        SourceHeight = _h;
    }

    public static (int X, int Y, int W, int H) GetVirtualScreenRect() =>
    (
        Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN)
    );

    protected override bool AcquireSourceDc(out IntPtr sourceDc, out IntPtr owningHwnd,
        out int originX, out int originY, out int width, out int height)
    {
        owningHwnd = IntPtr.Zero; // GetDC(NULL) returns desktop DC; release with ReleaseDC(NULL, ...)
        originX = _x;
        originY = _y;
        width = _w;
        height = _h;

        sourceDc = Win32.GetDC(IntPtr.Zero);
        return sourceDc != IntPtr.Zero;
    }
}
