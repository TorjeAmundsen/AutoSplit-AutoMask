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
    private readonly CamDeviceInfo _device;
    private readonly string _displayName;

    private VideoCapture? _cap;
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
        return Task.Run<IReadOnlyList<CamDeviceInfo>>(() =>
        {
            var names = DirectShow.EnumerateVideoCaptureNames();
            var list = new List<CamDeviceInfo>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                list.Add(new CamDeviceInfo { Name = names[i], Index = i });
            }
            return list;
        });
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cap = new VideoCapture(_device.Index, VideoCaptureAPIs.DSHOW);
        if (!_cap.IsOpened())
        {
            _cap.Dispose();
            _cap = null;
            throw new InvalidOperationException($"Could not open webcam: {_device.Name}");
        }

        _cap.Set(VideoCaptureProperties.FrameWidth, 1920);
        _cap.Set(VideoCaptureProperties.FrameHeight, 1080);
        _cap.Set(VideoCaptureProperties.Fps, 60);

        _widthPx = (int)_cap.Get(VideoCaptureProperties.FrameWidth);
        _heightPx = (int)_cap.Get(VideoCaptureProperties.FrameHeight);
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
                if (!_cap!.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(5);
                    continue;
                }
            }
            catch
            {
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
        byte* dst = (byte*)target.GetPixels();

        for (int y = 0; y < h; y++)
        {
            byte* s = (byte*)frame.Ptr(y);
            byte* d = dst + (long)y * w * 4;
            for (int x = 0; x < w; x++)
            {
                d[0] = s[0];
                d[1] = s[1];
                d[2] = s[2];
                d[3] = 255;
                s += 3;
                d += 4;
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
        catch
        {
            // ignored
        }

        _thread?.Join(1000);
        _thread = null;

        _cap?.Release();
        _cap?.Dispose();
        _cap = null;

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
