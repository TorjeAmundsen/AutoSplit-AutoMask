using System.Globalization;
using System.Text;
using SkiaSharp;

namespace AutoSplit_AutoMask.Export;

public static class RunLeashExporter
{
    private static readonly string[] CsvHeader =
    [
        "Comment", "2", "3", "Seg.", "Loop", "And", "Key",
        "AdjTable", "AdjTimer", "Seek", "M", "A",
        "方式", "閾値", "PN", "待機時間", "T", "遅延時間", "閾値N",
        "R val", "G val", "B val", "色許容", "Filter", "Akaze",
        "PX", "PY", "SX", "SY",
        "PX(S)", "PY(S)", "SX(S)", "SY(S)",
        "LTX", "LTY", "RBX", "RBY",
    ];

    public static async Task ExportAsync(
        SplitPreset preset,
        IReadOnlyList<string> inputImagePaths,
        string outputFolder)
    {
        if (preset.Splits is null || preset.Splits.Count == 0)
        {
            throw new InvalidOperationException("Preset has no splits.");
        }

        if (preset.Splits.Count != inputImagePaths.Count)
        {
            throw new InvalidOperationException(
                $"Input image count ({inputImagePaths.Count}) must match split count ({preset.Splits.Count}).");
        }

        Directory.CreateDirectory(outputFolder);
        string pictureFolder = Path.Combine(outputFolder, "picture");
        Directory.CreateDirectory(pictureFolder);

        // Pair splits with their input image paths so reorder/filter doesn't lose the mapping.
        var paired = preset.Splits
            .Select((split, idx) => (split, inputPath: inputImagePaths[idx]))
            .ToList();

        var resetEntry = paired.FirstOrDefault(p => p.split.Name == "reset");
        var startEntry = paired.FirstOrDefault(p => p.split.Name == "start_auto_splitter");

        // Output ordering: start_auto_splitter first (if present), then all other non-reset
        // non-start_auto_splitter splits in original order.
        var orderedRows = new List<(Split split, string inputPath)>();

        if (startEntry.split is not null)
        {
            orderedRows.Add(startEntry);
        }

        foreach (var p in paired)
        {
            if (p.split.Name == "reset" || p.split.Name == "start_auto_splitter")
            {
                continue;
            }
            orderedRows.Add(p);
        }

        // col2/col3 sequence: starts at 1, increments after each non-dummy row;
        // a dummy followed by a non-dummy increments after the dummy too.
        // A run of consecutive dummies shares the same value.
        var col2Values = ComputeCol2Sequence(orderedRows.Select(r => r.split.Dummy).ToList());

        var sb = new StringBuilder();
        AppendCsvRow(sb, CsvHeader);

        // Reset row (Seg. = 0). No picture file written for this row.
        AppendResetRow(sb, resetEntry.split, preset.PresetFolder);

        for (int i = 0; i < orderedRows.Count; i++)
        {
            var (split, inputPath) = orderedRows[i];
            int segNumber = i + 1;
            string maskPath = Path.Combine(preset.PresetFolder!, split.Mask);

            (int bboxX, int bboxY, int bboxW, int bboxH, int maskW, int maskH) =
                ComputeMaskBoundingBox(maskPath);

            // Write picture/N.png: input screenshot resized to mask resolution, RGB only.
            string picturePath = Path.Combine(pictureFolder, $"{segNumber}.png");
            WritePictureFile(inputPath, picturePath, maskW, maskH);

            AppendSplitRow(sb, split, segNumber, col2Values[i],
                bboxX, bboxY, bboxW, bboxH, maskW, maskH);
        }

        // Match example file: CRLF line endings, UTF-8 with BOM (Japanese tool compatibility).
        string csvPath = Path.Combine(outputFolder, "table.csv");
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(csvPath, sb.ToString(), encoding);
    }

    private static List<int> ComputeCol2Sequence(List<bool> isDummy)
    {
        var result = new List<int>(isDummy.Count);
        int counter = 1;
        for (int i = 0; i < isDummy.Count; i++)
        {
            result.Add(counter);
            bool nextExistsAndDummy = i + 1 < isDummy.Count && isDummy[i + 1];
            // Increment unless this is a dummy followed by another dummy (chain shares value)
            if (!isDummy[i] || !nextExistsAndDummy)
            {
                counter++;
            }
        }
        return result;
    }

    private static (int X, int Y, int W, int H, int MaskW, int MaskH) ComputeMaskBoundingBox(string maskPath)
    {
        using var mask = SKBitmap.Decode(maskPath)
                         ?? throw new IOException($"Failed to decode mask: {maskPath}");

        int w = mask.Width;
        int h = mask.Height;
        var pixels = mask.Pixels;

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                if (pixels[row + x].Alpha != 0)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        // Fully transparent or fully opaque-without-alpha: fall back to full image.
        if (maxX < 0)
        {
            return (0, 0, w, h, w, h);
        }

        return (minX, minY, maxX - minX + 1, maxY - minY + 1, w, h);
    }

