using SkiaSharp;

namespace AutoSplit_AutoMask.Capture;

internal enum V4L2PixelFormat
{
    Bgr24,
    Rgb24,
    // B, G, R and an unused byte
    Bgrx,
    Gray,
    Nv12,
    Nv21,
    I420,
    Yv12,
    Yuyv,
    Uyvy,
    Mjpeg,
}

internal readonly record struct FrameLayout(V4L2PixelFormat Format, int Width, int Height, int Stride);

// Converts V4L2 frames to BGRA the way OpenCV's V4L2 backend does for AutoSplit on Linux, except
// that rows are read with their stride (OpenCV ignores bytesperline and assumes packed rows) and
// BGR24 becomes a whole image (OpenCV copies it into a one-row Mat).
// Ported from AutoSplitRewrite's autosplit-capture/src/convert.rs. The YUV paths are OpenCV
// 4.11's cvtColor COLOR_YUV2BGR_* math: BT.601 limited range in 20-bit fixed point.
internal static unsafe class PixelConversion
{
    private const int Shift = 20;
    private const int Cy = 1_220_542;
    private const int Cub = 2_116_026;
    private const int Cug = -409_993;
    private const int Cvg = -852_492;
    private const int Cvr = 1_673_527;
    private const int Half = 1 << (Shift - 1);

    public static int BytesPerPixel(V4L2PixelFormat format) => format switch
    {
        V4L2PixelFormat.Bgr24 or V4L2PixelFormat.Rgb24 => 3,
        V4L2PixelFormat.Bgrx => 4,
        V4L2PixelFormat.Gray or V4L2PixelFormat.Nv12 or V4L2PixelFormat.Nv21
            or V4L2PixelFormat.I420 or V4L2PixelFormat.Yv12 => 1,
        V4L2PixelFormat.Yuyv or V4L2PixelFormat.Uyvy => 2,
        _ => 0,
    };

    private static long FrameLength(FrameLayout layout)
    {
        var (format, width, height, stride) = layout;
        return format switch
        {
            V4L2PixelFormat.Nv12 or V4L2PixelFormat.Nv21 =>
                (long)stride * height + BytesNeeded(height / 2, stride, width),
            V4L2PixelFormat.I420 or V4L2PixelFormat.Yv12 =>
                (long)stride * height + (long)(stride / 2) * (height / 2) + BytesNeeded(height / 2, stride / 2, width / 2),
            _ => BytesNeeded(height, stride, width * BytesPerPixel(format)),
        };
    }

    // The last row needs only its pixels, not a whole stride.
    private static long BytesNeeded(int rows, int stride, int rowBytes) =>
        rows == 0 ? 0 : (long)(rows - 1) * stride + rowBytes;

    // Null when the frame is too short or has a size its format can't have.
    public static SKBitmap? ToBgra(ReadOnlySpan<byte> src, FrameLayout layout)
    {
        if (layout.Format == V4L2PixelFormat.Mjpeg)
        {
            return DecodeJpeg(src);
        }

        var (format, width, height, stride) = layout;
        bool evenWidth = format is V4L2PixelFormat.Nv12 or V4L2PixelFormat.Nv21 or V4L2PixelFormat.I420
            or V4L2PixelFormat.Yv12 or V4L2PixelFormat.Yuyv or V4L2PixelFormat.Uyvy;
        bool evenHeight = format is V4L2PixelFormat.Nv12 or V4L2PixelFormat.Nv21 or V4L2PixelFormat.I420
            or V4L2PixelFormat.Yv12;
        if (width <= 0 || height <= 0
            || (evenWidth && width % 2 != 0)
            || (evenHeight && height % 2 != 0)
            || stride < width * BytesPerPixel(format)
            || src.Length < FrameLength(layout))
        {
            return null;
        }

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        byte* dst = (byte*)bitmap.GetPixels();
        fixed (byte* s = src)
        {
            switch (format)
            {
                case V4L2PixelFormat.Bgr24:
                    Rgb24(s, layout, dst, swap: false);
                    break;
                case V4L2PixelFormat.Rgb24:
                    Rgb24(s, layout, dst, swap: true);
                    break;
                case V4L2PixelFormat.Bgrx:
                    Bgrx(s, layout, dst);
                    break;
                case V4L2PixelFormat.Gray:
                    Gray(s, layout, dst);
                    break;
                case V4L2PixelFormat.Nv12:
                    Yuv420Interleaved(s, layout, dst, swap: false);
                    break;
                case V4L2PixelFormat.Nv21:
                    Yuv420Interleaved(s, layout, dst, swap: true);
                    break;
                case V4L2PixelFormat.I420:
                    Yuv420Planar(s, layout, dst, swap: false);
                    break;
                case V4L2PixelFormat.Yv12:
                    Yuv420Planar(s, layout, dst, swap: true);
                    break;
                case V4L2PixelFormat.Yuyv:
                    Yuv422(s, layout, dst, uyvy: false);
                    break;
                case V4L2PixelFormat.Uyvy:
                    Yuv422(s, layout, dst, uyvy: true);
                    break;
            }
        }
        return bitmap;
    }

