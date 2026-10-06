using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AutoSplit_AutoMask.Interop;

// The parts of libc and <linux/videodev2.h> V4L2 capture needs. Struct layouts and ioctl
// numbers are the 64-bit ones, which x64 and arm64 share.
[SupportedOSPlatform("linux")]
internal static unsafe partial class V4L2
{
    public const int O_RDWR = 0x2;
    public const int O_CLOEXEC = 0x80000;
    public const int EINTR = 4;

    public const int PROT_READ = 0x1;
    public const int MAP_SHARED = 0x1;

    public const short POLLIN = 0x1;
    public const short POLLERR = 0x8;
    public const short POLLHUP = 0x10;
    public const short POLLNVAL = 0x20;

    public const uint VIDIOC_QUERYCAP = 0x80685600;
    public const uint VIDIOC_G_FMT = 0xC0D05604;
    public const uint VIDIOC_S_FMT = 0xC0D05605;
    public const uint VIDIOC_REQBUFS = 0xC0145608;
    public const uint VIDIOC_QUERYBUF = 0xC0585609;
    public const uint VIDIOC_QBUF = 0xC058560F;
    public const uint VIDIOC_DQBUF = 0xC0585611;
    public const uint VIDIOC_STREAMON = 0x40045612;
    public const uint VIDIOC_STREAMOFF = 0x40045613;

    public const uint V4L2_CAP_VIDEO_CAPTURE = 0x1;
    public const uint V4L2_CAP_DEVICE_CAPS = 0x80000000;
    public const uint V4L2_BUF_TYPE_VIDEO_CAPTURE = 1;
    public const uint V4L2_MEMORY_MMAP = 1;
    public const uint V4L2_BUF_FLAG_ERROR = 0x40;

    [StructLayout(LayoutKind.Sequential)]
    public struct Capability
    {
        public fixed byte Driver[16];
        public fixed byte Card[32];
        public fixed byte BusInfo[32];
        public uint Version;
        public uint Capabilities;
        public uint DeviceCaps;
        public fixed uint Reserved[3];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PixFormat
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public uint Field;
        public uint BytesPerLine;
        public uint SizeImage;
        public uint Colorspace;
        public uint Priv;
        public uint Flags;
        public uint YcbcrEnc;
        public uint Quantization;
        public uint XferFunc;
    }

    // The format union holds pointers in some of its members, so it starts 8 bytes in.
    [StructLayout(LayoutKind.Explicit, Size = 208)]
    public struct Format
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public PixFormat Pix;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RequestBuffers
    {
        public uint Count;
        public uint Type;
        public uint Memory;
        public uint Capabilities;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 88)]
    public struct Buffer
    {
        [FieldOffset(0)] public uint Index;
        [FieldOffset(4)] public uint Type;
        [FieldOffset(8)] public uint BytesUsed;
        [FieldOffset(12)] public uint Flags;
        [FieldOffset(60)] public uint Memory;
        [FieldOffset(64)] public uint Offset;
        [FieldOffset(72)] public uint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    public static uint FourCC(string code) =>
        code[0] | (uint)code[1] << 8 | (uint)code[2] << 16 | (uint)code[3] << 24;

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    public static partial int Close(int fd);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoctlRaw(int fd, nuint request, void* arg);

    [LibraryImport("libc", EntryPoint = "mmap", SetLastError = true)]
    public static partial nint Mmap(nint addr, nuint length, int prot, int flags, int fd, nint offset);

    [LibraryImport("libc", EntryPoint = "munmap")]
    public static partial int Munmap(nint addr, nuint length);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    public static partial int Poll(PollFd* fds, nuint count, int timeoutMs);

    // Retries when a signal interrupts the call, like libv4l2 does. Returns errno, or 0.
    public static int Ioctl<T>(int fd, uint request, ref T arg) where T : unmanaged
    {
        fixed (T* p = &arg)
        {
            while (true)
            {
                if (IoctlRaw(fd, request, p) != -1)
                {
                    return 0;
                }
                int errno = Marshal.GetLastPInvokeError();
                if (errno != EINTR)
                {
                    return errno;
                }
            }
        }
    }

    public static string ErrorText(int errno) => Marshal.GetPInvokeErrorMessage(errno);
}
