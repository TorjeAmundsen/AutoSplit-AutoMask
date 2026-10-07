using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AutoSplit_AutoMask.Interop;
using SkiaSharp;

namespace AutoSplit_AutoMask.Capture;

// AVFoundation capture, ported from AutoSplitRewrite's autosplit-capture/src/avfoundation.rs.
// AutoSplit doesn't run on macOS, so there's nothing to match: frames come from the device's
// active format, converted to BGRA by macOS.
[SupportedOSPlatform("macos")]
public sealed unsafe class AvFoundationCapture : ICaptureSource
{
    private const long AVAuthorizationStatusNotDetermined = 0;
    private const long AVAuthorizationStatusAuthorized = 3;

    private const string DeniedMessage =
        "Camera access is denied. Allow AutoMask in System Settings > Privacy & Security > Camera, "
        + "then quit and reopen it.";

    // Delegate objects by pointer, since the frame callback only gets the Objective-C object.
    private static readonly ConcurrentDictionary<nint, AvFoundationCapture> Delegates = new();
    private static readonly Lazy<nint> DelegateClass = new(RegisterDelegateClass);
    private static readonly object AccessGate = new();
    private static TaskCompletionSource<bool>? _accessRequest;
    private static nint _accessBlock;

    private readonly CamDeviceInfo _device;
    private readonly LatestFrame _frames = new();

    private nint _session;
    private nint _output;
    private nint _delegate;
    private nint _queue;
    private Task? _stopTask;

    public AvFoundationCapture(CamDeviceInfo device)
    {
        _device = device;
        DisplayName = $"Webcam: {device.Name}";
    }

