using System.Diagnostics;
using Avalonia.Threading;
using SkiaSharp;

namespace AutoSplit_AutoMask.Capture;

public sealed class CaptureController : IAsyncDisposable
{
    public const int CompareWidth = 320;
    public const int CompareHeight = 240;
    public const int TargetFps = 60;

    public readonly record struct CropRect(int X, int Y, int W, int H);

    // Immutable snapshot of all fields that the UI thread writes and the capture loop reads.
    // Swapped as a single reference so the loop can never observe a torn (refPixels, refMask)
    // pair from different reference images.
    private sealed record CaptureState(
        byte[]? RefPixels,
        byte[]? RefMask,
        double Required,
        CropRect Crop);

    // Pixel buffer is BGRA, tightly packed, CompareWidth * CompareHeight * 4 bytes.
    public event Action<byte[], double, double, double>? FrameReady;
    public event Action<string>? ErrorReported;

    private readonly SemaphoreSlim _swapLock = new(1, 1);

    private Thread? _thread;
    private CancellationTokenSource? _cts;

    private ICaptureSource? _active;

    private CaptureState _state = new(null, null, 0.0, new CropRect(0, 0, 0, 0));
    private double _highest;
    private int _uiPostPending;

    // Double-buffer the live pixels so the capture loop can refill one buffer while the
    // UI handler still holds the previous one. _uiPostPending gates a single post in flight,
    // so two buffers are sufficient. Allocated once at construction; no per-frame GC churn.
    private readonly byte[][] _frameBuffers =
    {
        new byte[CompareWidth * CompareHeight * 4],
        new byte[CompareWidth * CompareHeight * 4],
    };
    private int _writeIndex;

    public void UpdateReference(byte[]? refPixels, byte[]? refMask, double required)
    {
        UpdateState(s => s with { RefPixels = refPixels, RefMask = refMask, Required = required });
        Interlocked.Exchange(ref _highest, 0.0);
    }

    public void UpdateCrop(CropRect rect)
    {
        UpdateState(s => s with { Crop = rect });
    }

    public void ResetHighest() => Interlocked.Exchange(ref _highest, 0.0);

    private void UpdateState(Func<CaptureState, CaptureState> mutate)
    {
        while (true)
        {
            var current = Volatile.Read(ref _state);
            var next = mutate(current);
            if (Interlocked.CompareExchange(ref _state, next, current) == current)
            {
                return;
            }
        }
    }

    private static readonly TimeSpan SourceShutdownTimeout = TimeSpan.FromSeconds(5);

    public async Task SetSourceAsync(ICaptureSource? newSource, CancellationToken ct)
    {
        await _swapLock.WaitAsync(ct);
        try
        {
            var current = Volatile.Read(ref _active);
            if (current is not null)
            {
                Volatile.Write(ref _active, null);
                await ShutdownSourceAsync(current);
            }

            if (newSource is not null)
            {
                await newSource.StartAsync(ct);
                Volatile.Write(ref _active, newSource);
            }
        }
        finally
        {
            _swapLock.Release();
        }
    }

    private async Task ShutdownSourceAsync(ICaptureSource source)
    {
        // Bounded shutdown so a hung native source (OpenCV deadlock during webcam release,
        // GDI driver stall) doesn't block the swap forever and prevent the user from
        // switching to a working source. Errors and timeouts surface via ErrorReported so
        // the UI can show why the previous source might still be holding resources.
        try
        {
            await source.StopAsync().WaitAsync(SourceShutdownTimeout);
        }
        catch (TimeoutException)
        {
            ReportShutdownIssue(source, "stop timed out");
        }
        catch (Exception ex)
        {
            ReportShutdownIssue(source, $"stop failed: {ex.Message}");
        }

        try
        {
            await source.DisposeAsync().AsTask().WaitAsync(SourceShutdownTimeout);
        }
        catch (TimeoutException)
        {
            ReportShutdownIssue(source, "dispose timed out");
        }
        catch (Exception ex)
        {
            ReportShutdownIssue(source, $"dispose failed: {ex.Message}");
        }
    }

