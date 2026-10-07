using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using AutoSplit_AutoMask.Interop;
using SkiaSharp;

namespace AutoSplit_AutoMask.Capture;

// DirectShow capture, ported from AutoSplitRewrite's autosplit-capture/src/dshow.rs, which
// replicates OpenCV 4.11's videoInput (cap_dshow.cpp) that AutoSplit's cv2.VideoCapture(index)
// uses on Windows: same device order, same format negotiation and the same Sample Grabber setup,
// so Windows' own filters convert capture card formats exactly like they do for AutoSplit. OBS
// Virtual Camera delivers NV12, converted with OpenCV's math.
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WebcamCapture : ICaptureSource
{
    // videoInput::mediaSubtypes: the order OpenCV tries subtypes in when setting a size.
    private static readonly Guid[] MediaSubtypes =
    [
        DirectShow.MEDIASUBTYPE_RGB24,
        new("E436EB7E-524F-11CE-9F53-0020AF0BA770"), // RGB32
        new("E436EB7C-524F-11CE-9F53-0020AF0BA770"), // RGB555
        new("E436EB7B-524F-11CE-9F53-0020AF0BA770"), // RGB565
        DirectShow.FourCC("YUY2"),
        DirectShow.FourCC("YVYU"),
        DirectShow.FourCC("YUYV"),
        DirectShow.FourCC("IYUV"),
        DirectShow.FourCC("UYVY"),
        DirectShow.FourCC("YV12"),
        DirectShow.FourCC("YVU9"),
        DirectShow.FourCC("Y411"),
        DirectShow.FourCC("Y41P"),
        DirectShow.FourCC("Y211"),
        DirectShow.FourCC("AYUV"),
        DirectShow.FourCC("MJPG"),
        DirectShow.FourCC("Y800"),
        DirectShow.FourCC("Y8  "),
        DirectShow.FourCC("GREY"),
        DirectShow.FourCC("I420"),
        DirectShow.FourCC("BY8 "),
        DirectShow.FourCC("Y16 "),
        DirectShow.MEDIASUBTYPE_NV12,
    ];

    private static readonly Guid[] TwoBytesPerPixel =
        [DirectShow.FourCC("YUY2"), DirectShow.FourCC("YVYU"), DirectShow.FourCC("UYVY")];

    // Devices OpenCV forces to NV12, because they accept any format but only really send NV12.
    private static readonly string[] Nv12Devices = ["OBS Virtual Camera", "Streamlabs Desktop Virtual Webcam"];

    private readonly CamDeviceInfo _device;
    private readonly LatestFrame _frames = new();
    private readonly ManualResetEventSlim _stop = new();

    private Thread? _thread;
    private Task? _stopTask;
    private bool _nv12;

    // COM objects of the running graph, released in reverse order by the capture thread
    private readonly List<object> _com = [];
    private IMediaControl? _control;

    public WebcamCapture(CamDeviceInfo device)
    {
        _device = device;
        DisplayName = $"Webcam: {device.Name}";
    }

    public string DisplayName { get; }
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
                using var apartment = DirectShow.ComApartment.Enter();
                var monikers = DirectShow.Monikers();
                var list = new List<CamDeviceInfo>(monikers.Count);
                for (int i = 0; i < monikers.Count; i++)
                {
                    list.Add(new CamDeviceInfo { Name = monikers[i].Name, Index = i });
                    DirectShow.Release(monikers[i].Moniker);
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
        // Opening a DirectShow device blocks for several seconds when it is already in
        // use by another program (e.g. a capture card held by OBS). The graph lives on
        // a dedicated thread that owns its COM apartment, so the UI thread stays responsive
        // and no thread pool worker is COM-tainted (see EnumerateDevicesAsync).
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(tcs))
        {
            IsBackground = true,
            Name = "Webcam Capture",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        return tcs.Task;
    }

    private void Run(TaskCompletionSource started)
    {
        using var apartment = DirectShow.ComApartment.Enter();
        try
        {
            Open();
            _control!.Run();
        }
        catch (Exception ex)
        {
            ReleaseGraph();
            started.SetException(ex as InvalidOperationException ?? OpenError(
                $"{ex.Message} It may be in use by another program. "
                + "If OBS is using it, select OBS Virtual Camera instead."));
            return;
        }
        started.SetResult();

        _stop.Wait();
        try
        {
            _control.Stop();
        }
        catch (Exception ex)
        {
            Utils.LogError($"DirectShow stop failed: {ex}");
        }
        // The graph has to go before its COM apartment
        ReleaseGraph();
    }

    private InvalidOperationException OpenError(string reason) =>
        new($"Could not open '{_device.Name}': {reason}");

    private T Track<T>(T com) where T : class
    {
        _com.Add(com);
        return com;
    }

    private void ReleaseGraph()
    {
        for (int i = _com.Count - 1; i >= 0; i--)
        {
            DirectShow.Release(_com[i]);
        }
        _com.Clear();
        _control = null;
    }

    // The device with the picked device's name, preferring its index when several share the
    // name. Devices can come and go between listing and opening.
    private IMoniker? FindMoniker()
    {
        var monikers = DirectShow.Monikers();
        IMoniker? found = null;
        for (int i = 0; i < monikers.Count; i++)
        {
            if (monikers[i].Name == _device.Name && (found is null || i == _device.Index))
            {
                found = monikers[i].Moniker;
            }
        }
        foreach (var (moniker, _) in monikers)
        {
            if (moniker != found)
            {
                DirectShow.Release(moniker);
            }
        }
        return found;
    }

    // videoInput::start with AutoSplit's settings: the capture size is the device's current
    // format size (AutoSplit sets it from the format DirectShow reports), trying OpenCV's
    // subtypes in order, at the device's current frame rate.
    private void Open()
    {
        var moniker = Track(FindMoniker() ?? throw OpenError("the device is no longer connected."));

        var builder = Track(DirectShow.Create<ICaptureGraphBuilder2>(DirectShow.CLSID_CaptureGraphBuilder2));
        var graph = Track(DirectShow.Create<IGraphBuilder>(DirectShow.CLSID_FilterGraph));
        builder.SetFiltergraph(graph);
        _control = (IMediaControl)graph;

        moniker.BindToObject(0, 0, DirectShow.IID_IBaseFilter, out var source);
        Track(source);
        graph.AddFilter(source, _device.Name);

        if (builder.FindInterface(DirectShow.PIN_CATEGORY_PREVIEW, DirectShow.MEDIATYPE_Video, source,
                DirectShow.IID_IAMStreamConfig, out var config) < 0
            && builder.FindInterface(DirectShow.PIN_CATEGORY_CAPTURE, DirectShow.MEDIATYPE_Video, source,
                DirectShow.IID_IAMStreamConfig, out config) < 0)
        {
            throw OpenError("the device has no configurable video output.");
        }
        Track(config!);

        Marshal.ThrowExceptionForHR(config!.GetFormat(out var mediaType));
        try
        {
            var current = Info(mediaType)->Header;
            var (width, height) = FirstOutputSize(source) ?? (current.Width, current.Height);

            (int, int)? chosen = null;
            if (Nv12Devices.Contains(_device.Name)
                && SetSizeAndSubtype(config, ref mediaType, width, height, DirectShow.MEDIASUBTYPE_NV12))
            {
                chosen = (width, height);
            }
            if (chosen is null)
            {
                foreach (var subtype in MediaSubtypes)
                {
                    if (SetSizeAndSubtype(config, ref mediaType, width, height, subtype))
                    {
                        chosen = (width, height);
                        break;
                    }
                }
            }
            if (chosen is null
                && ClosestSizeAndSubtype(config, width, height) is var (closestW, closestH, closestSubtype)
                && SetSizeAndSubtype(config, ref mediaType, closestW, closestH, closestSubtype))
            {
                chosen = (closestW, closestH);
            }
            (width, height) = chosen ?? (width, height);
            _nv12 = mediaType->Subtype == DirectShow.MEDIASUBTYPE_NV12;
            SourceWidth = Math.Max(width, 0);
            SourceHeight = Math.Abs(height);
        }
        finally
        {
            DirectShow.FreeMediaType(mediaType);
        }

        var grabberFilter = Track(DirectShow.Create<IBaseFilter>(DirectShow.CLSID_SampleGrabber));
        graph.AddFilter(grabberFilter, "Sample Grabber");
        var grabber = (ISampleGrabber)grabberFilter;
        grabber.SetOneShot(0);
        grabber.SetBufferSamples(0);
        // 1: BufferCB, handed the sample's bytes
        grabber.SetCallback(new GrabberCallback(this), 1);

        // NV12 stays NV12 (OpenCV converts it), everything else becomes RGB24 through
        // Windows' own conversion filters
        var grabberType = new AmMediaType
        {
            MajorType = DirectShow.MEDIATYPE_Video,
            Subtype = _nv12 ? DirectShow.MEDIASUBTYPE_NV12 : DirectShow.MEDIASUBTYPE_RGB24,
            FormatType = DirectShow.FORMAT_VideoInfo,
        };
        grabber.SetMediaType(&grabberType);

        var renderer = Track(DirectShow.Create<IBaseFilter>(DirectShow.CLSID_NullRenderer));
        graph.AddFilter(renderer, "NullRenderer");
        builder.RenderStream(DirectShow.PIN_CATEGORY_PREVIEW, DirectShow.MEDIATYPE_Video, source, grabberFilter, renderer);

        // Deliver frames as soon as they arrive instead of waiting on a reference clock
        try
        {
            ((IMediaFilter)graph).SetSyncSource(0);
        }
        catch (Exception)
        {
        }
    }

    private static VideoInfoHeader* Info(AmMediaType* mediaType) => (VideoInfoHeader*)mediaType->Format;

    // setSizeAndSubtype: changes size and subtype on the current media type and applies it.
    // Like OpenCV, the bitmap header's compression and bit count are left as they were.
    private static bool SetSizeAndSubtype(IAMStreamConfig config, ref AmMediaType* mediaType, int width, int height, Guid subtype)
    {
        if (config.GetFormat(out var previous) < 0)
        {
            return false;
        }
        var info = Info(mediaType);
        info->Header.Width = width;
        info->Header.Height = height;
        info->Source = new Rect { Right = width, Bottom = height };
        info->Target = new Rect { Right = width, Bottom = height };
        mediaType->FormatType = DirectShow.FORMAT_VideoInfo;
        mediaType->MajorType = DirectShow.MEDIATYPE_Video;
        mediaType->Subtype = subtype;
        uint pixels = (uint)(width * height);
        mediaType->SampleSize = subtype == DirectShow.MEDIASUBTYPE_RGB24 ? pixels * 3
            : TwoBytesPerPixel.Contains(subtype) ? pixels * 2
            : 0;
        if (config.SetFormat(mediaType) >= 0)
        {
            DirectShow.FreeMediaType(previous);
            return true;
        }
        config.SetFormat(previous);
        DirectShow.FreeMediaType(mediaType);
        mediaType = previous;
        return false;
    }

    // findClosestSizeAndSubtype: the stream capability whose sizes come closest.
    private static (int Width, int Height, Guid Subtype)? ClosestSizeAndSubtype(IAMStreamConfig config, int width, int height)
    {
        if (config.GetNumberOfCapabilities(out int count, out int size) < 0
            || size != sizeof(VideoStreamConfigCaps))
        {
            return null;
        }

        static (int Best, bool Exact) Closest(int target, int min, int max, int step)
        {
            int best = 999_999;
            bool exact = false;
            for (int value = min; value <= max; value += step)
            {
                if (value == target)
                {
                    exact = true;
                    best = value;
                }
                else if (Math.Abs(target - value) < Math.Abs(target - best))
                {
                    best = value;
                }
            }
            return (best, exact);
        }

        int nearW = 9_999_999;
        int nearH = 9_999_999;
        (int, int, Guid)? found = null;
        for (int index = 0; index < count; index++)
        {
            VideoStreamConfigCaps caps;
            if (config.GetStreamCaps(index, out var configType, &caps) < 0)
            {
                continue;
            }
            var subtype = configType->Subtype;
            DirectShow.FreeMediaType(configType);
            if (caps.OutputGranularityX < 1 || caps.OutputGranularityY < 1)
            {
                continue;
            }

            var (tempW, exactX) = Closest(width, caps.MinOutputSize.Cx, caps.MaxOutputSize.Cx, caps.OutputGranularityX);
            var (tempH, exactY) = Closest(height, caps.MinOutputSize.Cy, caps.MaxOutputSize.Cy, caps.OutputGranularityY);
            if (exactX && exactY)
            {
                return (width, height, subtype);
            }
            if (Math.Abs(width - tempW) + Math.Abs(height - tempH) < Math.Abs(width - nearW) + Math.Abs(height - nearH))
            {
                nearW = tempW;
                nearH = tempH;
                found = (nearW, nearH, subtype);
            }
        }
        return found;
    }

    // The current size of the device's first output pin, which is where AutoSplit (through
    // pygrabber's get_current_format) reads the size it asks OpenCV for.
    private static (int Width, int Height)? FirstOutputSize(IBaseFilter source)
    {
        source.EnumPins(out var pins);
        try
        {
            while (pins.Next(1, out var pin, out _) == DirectShow.S_OK && pin is not null)
            {
                try
                {
                    if (pin.QueryDirection(out int direction) < 0)
                    {
                        return null;
                    }
                    if (direction != DirectShow.PINDIR_OUTPUT)
                    {
                        continue;
                    }
                    if (pin is not IAMStreamConfig config)
                    {
                        return null;
                    }
                    if (config.GetFormat(out var format) < 0)
                    {
                        return null;
                    }
                    var header = Info(format)->Header;
                    DirectShow.FreeMediaType(format);
                    return (header.Width, header.Height);
                }
                finally
                {
                    DirectShow.Release(pin);
                }
            }
            return null;
        }
        finally
        {
            DirectShow.Release(pins);
        }
    }

    // Runs on DirectShow's streaming thread. OpenCV drops samples of any other size.
    private void Receive(nint buffer, int length)
    {
        int width = SourceWidth;
        int height = SourceHeight;
        if (buffer == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        if (_nv12)
        {
            if (length != width * height * 3 / 2)
            {
                return;
            }
            var layout = new FrameLayout(V4L2PixelFormat.Nv12, width, height, width);
            if (PixelConversion.ToBgra(new ReadOnlySpan<byte>((void*)buffer, length), layout) is { } nv12Frame)
            {
                _frames.Publish(nv12Frame);
            }
            return;
        }

        if (length != width * height * 3)
        {
            return;
        }

        // RGB24 is B, G, R in DIB order, bottom row first
        var frame = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        byte* targetPixels = (byte*)frame.GetPixels();
        for (int y = 0; y < height; y++)
        {
            byte* sourceRow = (byte*)buffer + (long)(height - 1 - y) * width * 3;
            byte* targetRow = targetPixels + (long)y * width * 4;
            for (int x = 0; x < width; x++)
            {
                targetRow[0] = sourceRow[0];
                targetRow[1] = sourceRow[1];
                targetRow[2] = sourceRow[2];
                targetRow[3] = 255;
                sourceRow += 3;
                targetRow += 4;
            }
        }
        _frames.Publish(frame);
    }

    [GeneratedComClass]
    private sealed partial class GrabberCallback(WebcamCapture capture) : ISampleGrabberCB
    {
        public int SampleCB(double sampleTime, nint sample) => DirectShow.E_NOTIMPL;

        public int BufferCB(double sampleTime, nint buffer, int length)
        {
            try
            {
                capture.Receive(buffer, length);
            }
            catch (Exception ex)
            {
                // An exception can't unwind into DirectShow's streaming thread
                Utils.LogError($"DirectShow frame failed: {ex}");
            }
            return DirectShow.S_OK;
        }
    }

    public bool TryGrabFrame(out SKBitmap? frame) => _frames.TryTake(out frame);

    // Stopping the graph waits for its streaming thread, so the join runs off the caller's (UI)
    // thread. Stopping again, as DisposeAsync does, returns the same task.
    public Task StopAsync() => _stopTask ??= Task.Run(() =>
    {
        _stop.Set();
        _thread?.Join();
        _thread = null;
    });

    // LatestFrame drops frames that arrive after it's disposed, so it needn't wait for the stop
    public ValueTask DisposeAsync()
    {
        Task stop = StopAsync();
        _frames.Dispose();
        return new ValueTask(stop);
    }
}
