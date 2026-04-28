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

        if (w <= 0 || h <= 0)
        {
            Win32.ReleaseDC(owningHwnd, srcDc);
            return false;
        }

        SourceWidth = w;
        SourceHeight = h;

        EnsureBuffers(srcDc, w, h);

        bool ok = Win32.BitBlt(_memDc, 0, 0, w, h, srcDc, ox, oy, Win32.SRCCOPY | Win32.CAPTUREBLT);

        Win32.ReleaseDC(owningHwnd, srcDc);

        if (!ok)
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
            return false;
        }

        frame = _frame;
        return true;
    }

    private void EnsureBuffers(IntPtr srcDc, int w, int h)
    {
        if (_memDc != IntPtr.Zero && w == _allocatedWidth && h == _allocatedHeight)
        {
            return;
        }

        ReleaseGdiHandles();

        _memDc = Win32.CreateCompatibleDC(srcDc);
        _gdiBitmap = Win32.CreateCompatibleBitmap(srcDc, w, h);
        _oldObject = Win32.SelectObject(_memDc, _gdiBitmap);
        _allocatedWidth = w;
        _allocatedHeight = h;
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
