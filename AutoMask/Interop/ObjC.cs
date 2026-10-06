using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AutoSplit_AutoMask.Interop;

// Minimal Objective-C runtime, CoreMedia and CoreVideo interop for AVFoundation capture.
// objc_msgSend is called through function pointers cast to each method's exact signature,
// which keeps it AOT-safe. BOOL is passed and returned as a byte: it is bool on arm64 and
// signed char on x64, one byte on both.
[SupportedOSPlatform("macos")]
internal static unsafe partial class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private const string CoreMedia = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";
    private const string CoreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
    public const string AVFoundation = "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";

    public const uint kCVPixelFormatType_32BGRA = 0x42475241;
    public const ulong kCVPixelBufferLock_ReadOnly = 1;

    private static readonly nint MsgSend = NativeLibrary.GetExport(NativeLibrary.Load(LibObjC), "objc_msgSend");

    [StructLayout(LayoutKind.Sequential)]
    public struct VideoDimensions
    {
        public int Width;
        public int Height;
    }

    [LibraryImport(LibObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetClass(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_getProtocol", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetProtocol(string name);

    [LibraryImport(LibObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint Sel(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_allocateClassPair", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint AllocateClassPair(nint superclass, string name, nuint extraBytes);

    [LibraryImport(LibObjC, EntryPoint = "objc_registerClassPair")]
    public static partial void RegisterClassPair(nint cls);

    [LibraryImport(LibObjC, EntryPoint = "class_addMethod", StringMarshalling = StringMarshalling.Utf8)]
    public static partial byte AddMethod(nint cls, nint selector, nint implementation, string types);

    [LibraryImport(LibObjC, EntryPoint = "class_addProtocol")]
    public static partial byte AddProtocol(nint cls, nint protocol);

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPush")]
    public static partial nint AutoreleasePoolPush();

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPop")]
    public static partial void AutoreleasePoolPop(nint pool);

    [LibraryImport(LibSystem, EntryPoint = "dispatch_queue_create", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint DispatchQueueCreate(string label, nint attributes);

    [LibraryImport(LibSystem, EntryPoint = "dispatch_release")]
    public static partial void DispatchRelease(nint obj);

    [LibraryImport(CoreMedia, EntryPoint = "CMSampleBufferGetImageBuffer")]
    public static partial nint CMSampleBufferGetImageBuffer(nint sampleBuffer);

    [LibraryImport(CoreMedia, EntryPoint = "CMVideoFormatDescriptionGetDimensions")]
    public static partial VideoDimensions CMVideoFormatDescriptionGetDimensions(nint description);

    [LibraryImport(CoreVideo, EntryPoint = "CVPixelBufferLockBaseAddress")]
    public static partial int CVPixelBufferLockBaseAddress(nint pixelBuffer, ulong flags);

    [LibraryImport(CoreVideo, EntryPoint = "CVPixelBufferUnlockBaseAddress")]
    public static partial int CVPixelBufferUnlockBaseAddress(nint pixelBuffer, ulong flags);

    [LibraryImport(CoreVideo, EntryPoint = "CVPixelBufferGetBaseAddress")]
    public static partial nint CVPixelBufferGetBaseAddress(nint pixelBuffer);

    [LibraryImport(CoreVideo, EntryPoint = "CVPixelBufferGetBytesPerRow")]
    public static partial nuint CVPixelBufferGetBytesPerRow(nint pixelBuffer);

    [LibraryImport(CoreVideo, EntryPoint = "CVPixelBufferGetWidth")]
    public static partial nuint CVPixelBufferGetWidth(nint pixelBuffer);

    [LibraryImport(CoreVideo, EntryPoint = "CVPixelBufferGetHeight")]
    public static partial nuint CVPixelBufferGetHeight(nint pixelBuffer);

    // The value of a global constant like AVMediaTypeVideo, which is an NSString pointer.
    public static nint GlobalConstant(string library, string symbol) =>
        *(nint*)NativeLibrary.GetExport(NativeLibrary.Load(library), symbol);

    public static nint Send(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, nint>)MsgSend)(receiver, Sel(selector));

    public static nint Send(nint receiver, string selector, nint arg) =>
        ((delegate* unmanaged<nint, nint, nint, nint>)MsgSend)(receiver, Sel(selector), arg);

    public static nint Send(nint receiver, string selector, nint arg1, nint arg2) =>
        ((delegate* unmanaged<nint, nint, nint, nint, nint>)MsgSend)(receiver, Sel(selector), arg1, arg2);

    public static nint SendUInt(nint receiver, string selector, uint arg) =>
        ((delegate* unmanaged<nint, nint, uint, nint>)MsgSend)(receiver, Sel(selector), arg);

    public static nint SendIndex(nint receiver, string selector, nuint index) =>
        ((delegate* unmanaged<nint, nint, nuint, nint>)MsgSend)(receiver, Sel(selector), index);

    public static nuint SendNUInt(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, nuint>)MsgSend)(receiver, Sel(selector));

    public static bool SendBool(nint receiver, string selector, nint arg) =>
        ((delegate* unmanaged<nint, nint, nint, byte>)MsgSend)(receiver, Sel(selector), arg) != 0;

    public static void SendVoid(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, void>)MsgSend)(receiver, Sel(selector));

    public static void SendVoid(nint receiver, string selector, nint arg) =>
        ((delegate* unmanaged<nint, nint, nint, void>)MsgSend)(receiver, Sel(selector), arg);

    public static void SendVoid(nint receiver, string selector, nint arg1, nint arg2) =>
        ((delegate* unmanaged<nint, nint, nint, nint, void>)MsgSend)(receiver, Sel(selector), arg1, arg2);

    public static void SendVoidBool(nint receiver, string selector, bool arg) =>
        ((delegate* unmanaged<nint, nint, byte, void>)MsgSend)(receiver, Sel(selector), arg ? (byte)1 : (byte)0);

    public static nint SendWithError(nint receiver, string selector, nint arg, out nint error)
    {
        nint err = 0;
        nint result = ((delegate* unmanaged<nint, nint, nint, nint*, nint>)MsgSend)(receiver, Sel(selector), arg, &err);
        error = err;
        return result;
    }

    public static nint NewString(string value)
    {
        nint utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(GetClass("NSString"), "stringWithUTF8String:", utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    public static string? ToManagedString(nint nsString) =>
        nsString == 0 ? null : Marshal.PtrToStringUTF8(Send(nsString, "UTF8String"));

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public nint Isa;
        public int Flags;
        public int Reserved;
        public nint Invoke;
        public BlockDescriptor* Descriptor;
    }

    private const int BLOCK_IS_GLOBAL = 1 << 28;

    // A global block (one that captures nothing) calling invoke. Global blocks are never
    // copied or freed by the runtime, so it lives for the rest of the process.
    public static nint NewGlobalBlock(nint invoke)
    {
        var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
        descriptor->Size = (nuint)sizeof(BlockLiteral);

        var block = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)sizeof(BlockLiteral));
        block->Isa = NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "_NSConcreteGlobalBlock");
        block->Flags = BLOCK_IS_GLOBAL;
        block->Invoke = invoke;
        block->Descriptor = descriptor;
        return (nint)block;
    }
}
