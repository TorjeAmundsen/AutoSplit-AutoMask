using Avalonia.Media.Imaging;
using SkiaSharp;

namespace AutoSplit_AutoMask;

public static class ImageProcessor
{
    public static SKBitmap ApplyScaledAlphaChannel(string inputPath, string alphaPath, Dictionary<string, SKBitmap> maskCache)
    {
        using var inputBitmap = SKBitmap.Decode(inputPath);

        bool ownAlpha = !maskCache.TryGetValue(alphaPath, out var alphaBitmap);
        alphaBitmap ??= SKBitmap.Decode(alphaPath);

        using var scaledAlpha = alphaBitmap!.Resize(
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

        if (ownAlpha)
        {
            alphaBitmap.Dispose();
        }

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
        using var skImage = SKImage.FromBitmap(skBitmap);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        var stream = new MemoryStream();
        encoded.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    public static void SaveBitmapToStream(SKBitmap bitmap, Stream stream)
    {
        using var skImage = SKImage.FromBitmap(bitmap);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        encoded.SaveTo(stream);
    }

    public static void SaveBitmapToPath(SKBitmap bitmap, string path)
    {
        using var stream = File.OpenWrite(path);
        SaveBitmapToStream(bitmap, stream);
    }
}
