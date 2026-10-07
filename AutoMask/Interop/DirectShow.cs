using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace AutoSplit_AutoMask.Interop;

// DirectShow interop for WebcamCapture, through source-generated COM (AOT-safe). Interfaces
// only declare methods up to the last one used; unused ones are placeholders that keep the
// vtable slots in order and are never called.
[SupportedOSPlatform("windows")]
internal static unsafe partial class DirectShow
{
    public static readonly Guid CLSID_SystemDeviceEnum = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    public static readonly Guid CLSID_VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
    public static readonly Guid CLSID_CaptureGraphBuilder2 = new("BF87B6E1-8C27-11d0-B3F0-00AA003761C5");
    public static readonly Guid CLSID_FilterGraph = new("E436EBB3-524F-11CE-9F53-0020AF0BA770");
    // qedit.h, no longer in the Windows SDK but qedit.dll still ships with Windows
    public static readonly Guid CLSID_SampleGrabber = new("C1F400A0-3F08-11d3-9F0B-006008039E37");
    public static readonly Guid CLSID_NullRenderer = new("C1F400A4-3F08-11d3-9F0B-006008039E37");
    public static readonly Guid PIN_CATEGORY_CAPTURE = new("FB6C4281-0353-11d1-905F-0000C0CC16BA");
    public static readonly Guid PIN_CATEGORY_PREVIEW = new("FB6C4282-0353-11d1-905F-0000C0CC16BA");
    public static readonly Guid MEDIATYPE_Video = FourCC("vids");
    public static readonly Guid FORMAT_VideoInfo = new("05589F80-C356-11CE-BF01-00AA0055595A");
    public static readonly Guid MEDIASUBTYPE_RGB24 = new("E436EB7D-524F-11CE-9F53-0020AF0BA770");
    public static readonly Guid MEDIASUBTYPE_NV12 = FourCC("NV12");

    public static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    public static readonly Guid IID_IBaseFilter = new("56A86895-0AD4-11CE-B03A-0020AF0BA770");
    public static readonly Guid IID_IPropertyBag = new("55272A00-42CB-11CE-8135-00AA004BB851");
    public static readonly Guid IID_IAMStreamConfig = new("C6E13340-30AC-11d0-A18C-00A0C9118956");

    public const int PINDIR_OUTPUT = 1;
    public const int S_OK = 0;
    public const int E_NOTIMPL = unchecked((int)0x80004001);

    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const ushort VT_BSTR = 8;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const uint COINIT_MULTITHREADED = 0x0;

    // The GUID DirectShow uses for a FOURCC media subtype.
    public static Guid FourCC(string code) =>
        new((uint)(code[0] | code[1] << 8 | code[2] << 16 | code[3] << 24),
            0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("oleaut32.dll")]
    private static partial int VariantClear(Variant* var);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Variant
    {
        public ushort Vt;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Value;
        public nint Padding;
    }

    // Joins the current thread's COM apartment (the one its ApartmentState names) and leaves it
    // on Dispose. COM objects created inside must be released before that.
    public readonly struct ComApartment : IDisposable
    {
        private readonly bool _initialized;

        private ComApartment(bool initialized) => _initialized = initialized;

        public static ComApartment Enter()
        {
            uint apartment = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA
                ? COINIT_APARTMENTTHREADED
                : COINIT_MULTITHREADED;
            // S_FALSE (already initialized) still has to be balanced
            return new ComApartment(CoInitializeEx(0, apartment) >= 0);
        }

        public void Dispose()
        {
            if (_initialized)
            {
                CoUninitialize();
            }
        }
    }

