using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace AutoSplit_AutoMask;

public static class ImageProcessor
{
    public static SKBitmap ApplyScaledAlphaChannel(string inputPath, string alphaPath, Dictionary<string, SKBitmap> maskCache)
    {
        using var inputBitmap = SKBitmap.Decode(inputPath);

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

        using var scaledAlpha = alphaBitmap.Resize(
            new SKImageInfo(inputBitmap.Width, inputBitmap.Height),
            new SKSamplingOptions(SKFilterMode.Linear))!;

        int width = inputBitmap.Width;
        int height = inputBitmap.Height;
        var outputBitmap = new SKBitmap(width, height);

        SKColor[] inputPixels = inputBitmap.Pixels;
        SKColor[] alphaPixels = scaledAlpha.Pixels;
        SKColor[] outputPixels = new SKColor[width * height];

        Parallel.For(0, height, y =>
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                var src = inputPixels[i];
                outputPixels[i] = alphaPixels[i].Alpha == 255
                    ? new SKColor(src.Red, src.Green, src.Blue)
                    : SKColors.Transparent;
            }
        });

        outputBitmap.Pixels = outputPixels;

        return outputBitmap;
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
