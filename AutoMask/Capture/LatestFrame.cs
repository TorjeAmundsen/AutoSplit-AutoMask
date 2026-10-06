using SkiaSharp;

namespace AutoSplit_AutoMask.Capture;

// Hands the newest frame from a capture thread to CaptureController's loop. The bitmap last
// handed out stays alive until the next one is taken, per ICaptureSource.TryGrabFrame.
internal sealed class LatestFrame : IDisposable
{
    private readonly object _gate = new();
    private SKBitmap? _latest;
    private SKBitmap? _handedOut;
    private bool _disposed;

    public void Publish(SKBitmap frame)
    {
        SKBitmap? toDispose;
        lock (_gate)
        {
            if (_disposed)
            {
                toDispose = frame;
            }
            else
            {
                toDispose = _latest;
                _latest = frame;
            }
        }
        toDispose?.Dispose();
    }

    public bool TryTake(out SKBitmap? frame)
    {
        SKBitmap? pending;
        SKBitmap? toDispose;

        lock (_gate)
        {
            pending = _latest;
            _latest = null;
            toDispose = pending is null ? null : _handedOut;
            if (pending is not null)
            {
                _handedOut = pending;
            }
        }

        toDispose?.Dispose();

        frame = pending;
        return pending is not null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _latest?.Dispose();
            _latest = null;
            _handedOut?.Dispose();
            _handedOut = null;
        }
    }
}