    public static T Create<T>(Guid clsid) where T : class
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, 0, CLSCTX_INPROC_SERVER, IID_IUnknown, out nint unknown));
        try
        {
            return UniqueComInterfaceMarshaller<T>.ConvertToManaged((void*)unknown)!;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    // Releases a COM object now instead of when the GC finalizes it. The capture device stays
    // busy until its filter is released. Only works on unique wrappers, which is why every
    // interface this file hands out uses UniqueComInterfaceMarshaller.
    public static void Release(object? com)
    {
        if (com is ComObject comObject)
        {
            comObject.FinalRelease();
        }
    }

    // DeleteMediaType
    public static void FreeMediaType(AmMediaType* mediaType)
    {
        if (mediaType == null)
        {
            return;
        }
        if (mediaType->FormatSize != 0)
        {
            Marshal.FreeCoTaskMem(mediaType->Format);
        }
        if (mediaType->Unknown != 0)
        {
            Marshal.Release(mediaType->Unknown);
        }
        Marshal.FreeCoTaskMem((nint)mediaType);
    }

    // The video input devices OpenCV can open, in its order: monikers whose property bag can't
    // be read are skipped, like videoInput::listDevices does. The caller releases the monikers.
    public static List<(IMoniker Moniker, string Name)> Monikers()
    {
        var devices = new List<(IMoniker, string)>();
        var deviceEnum = Create<ICreateDevEnum>(CLSID_SystemDeviceEnum);
        IEnumMoniker? enumMoniker = null;
        try
        {
            // S_FALSE means there are no devices, and leaves the enumerator null
            Marshal.ThrowExceptionForHR(deviceEnum.CreateClassEnumerator(CLSID_VideoInputDeviceCategory, out enumMoniker, 0));
            if (enumMoniker is null)
            {
                return devices;
            }

            while (enumMoniker.Next(1, out var moniker, out _) == S_OK && moniker is not null)
            {
                if (moniker.BindToStorage(0, 0, IID_IPropertyBag, out var bag) < 0 || bag is null)
                {
                    Release(moniker);
                    continue;
                }
                devices.Add((moniker, ReadFriendlyName(bag) ?? ""));
                Release(bag);
            }
            return devices;
        }
        finally
        {
            Release(enumMoniker);
            Release(deviceEnum);
        }
    }

    private static string? ReadFriendlyName(IPropertyBag bag)
    {
        Variant value = default;
        string? name = null;
        if (bag.Read("FriendlyName", &value, 0) >= 0 && value.Vt == VT_BSTR && value.Value != 0)
        {
            name = Marshal.PtrToStringBSTR(value.Value);
        }
        VariantClear(&value);
        return name;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct AmMediaType
{
    public Guid MajorType;
    public Guid Subtype;
    public int FixedSizeSamples;
    public int TemporalCompression;
    public uint SampleSize;
    public Guid FormatType;
    public nint Unknown;
    public uint FormatSize;
    public nint Format;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SizeL
{
    public int Cx;
    public int Cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfoHeader
{
    public uint Size;
    public int Width;
    public int Height;
    public ushort Planes;
    public ushort BitCount;
    public uint Compression;
    public uint SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public uint ClrUsed;
    public uint ClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VideoInfoHeader
{
    public Rect Source;
    public Rect Target;
    public uint BitRate;
    public uint BitErrorRate;
    public long AvgTimePerFrame;
    public BitmapInfoHeader Header;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VideoStreamConfigCaps
{
    public Guid Guid;
    public uint VideoStandard;
    public SizeL InputSize;
    public SizeL MinCroppingSize;
    public SizeL MaxCroppingSize;
    public int CropGranularityX;
    public int CropGranularityY;
    public int CropAlignX;
    public int CropAlignY;
    public SizeL MinOutputSize;
    public SizeL MaxOutputSize;
    public int OutputGranularityX;
    public int OutputGranularityY;
    public int StretchTapsX;
    public int StretchTapsY;
    public int ShrinkTapsX;
    public int ShrinkTapsY;
    public long MinFrameInterval;
    public long MaxFrameInterval;
    public int MinBitsPerSecond;
    public int MaxBitsPerSecond;
}

[GeneratedComInterface]
[Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
internal partial interface ICreateDevEnum
{
    [PreserveSig]
    int CreateClassEnumerator(in Guid deviceClass, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IEnumMoniker>))] out IEnumMoniker? enumMoniker, int flags);
}

[GeneratedComInterface]
[Guid("00000102-0000-0000-C000-000000000046")]
internal partial interface IEnumMoniker
{
    [PreserveSig]
    int Next(int count, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IMoniker>))] out IMoniker? moniker, out int fetched);
}

[GeneratedComInterface]
[Guid("0000000F-0000-0000-C000-000000000046")]
internal partial interface IMoniker
{
    // IPersist and IPersistStream placeholders
    void GetClassID();
    void IsDirty();
    void Load();
    void Save();
    void GetSizeMax();

    void BindToObject(nint bindContext, nint toLeft, in Guid iid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IBaseFilter>))] out IBaseFilter filter);

    [PreserveSig]
    int BindToStorage(nint bindContext, nint toLeft, in Guid iid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IPropertyBag>))] out IPropertyBag? bag);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