    public string DisplayName { get; }
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }

    private static nint MediaTypeVideo => ObjC.GlobalConstant(ObjC.AVFoundation, "AVMediaTypeVideo");

    // Retained, autoreleased device objects; only valid inside the caller's autorelease pool.
    private static List<(nint Device, string Name)> VideoDevices()
    {
        // devicesWithMediaType: is deprecated but, unlike discovery sessions, lists every kind
        // of camera without naming the device types up front (as the rewrite does)
        nint devices = ObjC.Send(ObjC.GetClass("AVCaptureDevice"), "devicesWithMediaType:", MediaTypeVideo);
        nuint count = ObjC.SendNUInt(devices, "count");
        var list = new List<(nint, string)>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            nint device = ObjC.SendIndex(devices, "objectAtIndex:", i);
            list.Add((device, ObjC.ToManagedString(ObjC.Send(device, "localizedName")) ?? $"Camera {i}"));
        }
        return list;
    }

    public static Task<List<CamDeviceInfo>> EnumerateDevicesAsync()
    {
        return Task.Run(() =>
        {
            NativeLibrary.Load(ObjC.AVFoundation);
            nint pool = ObjC.AutoreleasePoolPush();
            try
            {
                return VideoDevices().Select((d, i) => new CamDeviceInfo { Name = d.Name, Index = i }).ToList();
            }
            finally
            {
                ObjC.AutoreleasePoolPop(pool);
            }
        });
    }

    // Asks for camera access the first time; macOS shows its permission prompt.
    private static void EnsureAccess()
    {
        nint deviceClass = ObjC.GetClass("AVCaptureDevice");
        long status = ObjC.Send(deviceClass, "authorizationStatusForMediaType:", MediaTypeVideo);
        if (status == AVAuthorizationStatusAuthorized)
        {
            return;
        }
        if (status != AVAuthorizationStatusNotDetermined)
        {
            throw new InvalidOperationException(DeniedMessage);
        }

        Task<bool> granted;
        lock (AccessGate)
        {
            // Read from a local: an answer on this thread would already have cleared the field
            var request = _accessRequest;
            if (request is null)
            {
                request = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _accessRequest = request;
                if (_accessBlock == 0)
                {
                    _accessBlock = ObjC.NewGlobalBlock((nint)(delegate* unmanaged<nint, byte, void>)&OnAccessAnswered);
                }
                ObjC.SendVoid(deviceClass, "requestAccessForMediaType:completionHandler:", MediaTypeVideo, _accessBlock);
            }
            granted = request.Task;
        }

        if (!granted.GetAwaiter().GetResult())
        {
            throw new InvalidOperationException(DeniedMessage);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnAccessAnswered(nint block, byte granted)
    {
        TaskCompletionSource<bool>? request;
        lock (AccessGate)
        {
            request = _accessRequest;
            _accessRequest = null;
        }
        request?.TrySetResult(granted != 0);
    }

    private static nint RegisterDelegateClass()
    {
        nint cls = ObjC.AllocateClassPair(ObjC.GetClass("NSObject"), "AutoMaskCaptureDelegate", 0);
        ObjC.AddMethod(
            cls,
            ObjC.Sel("captureOutput:didOutputSampleBuffer:fromConnection:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&OnSampleBuffer,
            "v@:@@@");
        nint protocol = ObjC.GetProtocol("AVCaptureVideoDataOutputSampleBufferDelegate");
        if (protocol != 0)
        {
            ObjC.AddProtocol(cls, protocol);
        }
        ObjC.RegisterClassPair(cls);
        return cls;
    }

    // Runs on the capture's dispatch queue.
    [UnmanagedCallersOnly]
    private static void OnSampleBuffer(nint self, nint selector, nint output, nint sampleBuffer, nint connection)
    {
        try
        {
            if (Delegates.TryGetValue(self, out var capture))
            {
                capture.Receive(sampleBuffer);
            }
        }
        catch (Exception ex)
        {
            // An exception can't unwind into the dispatch queue
            Utils.LogError($"AVFoundation frame failed: {ex}");
        }
    }

    private void Receive(nint sampleBuffer)
    {
        nint pixelBuffer = ObjC.CMSampleBufferGetImageBuffer(sampleBuffer);
        if (pixelBuffer == 0)
        {
            return;
        }

        ObjC.CVPixelBufferLockBaseAddress(pixelBuffer, ObjC.kCVPixelBufferLock_ReadOnly);
        try
        {
            int width = (int)ObjC.CVPixelBufferGetWidth(pixelBuffer);
            int height = (int)ObjC.CVPixelBufferGetHeight(pixelBuffer);
            int stride = (int)ObjC.CVPixelBufferGetBytesPerRow(pixelBuffer);
            nint baseAddress = ObjC.CVPixelBufferGetBaseAddress(pixelBuffer);
            if (baseAddress == 0)
            {
                return;
            }

            var data = new ReadOnlySpan<byte>((void*)baseAddress, stride * height);
            if (PixelConversion.ToBgra(data, new FrameLayout(V4L2PixelFormat.Bgrx, width, height, stride)) is { } frame)
            {
                _frames.Publish(frame);
            }
        }
        finally
        {
            ObjC.CVPixelBufferUnlockBaseAddress(pixelBuffer, ObjC.kCVPixelBufferLock_ReadOnly);
        }
    }

    public Task StartAsync(CancellationToken ct)
    {
        // Waiting for the permission prompt and startRunning both block
        return Task.Run(() =>
        {
            NativeLibrary.Load(ObjC.AVFoundation);
            nint pool = ObjC.AutoreleasePoolPush();
            try
            {
                Open();
            }
            catch
            {
                Release();
                throw;
            }
            finally
            {
                ObjC.AutoreleasePoolPop(pool);
            }
        }, ct);
    }

    private InvalidOperationException OpenError(string reason) =>
        new($"Could not open '{_device.Name}': {reason}");

    // The device with the picked device's name, preferring its index when several share the
    // name. Devices can come and go between listing and opening.
    private nint FindDevice()
    {
        var devices = VideoDevices();
        nint fallback = 0;
        for (int i = 0; i < devices.Count; i++)
        {
            if (devices[i].Name != _device.Name)
            {
                continue;
            }
            if (i == _device.Index)
            {
                return devices[i].Device;
            }
            if (fallback == 0)
            {
                fallback = devices[i].Device;
            }
        }
        return fallback;
    }

    private void Open()
    {
        EnsureAccess();

        nint device = FindDevice();
        if (device == 0)
        {
            throw OpenError("the device is no longer connected.");
        }

        nint input = ObjC.SendWithError(ObjC.GetClass("AVCaptureDeviceInput"), "deviceInputWithDevice:error:", device, out nint error);
        if (input == 0)
        {
            string reason = ObjC.ToManagedString(ObjC.Send(error, "localizedDescription")) ?? "unknown error";
            throw OpenError($"{reason} It may be in use by another program.");
        }

        _session = ObjC.Send(ObjC.Send(ObjC.GetClass("AVCaptureSession"), "alloc"), "init");
        // Keep the device's current format instead of a preset's (often 720p)
        nint inputPriority = ObjC.GlobalConstant(ObjC.AVFoundation, "AVCaptureSessionPresetInputPriority");
        if (ObjC.SendBool(_session, "canSetSessionPreset:", inputPriority))
        {
            ObjC.SendVoid(_session, "setSessionPreset:", inputPriority);
        }
        if (!ObjC.SendBool(_session, "canAddInput:", input))
        {
            throw OpenError("the device can't be used for capture.");
        }
        ObjC.SendVoid(_session, "addInput:", input);

        _output = ObjC.Send(ObjC.Send(ObjC.GetClass("AVCaptureVideoDataOutput"), "alloc"), "init");
        nint formatKey = ObjC.GlobalConstant("/System/Library/Frameworks/CoreVideo.framework/CoreVideo", "kCVPixelBufferPixelFormatTypeKey");
        nint bgra = ObjC.SendUInt(ObjC.GetClass("NSNumber"), "numberWithUnsignedInt:", ObjC.kCVPixelFormatType_32BGRA);
        nint settings = ObjC.Send(ObjC.GetClass("NSDictionary"), "dictionaryWithObject:forKey:", bgra, formatKey);
        ObjC.SendVoid(_output, "setVideoSettings:", settings);
        ObjC.SendVoidBool(_output, "setAlwaysDiscardsLateVideoFrames:", true);

        _delegate = ObjC.Send(ObjC.Send(DelegateClass.Value, "alloc"), "init");
        Delegates[_delegate] = this;
        _queue = ObjC.DispatchQueueCreate("automask.capture", 0);
        ObjC.SendVoid(_output, "setSampleBufferDelegate:queue:", _delegate, _queue);
        if (!ObjC.SendBool(_session, "canAddOutput:", _output))
        {
            throw OpenError("the device's video output can't be read.");
        }
        ObjC.SendVoid(_session, "addOutput:", _output);

        var dimensions = ObjC.CMVideoFormatDescriptionGetDimensions(
            ObjC.Send(ObjC.Send(device, "activeFormat"), "formatDescription"));
        SourceWidth = dimensions.Width;
        SourceHeight = dimensions.Height;

        ObjC.SendVoid(_session, "startRunning");
    }

    public bool TryGrabFrame(out SKBitmap? frame) => _frames.TryTake(out frame);

    // stopRunning blocks until the session has stopped, so it runs off the caller's (UI)
    // thread. Stopping again, as DisposeAsync does, returns the same task.
    public Task StopAsync() => _stopTask ??= Task.Run(() =>
    {
        nint pool = ObjC.AutoreleasePoolPush();
        Release();
        ObjC.AutoreleasePoolPop(pool);
    });

    private void Release()
    {
        if (_delegate != 0)
        {
            Delegates.TryRemove(_delegate, out _);
        }
        if (_output != 0)
        {
            ObjC.SendVoid(_output, "setSampleBufferDelegate:queue:", 0, 0);
        }
        if (_session != 0)
        {
            ObjC.SendVoid(_session, "stopRunning");
            ObjC.SendVoid(_session, "release");
            _session = 0;
        }
        if (_output != 0)
        {
            ObjC.SendVoid(_output, "release");
            _output = 0;
        }
        if (_delegate != 0)
        {
            ObjC.SendVoid(_delegate, "release");
            _delegate = 0;
        }
        if (_queue != 0)
        {
            ObjC.DispatchRelease(_queue);
            _queue = 0;
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
