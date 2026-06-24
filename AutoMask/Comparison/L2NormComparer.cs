using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

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
        int n = refMask.Length;
        if (n == 0)
        {
            return 0.0;
        }

        long sumSq;
        int maskCount;

        // The AVX2 fast path consumes 8 mask bytes / 32 pixel bytes per iteration. Capture
        // sizes (320×240 = 76800 mask bytes, divisible by 8) hit this exclusively in practice.
        if (Avx2.IsSupported && Ssse3.IsSupported && n >= 8
            && refPixels.Length >= n * 4 && livePixels.Length >= n * 4)
        {
            (sumSq, maskCount) = CompareAvx2(refPixels, refMask, livePixels);
        }
        else
        {
            (sumSq, maskCount) = CompareScalar(refPixels, refMask, livePixels, 0, n);
        }

        if (maskCount == 0)
        {
            return 0.0;
        }

        double error = Math.Sqrt((double)sumSq);
        double maxError = Math.Sqrt(maskCount * 3.0 * 255.0 * 255.0);
        return 1.0 - error / maxError;
    }

    private static (long sumSq, int maskCount) CompareScalar(
        byte[] refPixels, byte[] refMask, byte[] livePixels, int start, int end)
    {
        long sumSq = 0;
        int maskCount = 0;
        for (int i = start, px = start * 4; i < end; i++, px += 4)
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
        return (sumSq, maskCount);
    }

    private static unsafe (long sumSq, int maskCount) CompareAvx2(
        byte[] refPixels, byte[] refMask, byte[] livePixels)
    {
        int n = refMask.Length;
        int simdEnd = n - (n % 8);

        // PSHUFB index vectors: replicate each of 8 source mask bytes by 4 to cover all 4
        // BGRA bytes of its pixel. PSHUFB operates inside each 128-bit lane, so the source
        // half is duplicated below into both halves of a 128-bit register.
        var lowExpand = Vector128.Create(
            (byte)0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3);
        var highExpand = Vector128.Create(
            (byte)4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7);
        // Zero alpha bytes so the L2 sum stays BGR-only, matching the scalar formula even
        // when ref alpha differs from live alpha in masked-out regions.
        var alphaKill = Vector256.Create(
            (byte)0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0x00,
            0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0x00,
            0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0x00,
            0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0x00);

        Vector256<long> sumVec = Vector256<long>.Zero;
        int maskCount = 0;

        fixed (byte* refP = refPixels, mP = refMask, livP = livePixels)
        {
            for (int i = 0; i < simdEnd; i += 8)
            {
                ulong m8 = *(ulong*)(mP + i);
                // Mask bytes are exactly 0x00 or 0xFF, so 8 active bytes contribute 64 bits.
                maskCount += BitOperations.PopCount(m8) >> 3;

                // Broadcast 8 mask bytes -> 32 bytes (one Vector256 covering 8 BGRA pixels).
                var maskV128 = Vector128.Create(m8, m8).AsByte();
                var lowLane = Ssse3.Shuffle(maskV128, lowExpand);
                var highLane = Ssse3.Shuffle(maskV128, highExpand);
                var pixelActive = Vector256.Create(lowLane, highLane);

                var laneMask = Avx2.And(pixelActive, alphaKill);

                int px = i * 4;
                var refV = Avx.LoadVector256(refP + px);
                var livV = Avx.LoadVector256(livP + px);

                // |refV - livV| via two saturating subtractions OR'd together.
                var sub1 = Avx2.SubtractSaturate(refV, livV);
                var sub2 = Avx2.SubtractSaturate(livV, refV);
                var diffAbs = Avx2.Or(sub1, sub2);

                var diffMasked = Avx2.And(diffAbs, laneMask);

                // Widen byte -> ushort (per 128-bit lane), then PMADDWD to square-and-sum
                // adjacent pairs into 32-bit lanes.
                var lo = Avx2.UnpackLow(diffMasked, Vector256<byte>.Zero).AsInt16();
                var hi = Avx2.UnpackHigh(diffMasked, Vector256<byte>.Zero).AsInt16();
                var sqLo = Avx2.MultiplyAddAdjacent(lo, lo);
                var sqHi = Avx2.MultiplyAddAdjacent(hi, hi);
                var perChunk = Avx2.Add(sqLo, sqHi);

                // Promote int -> long every iteration; an int accumulator overflows around
                // ~1300 chunks (76800-pixel comparisons run 9600).
                sumVec = Avx2.Add(sumVec, Avx2.ConvertToVector256Int64(perChunk.GetLower()));
                sumVec = Avx2.Add(sumVec, Avx2.ConvertToVector256Int64(perChunk.GetUpper()));
            }
        }

        long sumSq = Vector256.Sum(sumVec);

        if (simdEnd < n)
        {
            var (tailSum, tailCount) = CompareScalar(refPixels, refMask, livePixels, simdEnd, n);
            sumSq += tailSum;
            maskCount += tailCount;
        }

        return (sumSq, maskCount);
    }
}