    private void ReportShutdownIssue(ICaptureSource source, string detail)
    {
        var msg = $"Capture source '{source.DisplayName}' {detail}";
        Dispatcher.UIThread.Post(() => ErrorReported?.Invoke(msg));
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
        var stopwatch = Stopwatch.StartNew();
        double nextDueMs = 0.0;

        while (!ct.IsCancellationRequested)
        {
            ICaptureSource? source = Volatile.Read(ref _active);
            CaptureState state = Volatile.Read(ref _state);
            byte[]? refPixels = state.RefPixels;
            byte[]? refMask = state.RefMask;
            double required = state.Required;
            CropRect crop = state.Crop;

            if (source is null)
            {
                Thread.Sleep(20);
                continue;
            }

            try
            {
                if (!source.TryGrabFrame(out var raw) || raw is null)
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

                double currentSimilarity = 0;
                double highestSimilarity = Volatile.Read(ref _highest);

                byte[] livePixels = _frameBuffers[_writeIndex];
                if (!ReadBgraBytesInto(scaled, livePixels))
                {
                    Thread.Sleep(2);
                    continue;
                }

                if (refPixels is not null && refMask is not null
                    && refPixels.Length == livePixels.Length
                    && refMask.Length * 4 == livePixels.Length)
                {
                    double similarity = Comparison.L2NormComparer.Compare(refPixels, refMask, livePixels);

                    highestSimilarity = UpdateHighest(similarity);
                    currentSimilarity = similarity;
                }

                if (Interlocked.CompareExchange(ref _uiPostPending, 1, 0) == 0)
                {
                    byte[] frameBuffer = livePixels;

                    Dispatcher.UIThread.Post(
                        () =>
                        {
                            try
                            {
                                FrameReady?.Invoke(frameBuffer, currentSimilarity, highestSimilarity, required);
                            }
                            finally
                            {
                                Interlocked.Exchange(ref _uiPostPending, 0);
                            }
                        },
                        DispatcherPriority.Background);

                    // Flip only on a successful post so the UI handler keeps exclusive access
                    // to the buffer it received until it sets _uiPostPending back to 0.
                    _writeIndex ^= 1;
                }
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                Dispatcher.UIThread.Post(() => ErrorReported?.Invoke(msg));
                Thread.Sleep(200);
            }

            double elapsed = stopwatch.Elapsed.TotalMilliseconds;
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

        // Single-pass crop+scale: draw the crop rect of the source straight into the
        // output bitmap. Skia clamps src rects that fall outside source bounds and the
        // pre-cleared black background fills any uncovered area, matching the previous
        // canvas-then-resize behavior without the intermediate w×h SKBitmap allocation.
        var output = new SKBitmap(new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(output))
        using (var sourceImage = SKImage.FromBitmap(source))
        {
            canvas.Clear(SKColors.Black);
            var srcRect = new SKRect(crop.X, crop.Y, crop.X + w, crop.Y + h);
            var dstRect = new SKRect(0, 0, outW, outH);
            canvas.DrawImage(sourceImage, srcRect, dstRect,
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }
        return output;
    }

    private double UpdateHighest(double similarity)
    {
        while (true)
        {
            double current = Volatile.Read(ref _highest);
            if (similarity <= current)
            {
                return current;
            }
            if (Interlocked.CompareExchange(ref _highest, similarity, current) == current)
            {
                return similarity;
            }
        }
    }

    private static bool ReadBgraBytesInto(SKBitmap bitmap, byte[] destination)
    {
        int byteCount = bitmap.ByteCount;
        if (byteCount != destination.Length)
        {
            // Source resized between iterations or a non-target format slipped through;
            // skip this frame rather than tear the destination buffer.
            return false;
        }
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), destination, 0, byteCount);
        return true;
    }

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        // Idempotent: a second call would await the already-disposed _swapLock and throw
        // ObjectDisposedException. The TestOutputWindow Closing handler can fire twice
        // when the OS issues simultaneous close requests (Windows taskbar 'Close all
        // windows'), so the controller has to tolerate it.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Stop();
        await SetSourceAsync(null, CancellationToken.None);
        _swapLock.Dispose();
    }
}
