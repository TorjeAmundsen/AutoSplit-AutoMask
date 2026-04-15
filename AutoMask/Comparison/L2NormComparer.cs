namespace AutoSplit_AutoMask.Comparison;

public static class L2NormComparer
{
    // All three arrays are stride-4 BGRA (SkiaSharp default).  refMask has one byte per pixel:
    // 255 where the reference alpha was >= 1, else 0.  Mirrors compare_l2_norm in AutoSplit:
    //   error   = sqrt(sum of squared per-channel deltas, over mask != 0)
    //   maxErr  = sqrt(maskCount * 3 * 255 * 255)
    //   similar = 1 - error / maxErr
    // Comparison is symmetric under channel permutation, so BGRA and RGBA produce identical results.
    public static double Compare(byte[] refPixels, byte[] refMask, byte[] livePixels)
    {
        if (refMask.Length == 0)
        {
            return 0.0;
        }

        long sumSq = 0;
        int maskCount = 0;

        for (int i = 0, px = 0; i < refMask.Length; i++, px += 4)
        {
            if (refMask[i] == 0)
            {
                continue;
            }

            maskCount++;
            int d0 = refPixels[px]     - livePixels[px];
            int d1 = refPixels[px + 1] - livePixels[px + 1];
            int d2 = refPixels[px + 2] - livePixels[px + 2];
            sumSq += d0 * d0 + d1 * d1 + d2 * d2;
        }

        if (maskCount == 0)
        {
            return 0.0;
        }

        double error = Math.Sqrt((double)sumSq);
        double maxError = Math.Sqrt(maskCount * 3.0 * 255.0 * 255.0);
        return 1.0 - error / maxError;
    }
}