internal unsafe partial interface IPropertyBag
{
    [PreserveSig]
    int Read(string name, DirectShow.Variant* value, nint errorLog);
}

[GeneratedComInterface]
[Guid("56A86899-0AD4-11CE-B03A-0020AF0BA770")]
internal partial interface IMediaFilter
{
    // IPersist placeholder
    void GetClassID();

    void Stop();
    void Pause();
    void Run(long start);
    void GetState(int timeoutMs, out int state);
    void SetSyncSource(nint clock);
    void GetSyncSource(out nint clock);
}

[GeneratedComInterface]
[Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770")]
internal partial interface IBaseFilter : IMediaFilter
{
    void EnumPins([MarshalUsing(typeof(UniqueComInterfaceMarshaller<IEnumPins>))] out IEnumPins pins);
}

[GeneratedComInterface]
[Guid("56A86892-0AD4-11CE-B03A-0020AF0BA770")]
internal partial interface IEnumPins
{
    [PreserveSig]
    int Next(int count, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IPin>))] out IPin? pin, out int fetched);
}

[GeneratedComInterface]
[Guid("56A86891-0AD4-11CE-B03A-0020AF0BA770")]
internal partial interface IPin
{
    // Placeholders
    void Connect();
    void ReceiveConnection();
    void Disconnect();
    void ConnectedTo();
    void ConnectionMediaType();
    void QueryPinInfo();

    [PreserveSig]
    int QueryDirection(out int direction);
}

[GeneratedComInterface]
[Guid("C6E13340-30AC-11d0-A18C-00A0C9118956")]
internal unsafe partial interface IAMStreamConfig
{
    [PreserveSig]
    int SetFormat(AmMediaType* mediaType);

    [PreserveSig]
    int GetFormat(out AmMediaType* mediaType);

    [PreserveSig]
    int GetNumberOfCapabilities(out int count, out int size);

    [PreserveSig]
    int GetStreamCaps(int index, out AmMediaType* mediaType, VideoStreamConfigCaps* caps);
}

// Only AddFilter, the first IFilterGraph method, is used
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("56A868A9-0AD4-11CE-B03A-0020AF0BA770")]
internal partial interface IGraphBuilder
{
    void AddFilter(IBaseFilter filter, string name);
}

[GeneratedComInterface]
[Guid("93E5A4E0-2D50-11d2-ABFA-00A0C9C6E38D")]
internal partial interface ICaptureGraphBuilder2
{
    void SetFiltergraph(IGraphBuilder graph);

    // Placeholders
    void GetFiltergraph();
    void SetOutputFileName();

    [PreserveSig]
    int FindInterface(in Guid category, in Guid type, IBaseFilter filter, in Guid iid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IAMStreamConfig>))] out IAMStreamConfig? config);

    void RenderStream(in Guid category, in Guid type, IBaseFilter source, IBaseFilter? compressor, IBaseFilter? renderer);
}

[GeneratedComInterface]
[Guid("56A868B1-0AD4-11CE-B03A-0020AF0BA770")]
internal partial interface IMediaControl
{
    // IDispatch placeholders
    void GetTypeInfoCount();
    void GetTypeInfo();
    void GetIDsOfNames();
    void Invoke();

    void Run();
    void Pause();
    void Stop();
}

[GeneratedComInterface]
[Guid("6B652FFF-11FE-4fce-92AD-0266B5D7C78F")]
internal unsafe partial interface ISampleGrabber
{
    void SetOneShot(int oneShot);
    void SetMediaType(AmMediaType* mediaType);
    void GetConnectedMediaType(AmMediaType* mediaType);
    void SetBufferSamples(int bufferSamples);
    void GetCurrentBuffer(int* size, int* buffer);
    void GetCurrentSample(nint* sample);
    void SetCallback(ISampleGrabberCB callback, int whichMethod);
}

[GeneratedComInterface]
[Guid("0579154A-2B53-4994-B0D0-E773148EFF85")]
internal partial interface ISampleGrabberCB
{
    [PreserveSig]
    int SampleCB(double sampleTime, nint sample);

    [PreserveSig]
    int BufferCB(double sampleTime, nint buffer, int length);
}
