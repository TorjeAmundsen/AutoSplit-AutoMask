using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace AutoSplit_AutoMask;

/// <summary>
/// One JSON file that failed to load. <see cref="Reason"/> is either an exception message or
/// "deserialized to null" when the JSON parsed but produced no model.
/// </summary>
public sealed record LoadFailure(string Path, string Reason);

public static class PresetService
{
    public static async Task<(List<SplitPreset> Presets, List<LoadFailure> Failures)> LoadPresetsAsync(string presetsDirectory)
    {
        var presetPaths = Directory.EnumerateDirectories(presetsDirectory)
            .Where(dir => Directory.EnumerateFiles(dir, "preset.json", SearchOption.TopDirectoryOnly).Any())
            .ToArray();

        // Reading and deserializing each preset.json in parallel - sequential await on a
        // slow disk (e.g. networked drive, large preset library) summed into noticeable
        // startup latency.
        var results = await Task.WhenAll(presetPaths.Select(LoadOnePresetAsync));

        List<SplitPreset> foundPresets = [];
        List<LoadFailure> failures = [];
        foreach (var (preset, failure) in results)
        {
            if (preset is not null)
            {
                foundPresets.Add(preset);
            }
            else if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        return (foundPresets, failures);
    }

    private static async Task<(SplitPreset? Preset, LoadFailure? Failure)> LoadOnePresetAsync(string presetPath)
    {
        string filePath = Path.Combine(presetPath, "preset.json");
        try
        {
            var preset = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(filePath, Encoding.UTF8),
                AppJsonContext.Default.SplitPreset);

            if (preset is null)
            {
                return (null, new LoadFailure(filePath, "deserialized to null"));
            }

            preset.PresetFolder = presetPath;
            return (preset, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return (null, new LoadFailure(filePath, ex.Message));
        }
    }

    public static async Task<(List<PremadeSplitsFile> Files, List<LoadFailure> Failures)> LoadPremadeSplitsAsync(string splitsDirectory)
    {
        if (!Directory.Exists(splitsDirectory))
        {
            return ([], []);
        }

        // Each game folder either holds splits.json directly (one sectionless group) or holds
        // section subfolders that each hold their own splits.json. A section's display name is
        // its folder name with the leading numeric ordering prefix stripped.
        List<(string Path, string? Section)> specs = [];
        foreach (var gameDir in Directory.EnumerateDirectories(splitsDirectory))
        {
            if (Directory.EnumerateFiles(gameDir, "splits.json", SearchOption.TopDirectoryOnly).Any())
            {
                specs.Add((gameDir, null));
                continue;
            }

            var sections = Directory.EnumerateDirectories(gameDir)
                .Where(dir => Directory.EnumerateFiles(dir, "splits.json", SearchOption.TopDirectoryOnly).Any())
                .Select(dir =>
                {
                    var (order, display) = ParseSectionFolderName(Path.GetFileName(dir));
                    return (Path: dir, Section: (string?)display, Order: order);
                })
                .OrderBy(s => s.Order)
                .ThenBy(s => s.Section, StringComparer.OrdinalIgnoreCase);

            foreach (var section in sections)
            {
                specs.Add((section.Path, section.Section));
            }
        }

        var results = await Task.WhenAll(specs.Select(s => LoadOnePremadeSplitsAsync(s.Path, s.Section)));

        List<PremadeSplitsFile> foundSplitFiles = [];
        List<LoadFailure> failures = [];
        foreach (var (file, failure) in results)
        {
            if (file is not null)
            {
                foundSplitFiles.Add(file);
            }
            else if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        // OrderBy is a stable sort, so the per-game section order built above is preserved.
        return (foundSplitFiles.OrderBy(f => f.GameName).ToList(), failures);
    }

    // "01 Start, Reset, End" -> (1, "Start, Reset, End"). Folders without a leading number sort
    // last (int.MaxValue) and keep their name unchanged.
    private static (int Order, string Display) ParseSectionFolderName(string folderName)
    {
        int digits = 0;
        while (digits < folderName.Length && char.IsDigit(folderName[digits]))
        {
            digits++;
        }

        if (digits == 0 || !int.TryParse(folderName[..digits], out int order))
        {
            return (int.MaxValue, folderName);
        }

        int start = digits;
        while (start < folderName.Length && (char.IsWhiteSpace(folderName[start]) || folderName[start] is '-' or '_' or '.'))
        {
            start++;
        }

        string display = folderName[start..].Trim();
        return (order, display.Length > 0 ? display : folderName);
    }

    private static async Task<(PremadeSplitsFile? File, LoadFailure? Failure)> LoadOnePremadeSplitsAsync(string splitPath, string? sectionName)
    {
        string filePath = Path.Combine(splitPath, "splits.json");
        try
        {
            var splitsFile = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(filePath, Encoding.UTF8),
                AppJsonContext.Default.PremadeSplitsFile);

            if (splitsFile is null)
            {
                return (null, new LoadFailure(filePath, "deserialized to null"));
            }

            splitsFile.FolderPath = splitPath;
            splitsFile.SectionName = sectionName;
            return (splitsFile, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return (null, new LoadFailure(filePath, ex.Message));
        }
    }

    public static string CreateFilenameForSplit(SplitPreset preset, int splitIndex)
    {
        var split = preset.Splits![splitIndex];
        int totalSplits = preset.Splits.Count;

        float? pauseTime = Math.Abs(split.PauseTime - 3.0f) > 0.01f ? split.PauseTime : null;
        uint? delay = split.Delay > 0 ? split.Delay : null;

        return BuildFilename(split.Name, splitIndex, totalSplits,
            split.Threshold, pauseTime, delay, split.Dummy, split.Inverted);
    }

    public static string BuildFilename(string name, int splitIndex, int totalSplits,
        float? threshold, float? pauseTime, uint? delay, bool dummy, bool inverted)
    {
        string prefix = name switch
        {
            "reset" => "reset",
            "start_auto_splitter" => "start_auto_splitter",
            _ => $"{splitIndex.ToString().PadLeft(totalSplits.ToString().Length, '0')}_{name}"
        };

        string output = threshold is { } t
            ? $"{prefix}_({t.ToString(System.Globalization.CultureInfo.InvariantCulture)})"
            : prefix;

        if (pauseTime is { } pt)
        {
            output += $"_[{pt.ToString(System.Globalization.CultureInfo.InvariantCulture)}]";
        }

        if (delay is { } d)
        {
            output += $"_#{d}#";
        }

        if (dummy)
        {
            output += "_{d}";
        }

        if (inverted)
        {
            output += "_{b}";
        }

        return output + ".png";
    }

    /// <summary>
    /// Returns a filename that is not yet present in <paramref name="used"/>, using
    /// "name (1).ext", "name (2).ext", ... if the preferred name is already taken.
    /// The chosen name is added to the set so subsequent calls won't pick it.
    /// </summary>
    private static string ReserveUniqueName(string preferred, HashSet<string> used)
    {
        if (used.Add(preferred))
        {
            return preferred;
        }

        string ext = Path.GetExtension(preferred);
        string baseName = Path.GetFileNameWithoutExtension(preferred);
        for (int i = 1; ; i++)
        {
            string candidate = $"{baseName} ({i}){ext}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Replaces spaces with underscores and strips characters that are invalid in directory names.
    /// </summary>
    public static string SanitizeFolderName(string presetName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = presetName.Replace(' ', '_');
        sanitized = new string(sanitized.Where(c => !invalidChars.Contains(c)).ToArray());
        return sanitized.Length > 0 ? sanitized : "NewPreset";
    }

    /// <summary>
    /// Writes a preset to <paramref name="targetFolder"/>: creates the directory, resolves mask
    /// paths (copying any mask that lives outside the target folder), builds the JSON, and writes
    /// preset.json.  Updates <see cref="EditablePreset.OriginalFolder"/> on success.
    /// Throws on any I/O failure - the caller is responsible for showing error UI.
    /// </summary>
    internal static async Task SavePresetToFolderAsync(EditablePreset preset, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);

        string targetFolderFull = Path.GetFullPath(targetFolder);
        // Normalize with a trailing separator so StartsWith can't match a sibling folder
        // that shares a name prefix (e.g. "Foo/" won't match "FooBar/mask.png")
        string targetFolderPrefix = targetFolderFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;
        string savestatesFolder = Path.Combine(targetFolderFull, "savestates");
        string savestatesPrefix = savestatesFolder + Path.DirectorySeparatorChar;
        var splitRelPaths = new List<string>();
        var splitSavestateRelPaths = new List<string>();

        // Pre-pass: reserve filenames already locked in by splits whose mask/savestate is
        // already inside the target folder. Two splits referencing different external files
        // that share a filename would otherwise overwrite each other, and an external copy
        // could overwrite an internal mask of the same name when processed first.
        var usedMaskNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedSavestateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var split in preset.Splits)
        {
            if (!string.IsNullOrEmpty(split.MaskAbsolutePath))
            {
                string maskFull = Path.GetFullPath(split.MaskAbsolutePath);
                if (maskFull.StartsWith(targetFolderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string rel = Path.GetRelativePath(targetFolderFull, maskFull);
                    // Only top-level mask filenames can collide with copy destinations
                    // (which always land at the target folder root).
                    if (!rel.Contains(Path.DirectorySeparatorChar) && !rel.Contains(Path.AltDirectorySeparatorChar))
                    {
                        usedMaskNames.Add(rel);
                    }
                }
            }

            if (!string.IsNullOrEmpty(split.SavestateAbsolutePath))
            {
                string savestateFull = Path.GetFullPath(split.SavestateAbsolutePath);
                if (savestateFull.StartsWith(savestatesPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    usedSavestateNames.Add(Path.GetFileName(savestateFull));
                }
            }
        }

        foreach (var split in preset.Splits)
        {
            if (string.IsNullOrEmpty(split.MaskAbsolutePath))
            {
                splitRelPaths.Add("");
            }
            else
            {
                string maskFull = Path.GetFullPath(split.MaskAbsolutePath);

                if (maskFull.StartsWith(targetFolderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    splitRelPaths.Add(Path.GetRelativePath(targetFolderFull, maskFull));
                }
                else
                {
                    string destFilename = ReserveUniqueName(Path.GetFileName(maskFull), usedMaskNames);
                    string destPath = Path.Combine(targetFolderFull, destFilename);
                    File.Copy(maskFull, destPath, overwrite: true);
                    // Update the model so subsequent saves treat this file as already in place
                    split.MaskAbsolutePath = destPath;
                    splitRelPaths.Add(destFilename);
                }
            }

            if (string.IsNullOrEmpty(split.SavestateAbsolutePath))
            {
                splitSavestateRelPaths.Add("");
                continue;
            }

            string savestateFullPath = Path.GetFullPath(split.SavestateAbsolutePath);

            if (savestateFullPath.StartsWith(savestatesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                splitSavestateRelPaths.Add(Path.GetRelativePath(targetFolderFull, savestateFullPath));
            }
            else
            {
                Directory.CreateDirectory(savestatesFolder);
                string destFilename = ReserveUniqueName(Path.GetFileName(savestateFullPath), usedSavestateNames);
                string destPath = Path.Combine(savestatesFolder, destFilename);
                File.Copy(savestateFullPath, destPath, overwrite: true);
                split.SavestateAbsolutePath = destPath;
                splitSavestateRelPaths.Add(Path.Combine("savestates", destFilename));
            }
        }

        var splitsArray = new JsonArray();
        for (int i = 0; i < preset.Splits.Count; i++)
        {
            var split = preset.Splits[i];
            var splitObj = new JsonObject
            {
                ["mask"] = splitRelPaths[i],
                ["name"] = split.Name,
            };

            if (split.ThresholdEnabled)
            {
                splitObj["threshold"] = split.Threshold;
            }

            if (split.PauseTimeEnabled)
            {
                splitObj["pauseTime"] = split.PauseTime;
            }

            if (split.DelayEnabled)
            {
                splitObj["delay"] = split.Delay;
            }

            if (split.Dummy)
            {
                splitObj["dummy"] = true;
            }

            if (split.Inverted)
            {
                splitObj["inverted"] = true;
            }

            if (!string.IsNullOrEmpty(splitSavestateRelPaths[i]))
            {
                splitObj["savestate"] = splitSavestateRelPaths[i];

                if (!string.IsNullOrEmpty(split.SavestateInstructions))
                {
                    splitObj["savestateInstructions"] = split.SavestateInstructions;
                }
            }

            splitsArray.Add((JsonNode)splitObj);
        }

        var jsonObj = new JsonObject
        {
            ["$schema"] = "../preset-schema.json",
            ["presetName"] = preset.PresetName,
        };

        if (!string.IsNullOrWhiteSpace(preset.GameName))
        {
            jsonObj["gameName"] = preset.GameName;
        }

        jsonObj["splits"] = splitsArray;

        string json = jsonObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        string finalPath = Path.Combine(targetFolderFull, "preset.json");
        // Write to a sibling temp file, then atomically replace. File.WriteAllTextAsync
        // truncates the destination before writing, so a crash mid-write would leave
        // preset.json empty or partial; a rename on the same volume is atomic on NTFS.
        string tmpPath = finalPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmpPath, json, Encoding.UTF8);
            File.Move(tmpPath, finalPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmpPath)) { File.Delete(tmpPath); } } catch { /* best-effort */ }
            throw;
        }

        if (Directory.Exists(savestatesFolder))
        {
            var referencedNames = new HashSet<string>(
                splitSavestateRelPaths
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Select(Path.GetFileName)!,
                StringComparer.OrdinalIgnoreCase);

            foreach (string file in Directory.EnumerateFiles(savestatesFolder))
            {
                if (!referencedNames.Contains(Path.GetFileName(file)))
                {
                    // Best-effort cleanup; preset.json is already committed and a stale
                    // savestate file is harmless on disk, so don't fail the whole save.
                    try { File.Delete(file); } catch { /* ignore */ }
                }
            }
        }

        preset.OriginalFolder = targetFolderFull;
    }

    /// <summary>
    /// Writes a single new pre-made split into the bundled splits library: resolves the game and
    /// section folders (creating a new section with the next numeric prefix when needed), copies
    /// the mask into the game's shared <c>masks/</c> folder, scales the optional base image to a
    /// 64x48 thumbnail in <c>thumbnails/</c>, and appends the split to the section's splits.json.
    /// <paramref name="existing"/> is the already-loaded library, used to resolve folder paths for
    /// games/sections that already exist. Throws on any I/O or decode failure.
    /// </summary>
    internal static async Task AddPremadeSplitAsync(
        string splitsDirectory,
        IReadOnlyList<PremadeSplitsFile> existing,
        string gameName,
        string sectionName,
        NewPremadeSplitInput input)
    {
        // Resolve the game directory: reuse the folder an existing game already lives in
        // (the parent of any of its section folders), otherwise derive a new one.
        var gameFile = existing.FirstOrDefault(f =>
            f.FolderPath != null &&
            string.Equals(f.GameName, gameName, StringComparison.OrdinalIgnoreCase));
        string gameDir = gameFile?.FolderPath != null
            ? (gameFile.SectionName != null ? Path.GetDirectoryName(gameFile.FolderPath)! : gameFile.FolderPath)
            : Path.Combine(splitsDirectory, SanitizeFolderName(gameName));

        // Resolve the section directory: reuse an existing section's folder, else create
        // "NN <section>" with the next free numeric prefix.
        var sectionFile = existing.FirstOrDefault(f =>
            f.FolderPath != null && f.SectionName != null &&
            string.Equals(f.GameName, gameName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(f.SectionName, sectionName, StringComparison.OrdinalIgnoreCase));
        string sectionDir = sectionFile?.FolderPath
            ?? Path.Combine(gameDir, $"{NextSectionPrefix(gameDir):D2} {SanitizeSectionName(sectionName)}");

        string masksDir = Path.Combine(gameDir, "masks");
        string thumbsDir = Path.Combine(gameDir, "thumbnails");
        Directory.CreateDirectory(masksDir);
        Directory.CreateDirectory(thumbsDir);
        Directory.CreateDirectory(sectionDir);

        // Mask: keep the source filename so re-picking the same file reuses one copy.
        string maskFull = Path.GetFullPath(input.MaskSourcePath);
        var (maskName, maskPresent) = ResolveAssetName(masksDir, Path.GetFileName(maskFull),
            candidate => FilesEqual(candidate, maskFull));
        if (!maskPresent)
        {
            File.Copy(maskFull, Path.Combine(masksDir, maskName), overwrite: false);
        }

        // Thumbnail: scale to 64x48 and name it from the split (e.g. "Enter Deku" -> enter_deku_thumb.png).
        string? baseRel = null;
        if (!string.IsNullOrEmpty(input.BaseImageSourcePath))
        {
            byte[] thumbBytes = ScaleToThumbnailPng(input.BaseImageSourcePath);
            var (thumbName, thumbPresent) = ResolveAssetName(thumbsDir, ThumbnailFileName(input.Name),
                candidate => BytesEqualFile(thumbBytes, candidate));
            if (!thumbPresent)
            {
                await File.WriteAllBytesAsync(Path.Combine(thumbsDir, thumbName), thumbBytes);
            }
            baseRel = $"../thumbnails/{thumbName}";
        }

        // Build the split object, omitting fields left at their defaults (same rules as preset.json).
        var splitObj = new JsonObject
        {
            ["mask"] = $"../masks/{maskName}",
            ["name"] = input.Name,
        };
        if (!string.IsNullOrWhiteSpace(input.Description)) { splitObj["description"] = input.Description; }
        if (baseRel != null) { splitObj["baseImage"] = baseRel; }
        if (input.ThresholdEnabled) { splitObj["threshold"] = input.Threshold; }
        if (input.PauseEnabled) { splitObj["pauseTime"] = input.PauseTime; }
        if (input.DelayEnabled) { splitObj["delay"] = input.Delay; }
        if (input.Dummy) { splitObj["dummy"] = true; }
        if (input.Inverted) { splitObj["inverted"] = true; }

        // Append to (or create) the section's splits.json.
        string sectionJsonPath = Path.Combine(sectionDir, "splits.json");
        JsonObject root;
        if (File.Exists(sectionJsonPath))
        {
            root = JsonNode.Parse(await File.ReadAllTextAsync(sectionJsonPath, Encoding.UTF8)) as JsonObject
                   ?? throw new InvalidOperationException($"{sectionJsonPath} is not a JSON object.");
        }
        else
        {
            root = new JsonObject
            {
                ["$schema"] = "../../splits-schema.json",
                ["gameName"] = gameName,
            };
        }

        JsonArray splitsArray;
        if (root["splits"] is JsonArray arr)
        {
            splitsArray = arr;
        }
        else
        {
            splitsArray = new JsonArray();
            root["splits"] = splitsArray;
        }
        splitsArray.Add((JsonNode)splitObj);

        string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        string tmpPath = sectionJsonPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmpPath, json, Encoding.UTF8);
            File.Move(tmpPath, sectionJsonPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmpPath)) { File.Delete(tmpPath); } } catch { /* best-effort */ }
            throw;
        }
    }

    // Highest numeric section prefix already used under a game folder, plus one (1 if none).
    private static int NextSectionPrefix(string gameDir)
    {
        if (!Directory.Exists(gameDir))
        {
            return 1;
        }

        int max = 0;
        foreach (string dir in Directory.EnumerateDirectories(gameDir))
        {
            var (order, _) = ParseSectionFolderName(Path.GetFileName(dir));
            if (order != int.MaxValue && order > max)
            {
                max = order;
            }
        }
        return max + 1;
    }

    // Section folder names keep spaces/commas; only filesystem-invalid characters are stripped.
    private static string SanitizeSectionName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return sanitized.Length > 0 ? sanitized : "Section";
    }

    private static string ThumbnailFileName(string splitName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string baseName = new string(splitName.ToLowerInvariant().Replace(' ', '_')
            .Where(c => !invalid.Contains(c)).ToArray());
        return $"{(baseName.Length > 0 ? baseName : "split")}_thumb.png";
    }

    private static byte[] ScaleToThumbnailPng(string sourcePath)
    {
        using SKBitmap src = SKBitmap.Decode(sourcePath)
            ?? throw new InvalidOperationException($"Could not decode image: {sourcePath}");
        using SKBitmap scaled = src.Resize(new SKImageInfo(64, 48, SKColorType.Bgra8888),
            new SKSamplingOptions(SKFilterMode.Linear))
            ?? throw new InvalidOperationException($"Could not resize image: {sourcePath}");
        using SKImage img = SKImage.FromBitmap(scaled);
        using SKData data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // Picks a filename in <paramref name="folder"/>: the preferred name if free, the existing file
    // if <paramref name="matchesExisting"/> says it's identical (AlreadyPresent=true), else the next
    // " (n)" variant.
    private static (string Name, bool AlreadyPresent) ResolveAssetName(
        string folder, string preferred, Func<string, bool> matchesExisting)
    {
        string ext = Path.GetExtension(preferred);
        string baseName = Path.GetFileNameWithoutExtension(preferred);
        string candidate = preferred;
        for (int i = 1; ; i++)
        {
            string candidatePath = Path.Combine(folder, candidate);
            if (!File.Exists(candidatePath))
            {
                return (candidate, false);
            }
            if (matchesExisting(candidatePath))
            {
                return (candidate, true);
            }
            candidate = $"{baseName} ({i}){ext}";
        }
    }

    private static bool FilesEqual(string pathA, string pathB)
    {
        var a = new FileInfo(pathA);
        var b = new FileInfo(pathB);
        if (!a.Exists || !b.Exists || a.Length != b.Length)
        {
            return false;
        }

        using FileStream sa = a.OpenRead();
        using FileStream sb = b.OpenRead();
        int ba, bb;
        do
        {
            ba = sa.ReadByte();
            bb = sb.ReadByte();
            if (ba != bb)
            {
                return false;
            }
        } while (ba != -1);
        return true;
    }

    private static bool BytesEqualFile(byte[] content, string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != content.Length)
            {
                return false;
            }
            return File.ReadAllBytes(path).AsSpan().SequenceEqual(content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
