using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AutoSplit_AutoMask.Interop;
using SkiaSharp;

namespace AutoSplit_AutoMask.Capture;

[SupportedOSPlatform("windows")]
public abstract class BitBltCaptureBase : ICaptureSource
{
    public abstract string DisplayName { get; }
    public int SourceWidth { get; protected set; }
    public int SourceHeight { get; protected set; }

    // Returned to callers.  Backing pixel memory is owned by us and stable across calls as
    // long as dimensions don't change.
    protected SKBitmap? _frame;

    private IntPtr _memDc = IntPtr.Zero;
    private IntPtr _gdiBitmap = IntPtr.Zero;
    private IntPtr _oldObject = IntPtr.Zero;
    private int _allocatedWidth;
    private int _allocatedHeight;

    public virtual Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public virtual Task StopAsync() => Task.CompletedTask;

    // Subclasses return the HDC to blit from (window client DC or desktop DC), the origin
    // offset within that DC, and the current source dimensions.  `releaseDc` tells us how
    // to return the DC after the blit.
    protected abstract bool AcquireSourceDc(out IntPtr sourceDc, out IntPtr owningHwnd,
        out int originX, out int originY, out int width, out int height);

    public bool TryGrabFrame(out SKBitmap? frame)
    {
        frame = null;

        if (!AcquireSourceDc(out var srcDc, out var owningHwnd, out int ox, out int oy, out int w, out int h))
        {
            return false;
        }

        // try/finally so any throw between acquire and release (EnsureBuffers GDI exhaustion,
        // BitBlt fault, allocator OOM) doesn't strand srcDc. ReleaseDC must run for caret-DC
        // and window-DC paths or the system DC pool fills up.
        bool blitOk;
        try
        {
            if (w <= 0 || h <= 0)
            {
                return false;
            }

            SourceWidth = w;
            SourceHeight = h;

            if (!EnsureBuffers(srcDc, w, h))
            {
                return false;
            }

            blitOk = Win32.BitBlt(_memDc, 0, 0, w, h, srcDc, ox, oy, Win32.SRCCOPY | Win32.CAPTUREBLT);
        }
        finally
        {
            Win32.ReleaseDC(owningHwnd, srcDc);
        }

        if (!blitOk)
        {
            return false;
        }

        var bmi = new Win32.BITMAPINFO
        {
            bmiHeader = new Win32.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                biWidth = w,
                // Negative for a top-down DIB so the byte layout matches SkiaSharp's default.
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Win32.BI_RGB,
            }
        };

        if (_frame is null || _frame.Width != w || _frame.Height != h)
        {
            _frame?.Dispose();
            _frame = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        }

        IntPtr pixels = _frame.GetPixels();
        int scans = Win32.GetDIBits(_memDc, _gdiBitmap, 0, (uint)h, pixels, ref bmi, Win32.DIB_RGB_COLORS);

        if (scans == 0)
        {
            // Drop the bitmap so a transient GetDIBits failure (e.g. driver glitch on
            // resolution change) doesn't leave a half-populated buffer that a later
            // size-match call would skip re-allocating.
            _frame.Dispose();
            _frame = null;
            return false;
        }

        frame = _frame;
        return true;
    }

    private bool EnsureBuffers(IntPtr srcDc, int w, int h)
    {
        if (_memDc != IntPtr.Zero && w == _allocatedWidth && h == _allocatedHeight)
        {
            return true;
        }

        ReleaseGdiHandles();

        IntPtr memDc = Win32.CreateCompatibleDC(srcDc);
        if (memDc == IntPtr.Zero)
        {
            return false;
        }

        IntPtr gdiBitmap = Win32.CreateCompatibleBitmap(srcDc, w, h);
        if (gdiBitmap == IntPtr.Zero)
        {
            Win32.DeleteDC(memDc);
            return false;
        }

        IntPtr oldObject = Win32.SelectObject(memDc, gdiBitmap);
        if (oldObject == IntPtr.Zero)
        {
            Win32.DeleteObject(gdiBitmap);
            Win32.DeleteDC(memDc);
            return false;
        }

        _memDc = memDc;
        _gdiBitmap = gdiBitmap;
        _oldObject = oldObject;
        _allocatedWidth = w;
        _allocatedHeight = h;
        return true;
    }

    private void ReleaseGdiHandles()
    {
        if (_memDc != IntPtr.Zero)
        {
            if (_oldObject != IntPtr.Zero)
            {
                Win32.SelectObject(_memDc, _oldObject);
                _oldObject = IntPtr.Zero;
            }

            Win32.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }

        if (_gdiBitmap != IntPtr.Zero)
        {
            Win32.DeleteObject(_gdiBitmap);
            _gdiBitmap = IntPtr.Zero;
        }
    }

    public ValueTask DisposeAsync()
    {
        ReleaseGdiHandles();
        _frame?.Dispose();
        _frame = null;
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
