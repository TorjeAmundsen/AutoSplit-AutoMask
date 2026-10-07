using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AutoSplit_AutoMask.Interop;
using SkiaSharp;

namespace AutoSplit_AutoMask.Capture;

// V4L2 capture, ported from AutoSplitRewrite's autosplit-capture/src/v4l2.rs. Device indexes
// are the N in /dev/videoN, which is what AutoSplit's cv2.VideoCapture(index) opens on Linux.
// Frames are captured at the device's current format, converted with the math OpenCV's V4L2
// backend uses.
[SupportedOSPlatform("linux")]
public sealed unsafe class V4L2Capture : ICaptureSource
{
    // The formats OpenCV tries, in its order, when the current one isn't usable.
    private static readonly (string FourCC, V4L2PixelFormat Format)[] TriedFormats =
    [
        ("BGR3", V4L2PixelFormat.Bgr24),
        ("RGB3", V4L2PixelFormat.Rgb24),
        ("YV12", V4L2PixelFormat.Yv12),
        ("YU12", V4L2PixelFormat.I420),
        ("YUYV", V4L2PixelFormat.Yuyv),
        ("UYVY", V4L2PixelFormat.Uyvy),
        ("NV12", V4L2PixelFormat.Nv12),
        ("NV21", V4L2PixelFormat.Nv21),
        // B, G, R, X in memory; OpenCV drops the 4th byte
        ("XR24", V4L2PixelFormat.Bgrx),
        ("AR24", V4L2PixelFormat.Bgrx),
        ("MJPG", V4L2PixelFormat.Mjpeg),
        ("JPEG", V4L2PixelFormat.Mjpeg),
    ];

    private const uint BufferCount = 2;

    private readonly CamDeviceInfo _device;
    private readonly LatestFrame _frames = new();

    private int _fd = -1;
    private FrameLayout _layout;
    private readonly List<(nint Address, nuint Length)> _buffers = [];
    private Thread? _thread;
    private volatile bool _stop;
    private Task? _stopTask;

    public V4L2Capture(CamDeviceInfo device)
    {
        _device = device;
        DisplayName = $"Webcam: {device.Name}";
    }

    public string DisplayName { get; }
    public int SourceWidth => _layout.Width;
    public int SourceHeight => _layout.Height;

    private static string DevicePath(int index) => $"/dev/video{index}";

    private static V4L2PixelFormat? PixelFormatOf(uint fourcc)
    {
        if (fourcc == V4L2.FourCC("GREY"))
        {
            return V4L2PixelFormat.Gray;
        }
        foreach (var (code, format) in TriedFormats)
        {
            if (V4L2.FourCC(code) == fourcc)
            {
                return format;
            }
        }
        return null;
    }

    // Metadata nodes, which UVC webcams add next to each camera, can't capture video.
    private static bool CapturesVideo(string path)
    {
        int fd = V4L2.Open(path, V4L2.O_RDWR | V4L2.O_CLOEXEC);
        if (fd < 0)
        {
            // Listed anyway, so opening it explains the problem (like missing permissions)
            return true;
        }
        try
        {
            var capability = default(V4L2.Capability);
            if (V4L2.Ioctl(fd, V4L2.VIDIOC_QUERYCAP, ref capability) != 0)
            {
                return true;
            }
            uint caps = (capability.Capabilities & V4L2.V4L2_CAP_DEVICE_CAPS) != 0
                ? capability.DeviceCaps
                : capability.Capabilities;
            return (caps & V4L2.V4L2_CAP_VIDEO_CAPTURE) != 0;
        }
        finally
        {
            V4L2.Close(fd);
        }
    }

    private static List<CamDeviceInfo> AllNodes()
    {
        // The class only exists once a video device driver is loaded
        var classDir = new DirectoryInfo("/sys/class/video4linux");
        if (!classDir.Exists)
        {
            return [];
        }

        var devices = new List<CamDeviceInfo>();
        foreach (var entry in classDir.EnumerateFileSystemInfos("video*"))
        {
            if (!int.TryParse(entry.Name.AsSpan("video".Length), out int index))
            {
                continue;
            }
            try
            {
                string name = File.ReadAllText(Path.Combine(entry.FullName, "name")).Trim();
                devices.Add(new CamDeviceInfo { Name = name, Index = index });
            }
            catch (IOException)
            {
                // Unplugged while listing
            }
        }
        devices.Sort((a, b) => a.Index.CompareTo(b.Index));
        return devices;
    }

