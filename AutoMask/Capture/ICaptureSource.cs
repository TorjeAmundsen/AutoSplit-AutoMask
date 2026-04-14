using SkiaSharp;

namespace AutoSplit_AutoMask.Capture;

public interface ICaptureSource : IAsyncDisposable
{
    string DisplayName { get; }

    // Native capture size (before any crop).  For a window this is client rect; for region
    // it's the region rect; for a webcam it's the camera frame.  Used to default the crop
    // spinboxes + clamp their values.
    int SourceWidth { get; }
    int SourceHeight { get; }

    Task StartAsync(CancellationToken ct);

    // Returns false if no new frame is ready yet (webcam buffer empty) or capture failed.
    // The returned SKBitmap is owned by the source and must NOT be disposed by the caller.
    // Valid only until the next TryGrabFrame call.
    bool TryGrabFrame(out SKBitmap? frame);

    Task StopAsync();
}
