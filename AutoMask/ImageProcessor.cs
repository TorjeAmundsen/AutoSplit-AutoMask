using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace AutoSplit_AutoMask;

public static class ImageProcessor
{
    public static SKBitmap ApplyScaledAlphaChannel(string inputPath, string alphaPath, Dictionary<string, SKBitmap> maskCache)
    {
        using var rawInput = SKBitmap.Decode(inputPath);
        // Native blit if the decoder picked a non-BGRA layout (rare for PNG on Windows,
        // possible for JPEG). Cheaper than the three SKColor[] copies the previous version
        // performed and avoids managed allocations of (width*height*4) bytes per call.
        using var inputDisposable = rawInput.ColorType == SKColorType.Bgra8888
            ? null
            : rawInput.Copy(SKColorType.Bgra8888);
        SKBitmap inputBitmap = inputDisposable ?? rawInput;

        SKBitmap alphaBitmap;
        // Decode under lock so concurrent callers (multiple Task.Run consumers) can't both
        // decode the same mask and leak one. Dictionary<,> is not thread-safe to read during
        // a writer either, so the lookup must also be inside the lock.
        lock (maskCache)
        {
            if (!maskCache.TryGetValue(alphaPath, out var cached))
            {
                cached = SKBitmap.Decode(alphaPath);
                maskCache[alphaPath] = cached;
            }
            alphaBitmap = cached;
        }

        // Resizing into an explicit BGRA8888 SKImageInfo guarantees the byte loop's layout.
        using var scaledAlpha = alphaBitmap.Resize(
            new SKImageInfo(inputBitmap.Width, inputBitmap.Height, SKColorType.Bgra8888),
            new SKSamplingOptions(SKFilterMode.Linear))!;

        int width = inputBitmap.Width;
        int height = inputBitmap.Height;
        var outputBitmap = new SKBitmap(
            new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));

        ApplyAlphaThreshold(inputBitmap, scaledAlpha, outputBitmap, width, height);

        return outputBitmap;
    }

    private static unsafe void ApplyAlphaThreshold(SKBitmap input, SKBitmap alpha, SKBitmap output,
        int width, int height)
    {
        IntPtr inPtr = input.GetPixels();
        IntPtr alpPtr = alpha.GetPixels();
        IntPtr outPtr = output.GetPixels();
        int rowIn = input.RowBytes;
        int rowAlp = alpha.RowBytes;
        int rowOut = output.RowBytes;

        Parallel.For(0, height, y =>
        {
            unsafe
            {
                byte* inRow = (byte*)inPtr + y * rowIn;
                byte* alpRow = (byte*)alpPtr + y * rowAlp;
                byte* outRow = (byte*)outPtr + y * rowOut;
                for (int x = 0; x < width; x++)
                {
                    int o = x * 4;
                    if (alpRow[o + 3] == 255)
                    {
                        // Hard threshold: keep input pixel only when scaled mask is fully opaque.
                        // Resize with linear sampling produces partial alphas at edges, which the
                        // original SKColor[] loop also rejected.
                        outRow[o]     = inRow[o];
                        outRow[o + 1] = inRow[o + 1];
                        outRow[o + 2] = inRow[o + 2];
                        outRow[o + 3] = 255;
                    }
                    else
                    {
                        *(uint*)(outRow + o) = 0u;
                    }
                }
            }
        });
    }

    public static Bitmap CreateCheckerBitmap(int width, int height)
    {
        const int tileSize = 8;
        var light = new SKColor(0xBB, 0xBB, 0xBB);
        var dark  = new SKColor(0x88, 0x88, 0x88);
        using var skBitmap = new SKBitmap(width, height);
        var pixels = new SKColor[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = ((x / tileSize + y / tileSize) & 1) == 0 ? light : dark;
            }
        }
        skBitmap.Pixels = pixels;
        return ToAvaloniaBitmap(skBitmap);
    }

    public static Bitmap ToAvaloniaBitmap(SKBitmap skBitmap)
    {
        var info = skBitmap.Info;
        var format = info.ColorType switch
        {
            SKColorType.Bgra8888 => PixelFormat.Bgra8888,
            SKColorType.Rgba8888 => PixelFormat.Rgba8888,
            _ => PixelFormat.Bgra8888,
        };
        var alpha = info.AlphaType == SKAlphaType.Opaque
            ? AlphaFormat.Opaque
            : AlphaFormat.Unpremul;

        if (info.ColorType != SKColorType.Bgra8888 && info.ColorType != SKColorType.Rgba8888)
        {
            using var converted = new SKBitmap(new SKImageInfo(info.Width, info.Height,
                SKColorType.Bgra8888, info.AlphaType));
            skBitmap.CopyTo(converted, SKColorType.Bgra8888);
            return new Bitmap(
                PixelFormat.Bgra8888,
                alpha,
                converted.GetPixels(),
                new PixelSize(converted.Width, converted.Height),
                new Vector(96, 96),
                converted.RowBytes);
        }

        return new Bitmap(
            format,
            alpha,
            skBitmap.GetPixels(),
            new PixelSize(info.Width, info.Height),
            new Vector(96, 96),
            skBitmap.RowBytes);
    }

    public static void SaveBitmapToStream(SKBitmap bitmap, Stream stream)
    {
        using var skImage = SKImage.FromBitmap(bitmap);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        encoded.SaveTo(stream);
    }

    public static void SaveBitmapToPath(SKBitmap bitmap, string path)
    {
        using var stream = File.Create(path);
        SaveBitmapToStream(bitmap, stream);
    }
}