    public static Task<List<CamDeviceInfo>> EnumerateDevicesAsync() =>
        Task.Run(() => AllNodes().Where(device => CapturesVideo(DevicePath(device.Index))).ToList());

    // The node to open: the picked one if it still has the picked name, else another with it.
    private string? CurrentNode()
    {
        var nodes = AllNodes();
        var node = nodes.FirstOrDefault(n => n.Index == _device.Index && n.Name == _device.Name)
            ?? nodes.FirstOrDefault(n => n.Name == _device.Name && CapturesVideo(DevicePath(n.Index)));
        return node is null ? null : DevicePath(node.Index);
    }

    public Task StartAsync(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                Open();
            }
            catch
            {
                Release();
                throw;
            }
            _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "V4L2 Capture" };
            _thread.Start();
        }, ct);
    }

    private InvalidOperationException OpenError(string reason) =>
        new($"Could not open '{_device.Name}': {reason}");

    private void Open()
    {
        string path = CurrentNode() ?? throw OpenError("the device is no longer connected.");
        _fd = V4L2.Open(path, V4L2.O_RDWR | V4L2.O_CLOEXEC);
        if (_fd < 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            throw OpenError($"{V4L2.ErrorText(errno)}. Check that your user is in the 'video' group.");
        }

        var current = new V4L2.Format { Type = V4L2.V4L2_BUF_TYPE_VIDEO_CAPTURE };
        int error = V4L2.Ioctl(_fd, V4L2.VIDIOC_G_FMT, ref current);
        if (error != 0)
        {
            throw OpenError(V4L2.ErrorText(error));
        }

        var candidates = new List<uint> { current.Pix.PixelFormat };
        candidates.AddRange(TriedFormats.Select(f => V4L2.FourCC(f.FourCC)).Where(c => c != current.Pix.PixelFormat));

        string lastError = "no supported pixel format";
        foreach (uint candidate in candidates.Where(c => PixelFormatOf(c) is not null))
        {
            var requested = new V4L2.Format { Type = V4L2.V4L2_BUF_TYPE_VIDEO_CAPTURE };
            requested.Pix.Width = current.Pix.Width;
            requested.Pix.Height = current.Pix.Height;
            requested.Pix.PixelFormat = candidate;
            error = V4L2.Ioctl(_fd, V4L2.VIDIOC_S_FMT, ref requested);
            if (error != 0)
            {
                lastError = V4L2.ErrorText(error);
                continue;
            }

            if (PixelFormatOf(requested.Pix.PixelFormat) is not { } format)
            {
                lastError = "the driver switched to an unsupported pixel format";
                continue;
            }

            int width = (int)requested.Pix.Width;
            int height = (int)requested.Pix.Height;
            int stride = Math.Max((int)requested.Pix.BytesPerLine, width * PixelConversion.BytesPerPixel(format));
            _layout = new FrameLayout(format, width, height, stride);
            StartStreaming();
            return;
        }
        throw OpenError(lastError);
    }

    private void StartStreaming()
    {
        var request = new V4L2.RequestBuffers
        {
            Count = BufferCount,
            Type = V4L2.V4L2_BUF_TYPE_VIDEO_CAPTURE,
            Memory = V4L2.V4L2_MEMORY_MMAP,
        };
        ThrowIfError(V4L2.Ioctl(_fd, V4L2.VIDIOC_REQBUFS, ref request));

        for (uint i = 0; i < request.Count; i++)
        {
            var buffer = new V4L2.Buffer { Index = i, Type = V4L2.V4L2_BUF_TYPE_VIDEO_CAPTURE, Memory = V4L2.V4L2_MEMORY_MMAP };
            ThrowIfError(V4L2.Ioctl(_fd, V4L2.VIDIOC_QUERYBUF, ref buffer));

            nint address = V4L2.Mmap(0, buffer.Length, V4L2.PROT_READ, V4L2.MAP_SHARED, _fd, (nint)buffer.Offset);
            if (address == -1)
            {
                ThrowIfError(Marshal.GetLastPInvokeError());
            }
            _buffers.Add((address, buffer.Length));

            ThrowIfError(V4L2.Ioctl(_fd, V4L2.VIDIOC_QBUF, ref buffer));
        }

        int type = (int)V4L2.V4L2_BUF_TYPE_VIDEO_CAPTURE;
        ThrowIfError(V4L2.Ioctl(_fd, V4L2.VIDIOC_STREAMON, ref type));
    }

    private void ThrowIfError(int errno)
    {
        if (errno != 0)
        {
            throw OpenError(V4L2.ErrorText(errno));
        }
    }

    // Copies each frame out of the driver's buffer and hands the buffer straight back, so a
    // slow conversion (MJPEG decoding on a weak CPU) doesn't hold up the driver.
    private void CaptureLoop()
    {
        byte[] raw = [];
        while (!_stop)
        {
            // Wait with a timeout so stopping doesn't hang on a device that stopped
            var pollFd = new V4L2.PollFd { Fd = _fd, Events = V4L2.POLLIN };
            int ready = V4L2.Poll(&pollFd, 1, 500);
            if (ready < 0 && Marshal.GetLastPInvokeError() == V4L2.EINTR)
            {
                continue;
            }
            if (ready < 0 || (pollFd.Revents & (V4L2.POLLERR | V4L2.POLLHUP | V4L2.POLLNVAL)) != 0)
            {
                return;
            }
            if (ready == 0)
            {
                continue;
            }

            var buffer = new V4L2.Buffer { Type = V4L2.V4L2_BUF_TYPE_VIDEO_CAPTURE, Memory = V4L2.V4L2_MEMORY_MMAP };
            if (V4L2.Ioctl(_fd, V4L2.VIDIOC_DQBUF, ref buffer) != 0)
            {
                return;
            }

            int length = 0;
            if ((buffer.Flags & V4L2.V4L2_BUF_FLAG_ERROR) == 0)
            {
                var (address, mapped) = _buffers[(int)buffer.Index];
                length = (int)Math.Min(buffer.BytesUsed == 0 ? buffer.Length : buffer.BytesUsed, (uint)mapped);
                if (raw.Length < length)
                {
                    raw = new byte[length];
                }
                new ReadOnlySpan<byte>((void*)address, length).CopyTo(raw);
            }

            if (V4L2.Ioctl(_fd, V4L2.VIDIOC_QBUF, ref buffer) != 0)
            {
                return;
            }

            if (length > 0 && PixelConversion.ToBgra(raw.AsSpan(0, length), _layout) is { } frame)
            {
                _frames.Publish(frame);
            }
        }
    }

    public bool TryGrabFrame(out SKBitmap? frame) => _frames.TryTake(out frame);

    // The join can wait out a poll timeout, so it runs off the caller's (UI) thread. Stopping
    // again, as DisposeAsync does, returns the same task.
    public Task StopAsync() => _stopTask ??= Task.Run(() =>
    {
        _stop = true;
        _thread?.Join(1000);
        _thread = null;
        Release();
    });

    private void Release()
    {
        if (_fd >= 0)
        {
            int type = (int)V4L2.V4L2_BUF_TYPE_VIDEO_CAPTURE;
            V4L2.Ioctl(_fd, V4L2.VIDIOC_STREAMOFF, ref type);
        }
        foreach (var (address, length) in _buffers)
        {
            V4L2.Munmap(address, length);
        }
        _buffers.Clear();
        if (_fd >= 0)
        {
            V4L2.Close(_fd);
            _fd = -1;
        }
    }

    // LatestFrame drops frames that arrive after it's disposed, so it needn't wait for the stop
    public ValueTask DisposeAsync()
    {
        Task stop = StopAsync();
        _frames.Dispose();
        return new ValueTask(stop);
    }
}