    private static void WritePictureFile(string inputPath, string outputPath, int targetW, int targetH)
    {
        using var input = SKBitmap.Decode(inputPath)
                          ?? throw new IOException($"Failed to decode input image: {inputPath}");

        SKBitmap toEncode;
        bool ownToEncode = false;

        if (input.Width != targetW || input.Height != targetH)
        {
            toEncode = input.Resize(
                new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Opaque),
                new SKSamplingOptions(SKFilterMode.Linear))
                ?? throw new IOException($"Failed to resize input image: {inputPath}");
            ownToEncode = true;
        }
        else
        {
            toEncode = input;
        }

        try
        {
            using var skImage = SKImage.FromBitmap(toEncode);
            using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(outputPath);
            encoded.SaveTo(stream);
        }
        finally
        {
            if (ownToEncode)
            {
                toEncode.Dispose();
            }
        }
    }

    private static void AppendResetRow(StringBuilder sb, Split? resetSplit, string? presetFolder)
    {
        // Defaults pulled from the OoT MST example's Reset row.
        int threshold = 94;
        int pauseTime = 1;
        int px = 19, py = 43, sx = 450, sy = 311;
        int pxS = 199, pyS = 167, sxS = 90, syS = 63;
        int maskW = 0, maskH = 0;

        if (resetSplit is not null && presetFolder is not null
            && !string.IsNullOrEmpty(resetSplit.Mask))
        {
            string maskPath = Path.Combine(presetFolder, resetSplit.Mask);
            if (File.Exists(maskPath))
            {
                var bbox = ComputeMaskBoundingBox(maskPath);
                px = bbox.X; py = bbox.Y; sx = bbox.W; sy = bbox.H;
                maskW = bbox.MaskW; maskH = bbox.MaskH;
                pxS = Math.Max(0, (maskW - 40) / 2);
                pyS = Math.Max(0, (maskH - 40) / 2);
                sxS = Math.Min(40, maskW);
                syS = Math.Min(40, maskH);
            }

            threshold = (int)Math.Round(resetSplit.Threshold * 100);
            pauseTime = (int)Math.Round(resetSplit.PauseTime);
        }

        AppendCsvRow(sb,
        [
            "Reset", "Reset", "Reset",
            "0",            // Seg.
            "1",            // Loop
            "0",            // And
            "0",            // Key (Reset key code)
            "0",            // AdjTable
            "0",            // AdjTimer
            "0",            // Seek (matches example's Reset row)
            "0",            // M
            "0",            // A
            "8",            // 方式 (L2 Norm)
            Inv(threshold),
            "p",            // PN
            Inv(pauseTime),
            "0",            // T
            "0",            // 遅延時間
            "90",           // 閾値N
            "0", "0", "0",  // R/G/B
            "10",           // 色許容
            "none",         // Filter
            "300",          // Akaze
            Inv(px), Inv(py), Inv(sx), Inv(sy),
            Inv(pxS), Inv(pyS), Inv(sxS), Inv(syS),
            "-999999", "-999999", "999999", "999999",
        ]);
    }

    private static void AppendSplitRow(
        StringBuilder sb, Split split, int seg, int col2,
        int px, int py, int sx, int sy, int maskW, int maskH)
    {
        int threshold = (int)Math.Round(split.Threshold * 100);
        string pn = split.Inverted ? "n" : "p";
        int pauseTime = (int)Math.Round(split.PauseTime);
        int t = split.Delay > 0 ? 1 : 0;
        int delayMs = (int)split.Delay;
        int key = split.Dummy ? -1 : 0;

        int pxS = Math.Max(0, (maskW - 40) / 2);
        int pyS = Math.Max(0, (maskH - 40) / 2);
        int sxS = Math.Min(40, maskW);
        int syS = Math.Min(40, maskH);

        AppendCsvRow(sb,
        [
            split.Name,
            Inv(col2), Inv(col2),
            Inv(seg),
            "1",                // Loop
            "0",                // And
            Inv(key),
            "0", "0",           // AdjTable, AdjTimer
            "-1",               // Seek
            "0", "0",           // M, A
            "8",                // 方式 (L2 Norm)
            Inv(threshold),
            pn,
            Inv(pauseTime),
            Inv(t),
            Inv(delayMs),
            "90",               // 閾値N
            "0", "0", "0",      // R/G/B
            "10",               // 色許容
            "none",             // Filter
            "300",              // Akaze
            Inv(px), Inv(py), Inv(sx), Inv(sy),
            Inv(pxS), Inv(pyS), Inv(sxS), Inv(syS),
            "-999999", "-999999", "999999", "999999",
        ]);
    }

    private static string Inv(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static void AppendCsvRow(StringBuilder sb, IReadOnlyList<string> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append('"');
            sb.Append(fields[i].Replace("\"", "\"\""));
            sb.Append('"');
        }
        sb.Append("\r\n");
    }
}