    // OpenCV decodes with libjpeg-turbo's defaults (accurate integer DCT, smooth chroma
    // upsampling), which are also Skia's. No color space, so Skia doesn't convert colors.
    private static SKBitmap? DecodeJpeg(ReadOnlySpan<byte> src)
    {
        using var data = SKData.CreateCopy(src);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            bitmap.Dispose();
            return null;
        }
        return bitmap;
    }

    // uvToRGBuv then yRGBuvToRGBA, written as BGRA.
    private static void WritePixel(byte* dst, int y, int u, int v)
    {
        u -= 128;
        v -= 128;
        int r = Half + Cvr * v;
        int g = Half + Cvg * v + Cug * u;
        int b = Half + Cub * u;
        int luma = Math.Max(y - 16, 0) * Cy;
        dst[0] = Clamp((luma + b) >> Shift);
        dst[1] = Clamp((luma + g) >> Shift);
        dst[2] = Clamp((luma + r) >> Shift);
        dst[3] = 255;
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    private static void Yuv420Interleaved(byte* src, FrameLayout layout, byte* dst, bool swap)
    {
        var (_, width, height, stride) = layout;
        byte* chroma = src + (long)stride * height;
        for (int y = 0; y < height; y++)
        {
            byte* luma = src + (long)y * stride;
            byte* uv = chroma + (long)(y / 2) * stride;
            byte* output = dst + (long)y * width * 4;
            for (int x = 0; x < width; x += 2)
            {
                int u = swap ? uv[x + 1] : uv[x];
                int v = swap ? uv[x] : uv[x + 1];
                WritePixel(output + x * 4, luma[x], u, v);
                WritePixel(output + (x + 1) * 4, luma[x + 1], u, v);
            }
        }
    }

    private static void Yuv420Planar(byte* src, FrameLayout layout, byte* dst, bool swap)
    {
        var (_, width, height, stride) = layout;
        byte* chroma = src + (long)stride * height;
        long planeLength = (long)(stride / 2) * (height / 2);
        for (int y = 0; y < height; y++)
        {
            byte* luma = src + (long)y * stride;
            byte* first = chroma + (long)(y / 2) * (stride / 2);
            byte* second = first + planeLength;
            byte* output = dst + (long)y * width * 4;
            for (int x = 0; x < width; x += 2)
            {
                int u = swap ? second[x / 2] : first[x / 2];
                int v = swap ? first[x / 2] : second[x / 2];
                WritePixel(output + x * 4, luma[x], u, v);
                WritePixel(output + (x + 1) * 4, luma[x + 1], u, v);
            }
        }
    }

    private static void Yuv422(byte* src, FrameLayout layout, byte* dst, bool uyvy)
    {
        var (_, width, height, stride) = layout;
        for (int y = 0; y < height; y++)
        {
            byte* row = src + (long)y * stride;
            byte* output = dst + (long)y * width * 4;
            for (int x = 0; x < width; x += 2)
            {
                byte* group = row + x * 2;
                if (uyvy)
                {
                    WritePixel(output + x * 4, group[1], group[0], group[2]);
                    WritePixel(output + (x + 1) * 4, group[3], group[0], group[2]);
                }
                else
                {
                    WritePixel(output + x * 4, group[0], group[1], group[3]);
                    WritePixel(output + (x + 1) * 4, group[2], group[1], group[3]);
                }
            }
        }
    }

    private static void Rgb24(byte* src, FrameLayout layout, byte* dst, bool swap)
    {
        var (_, width, height, stride) = layout;
        for (int y = 0; y < height; y++)
        {
            byte* row = src + (long)y * stride;
            byte* output = dst + (long)y * width * 4;
            for (int x = 0; x < width; x++)
            {
                output[0] = swap ? row[2] : row[0];
                output[1] = row[1];
                output[2] = swap ? row[0] : row[2];
                output[3] = 255;
                row += 3;
                output += 4;
            }
        }
    }

    private static void Bgrx(byte* src, FrameLayout layout, byte* dst)
    {
        var (_, width, height, stride) = layout;
        for (int y = 0; y < height; y++)
        {
            byte* row = src + (long)y * stride;
            byte* output = dst + (long)y * width * 4;
            for (int x = 0; x < width; x++)
            {
                output[0] = row[0];
                output[1] = row[1];
                output[2] = row[2];
                output[3] = 255;
                row += 4;
                output += 4;
            }
        }
    }

    // The way COLOR_GRAY2BGR does it.
    private static void Gray(byte* src, FrameLayout layout, byte* dst)
    {
        var (_, width, height, stride) = layout;
        for (int y = 0; y < height; y++)
        {
            byte* row = src + (long)y * stride;
            byte* output = dst + (long)y * width * 4;
            for (int x = 0; x < width; x++)
            {
                byte value = row[x];
                output[0] = value;
                output[1] = value;
                output[2] = value;
                output[3] = 255;
                output += 4;
            }
        }
    }
}
