using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AutoSplit_AutoMask.Interop;

// Minimal DirectShow interop for enumerating video capture device friendly names.
// OpenCV's DSHOW backend opens devices by integer index in the same order this
// enumerator walks, so pairing name[i] ↔ VideoCapture(i, DSHOW) is reliable.
// Manual vtable dispatch keeps us AOT-safe (no [ComImport]).
[SupportedOSPlatform("windows")]
internal static unsafe partial class DirectShow
{
    public static readonly Guid CLSID_SystemDeviceEnum =
        new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    public static readonly Guid IID_ICreateDevEnum =
        new("29840822-5B84-11D0-BD3B-00A0C911CE86");
    public static readonly Guid CLSID_VideoInputDeviceCategory =
        new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
    public static readonly Guid IID_IPropertyBag =
        new("55272A00-42CB-11CE-8135-00AA004BB851");

    public const uint CLSCTX_INPROC_SERVER = 0x1;
    public const ushort VT_BSTR = 8;

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter,
        uint dwClsContext, in Guid riid, out IntPtr ppv);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [LibraryImport("oleaut32.dll")]
    private static partial int VariantClear(ref VARIANT var);

    [StructLayout(LayoutKind.Sequential)]
    private struct VARIANT
    {
        public ushort vt;
        public ushort r1;
        public ushort r2;
        public ushort r3;
        public IntPtr val;
        public IntPtr pad;
    }

    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const uint COINIT_MULTITHREADED = 0x0;

    [ThreadStatic]
    private static bool _comInited;

    public static void EnsureComInitialized()
    {
        if (_comInited)
        {
            return;
        }

        uint apartment = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA
            ? COINIT_APARTMENTTHREADED
            : COINIT_MULTITHREADED;
        CoInitializeEx(IntPtr.Zero, apartment);
        _comInited = true;
    }

    private static uint IUnknown_Release(IntPtr com)
    {
        if (com == IntPtr.Zero)
        {
            return 0;
        }
        var vtable = *(IntPtr**)com;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint>)vtable[2];
        return fn(com);
    }

    public static List<string> EnumerateVideoCaptureNames()
    {
        EnsureComInitialized();

        var names = new List<string>();
        IntPtr devEnum = IntPtr.Zero;
        IntPtr enumMoniker = IntPtr.Zero;

        int hr = CoCreateInstance(CLSID_SystemDeviceEnum, IntPtr.Zero,
            CLSCTX_INPROC_SERVER, IID_ICreateDevEnum, out devEnum);
        if (hr < 0 || devEnum == IntPtr.Zero)
        {
            return names;
        }

        try
        {
            // ICreateDevEnum::CreateClassEnumerator (slot 3)
            var vtable = *(IntPtr**)devEnum;
            var createClassEnum =
                (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, uint, int>)vtable[3];
            Guid category = CLSID_VideoInputDeviceCategory;
            IntPtr em;
            hr = createClassEnum(devEnum, &category, &em, 0);
            enumMoniker = em;

            // S_FALSE (1) means "no devices"; negative means failure.
            if (hr != 0 || enumMoniker == IntPtr.Zero)
            {
                return names;
            }

            // IEnumMoniker::Next (slot 3) — walk one at a time.
            var emVtable = *(IntPtr**)enumMoniker;
            var next =
                (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, uint*, int>)emVtable[3];

            while (true)
            {
                IntPtr moniker = IntPtr.Zero;
                uint fetched = 0;
                int nh = next(enumMoniker, 1, &moniker, &fetched);
                if (nh != 0 || fetched == 0 || moniker == IntPtr.Zero)
                {
                    break;
                }

                string? name = ReadFriendlyName(moniker);
                IUnknown_Release(moniker);
                names.Add(name ?? $"Camera {names.Count}");
            }
        }
        finally
        {
            if (enumMoniker != IntPtr.Zero)
            {
                IUnknown_Release(enumMoniker);
            }
            IUnknown_Release(devEnum);
        }

        return names;
    }

    private static string? ReadFriendlyName(IntPtr moniker)
    {
        // IMoniker::BindToStorage (slot 9: after IUnknown(3) + IPersist(1) + IPersistStream(4) + BindToObject(1))
        var vtable = *(IntPtr**)moniker;
        var bindToStorage =
            (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, Guid*, IntPtr*, int>)vtable[9];

        IntPtr propBag = IntPtr.Zero;
        Guid iid = IID_IPropertyBag;
        int hr = bindToStorage(moniker, IntPtr.Zero, IntPtr.Zero, &iid, &propBag);
        if (hr < 0 || propBag == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // IPropertyBag::Read (slot 3): Read(LPCOLESTR name, VARIANT* var, IErrorLog* log)
            var pbVtable = *(IntPtr**)propBag;
            var read =
                (delegate* unmanaged[Stdcall]<IntPtr, char*, VARIANT*, IntPtr, int>)pbVtable[3];

            VARIANT v = default;
            string? result = null;
            fixed (char* pName = "FriendlyName")
            {
                int rh = read(propBag, pName, &v, IntPtr.Zero);
                if (rh >= 0 && v.vt == VT_BSTR && v.val != IntPtr.Zero)
                {
                    result = Marshal.PtrToStringBSTR(v.val);
                }
            }
            VariantClear(ref v);
            return result;
        }
        finally
        {
            IUnknown_Release(propBag);
        }
    }
}
