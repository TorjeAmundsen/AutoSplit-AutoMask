using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace AutoSplit_AutoMask.Capture;

public sealed class CaptureController : IAsyncDisposable
{
    public const int CompareWidth = 320;
    public const int CompareHeight = 240;
    public const int TargetFps = 30;

    public readonly record struct CropRect(int X, int Y, int W, int H);

    public event Action<Bitmap, double, double, double>? FrameReady;
    public event Action<string>? ErrorReported;

    private readonly SemaphoreSlim _swapLock = new(1, 1);

    private Thread? _thread;
    private CancellationTokenSource? _cts;

    private ICaptureSource? _active;

    private byte[]? _refPixels;
    private byte[]? _refMask;
    private double _required;

    private CropRect _crop;
    private double _highest;

    public void UpdateReference(byte[]? refPixels, byte[]? refMask, double required)
    {
        _refPixels = refPixels;
        _refMask = refMask;
        _required = required;
        _highest = 0.0;
    }

    public void UpdateCrop(CropRect rect) => _crop = rect;

    public void ResetHighest() => _highest = 0.0;

    public async Task SetSourceAsync(ICaptureSource? newSource, CancellationToken ct)
    {
        await _swapLock.WaitAsync(ct);
        try
        {
            if (_active is not null)
            {
                try { await _active.StopAsync(); } catch { /* ignore */ }
                try { await _active.DisposeAsync(); } catch { /* ignore */ }
                _active = null;
            }

            if (newSource is not null)
            {
                await newSource.StartAsync(ct);
                _active = newSource;
            }
        }
        finally
        {
            _swapLock.Release();
        }
    }

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _thread = new Thread(() => Loop(ct))
        {
            IsBackground = true,
            Name = "AutoMask-Capture",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _thread?.Join(TimeSpan.FromMilliseconds(500));
        _thread = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void Loop(CancellationToken ct)
    {
        double frameMs = 1000.0 / TargetFps;
        var sw = Stopwatch.StartNew();
        double nextDueMs = 0.0;

        while (!ct.IsCancellationRequested)
        {
            ICaptureSource? src = _active;
            byte[]? refPixels = _refPixels;
            byte[]? refMask = _refMask;
            double required = _required;
            CropRect crop = _crop;

            if (src is null)
            {
                Thread.Sleep(20);
                continue;
            }

            try
            {
                if (!src.TryGrabFrame(out var raw) || raw is null)
                {
                    Thread.Sleep(2);
                    continue;
                }

                using var scaled = CropAndScaleNearest(raw, crop, CompareWidth, CompareHeight);
                if (scaled is null)
                {
                    Thread.Sleep(2);
                    continue;
                }

                double cur = 0;
                double high = _highest;

                if (refPixels is not null && refMask is not null)
                {
                    byte[] livePixels = ReadBgraBytes(scaled);
                    double similarity = Comparison.L2NormComparer.Compare(refPixels, refMask, livePixels);

                    if (similarity > _highest)
                    {
                        _highest = similarity;
                    }

                    cur = similarity;
                    high = _highest;
                }

                Bitmap uiBitmap = ImageProcessor.ToAvaloniaBitmap(scaled);

                Dispatcher.UIThread.Post(
                    () => FrameReady?.Invoke(uiBitmap, cur, high, required),
                    DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                Dispatcher.UIThread.Post(() => ErrorReported?.Invoke(msg));
                Thread.Sleep(200);
            }

            double elapsed = sw.Elapsed.TotalMilliseconds;
            nextDueMs += frameMs;
            double sleep = nextDueMs - elapsed;
            if (sleep > 1)
            {
                Thread.Sleep((int)sleep);
            }
            else if (sleep < -frameMs)
            {
                nextDueMs = elapsed;
            }
        }
    }

    private static SKBitmap? CropAndScaleNearest(SKBitmap source, CropRect crop, int outW, int outH)
    {
        int srcW = source.Width;
        int srcH = source.Height;

        int w = crop.W <= 0 ? srcW : crop.W;
        int h = crop.H <= 0 ? srcH : crop.H;
        if (w < 1 || h < 1)
        {
            return null;
        }

        // Fast path: full-frame, no offset.
        if (crop.X == 0 && crop.Y == 0 && w == srcW && h == srcH)
        {
            return source.Resize(
                new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Opaque),
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }

        // Paint the source into a fixed w×h canvas at offset (−X, −Y) so the crop size
        // defines the output rectangle and parts outside the source stay black.
        using var canvasBitmap = new SKBitmap(
            new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(canvasBitmap))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(source, -crop.X, -crop.Y);
        }

        return canvasBitmap.Resize(
            new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Opaque),
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
    }

    private static byte[] ReadBgraBytes(SKBitmap bitmap)
    {
        int byteCount = bitmap.ByteCount;
        byte[] result = new byte[byteCount];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), result, 0, byteCount);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        await SetSourceAsync(null, CancellationToken.None);
        _swapLock.Dispose();
    }
}
