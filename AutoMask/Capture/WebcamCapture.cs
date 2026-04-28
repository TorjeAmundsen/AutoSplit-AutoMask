using System.Runtime.Versioning;
using AutoSplit_AutoMask.Interop;
using OpenCvSharp;
using SkiaSharp;

namespace AutoSplit_AutoMask.Capture;

public sealed class CamDeviceInfo
{
    public string Name { get; init; } = "";
    public int Index { get; init; }
}

[SupportedOSPlatform("windows")]
public sealed class WebcamCapture : ICaptureSource
{
    public event Action<string>? ErrorReported;

    private readonly CamDeviceInfo _device;
    private readonly string _displayName;

    private VideoCapture? _videoCapture;
    private Thread? _thread;
    private CancellationTokenSource? _cts;

    private int _widthPx;
    private int _heightPx;

    private readonly object _gate = new();
    private SKBitmap? _latest;
    private SKBitmap? _handedOut;

    public WebcamCapture(CamDeviceInfo device)
    {
        _device = device;
        _displayName = $"Webcam: {device.Name}";
    }

    public string DisplayName => _displayName;
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }

    public static Task<IReadOnlyList<CamDeviceInfo>> EnumerateDevicesAsync()
    {
        // Run on a dedicated short-lived STA thread instead of a thread pool worker.
        // DirectShow's CoInitializeEx call would otherwise leave the pool thread
        // MTA-tainted with no matching CoUninitialize - when the runtime later reuses
        // that thread (e.g. for an await continuation), shell COM marshaling between
        // STA UI and the dirty MTA pool thread deadlocks IFileDialog.Show, freezing
        // any subsequent file picker app-wide.
        var tcs = new TaskCompletionSource<IReadOnlyList<CamDeviceInfo>>();
        var thread = new Thread(() =>
        {
            try
            {
                var names = DirectShow.EnumerateVideoCaptureNames();
                var list = new List<CamDeviceInfo>(names.Count);
                for (int i = 0; i < names.Count; i++)
                {
                    list.Add(new CamDeviceInfo { Name = names[i], Index = i });
                }
                tcs.SetResult(list);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Webcam Enum (STA)",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _videoCapture = new VideoCapture(_device.Index, VideoCaptureAPIs.DSHOW);
        if (!_videoCapture.IsOpened())
        {
            _videoCapture.Dispose();
            _videoCapture = null;
            throw new InvalidOperationException($"Could not open webcam: {_device.Name}");
        }

        _videoCapture.Set(VideoCaptureProperties.FrameWidth, 1920);
        _videoCapture.Set(VideoCaptureProperties.FrameHeight, 1080);
        _videoCapture.Set(VideoCaptureProperties.Fps, 60);

        _widthPx = (int)_videoCapture.Get(VideoCaptureProperties.FrameWidth);
        _heightPx = (int)_videoCapture.Get(VideoCaptureProperties.FrameHeight);
        if (_widthPx <= 0 || _heightPx <= 0)
        {
            _widthPx = 640;
            _heightPx = 480;
        }

        SourceWidth = _widthPx;
        SourceHeight = _heightPx;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "Webcam Capture" };
        _thread.Start();

        return Task.CompletedTask;
    }

    private void CaptureLoop()
    {
        var ct = _cts!.Token;
        using var frame = new Mat();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_videoCapture!.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(5);
                    continue;
                }
            }
            catch (Exception ex)
            {
                // OpenCV native errors (device unplugged, codec failure, OOM during
                // frame allocation) — surface to UI so the live tester can show why
                // the feed stopped instead of silently freezing.
                var msg = $"Webcam read failed: {ex.Message}";
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ErrorReported?.Invoke(msg));
                break;
            }

            CopyBgrMatToLatest(frame);
        }
    }

    private unsafe void CopyBgrMatToLatest(Mat frame)
    {
        int w = frame.Cols;
        int h = frame.Rows;
        if (w <= 0 || h <= 0 || frame.Channels() != 3)
        {
            return;
        }

        var target = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        byte* targetPixels = (byte*)target.GetPixels();

        for (int y = 0; y < h; y++)
        {
            byte* sourceRow = (byte*)frame.Ptr(y);
            byte* targetRow = targetPixels + (long)y * w * 4;
            for (int x = 0; x < w; x++)
            {
                targetRow[0] = sourceRow[0];
                targetRow[1] = sourceRow[1];
                targetRow[2] = sourceRow[2];
                targetRow[3] = 255;
                sourceRow += 3;
                targetRow += 4;
            }
        }

        SKBitmap? toDispose;
        lock (_gate)
        {
            toDispose = _latest;
            _latest = target;
        }
        toDispose?.Dispose();
    }

    public bool TryGrabFrame(out SKBitmap? frame)
    {
        SKBitmap? pending;
        SKBitmap? toDispose;

        lock (_gate)
        {
            pending = _latest;
            _latest = null;
            toDispose = pending is null ? null : _handedOut;
            if (pending is not null)
            {
                _handedOut = pending;
            }
        }

        toDispose?.Dispose();

        if (pending is null)
        {
            frame = null;
            return false;
        }

        frame = pending;
        return true;
    }

    public Task StopAsync()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _thread?.Join(1000);
        _thread = null;

        _videoCapture?.Release();
        _videoCapture?.Dispose();
        _videoCapture = null;

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        lock (_gate)
        {
            _latest?.Dispose();
            _latest = null;
            _handedOut?.Dispose();
            _handedOut = null;
        }
    }
}
