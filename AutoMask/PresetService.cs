using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        // Reading and deserializing each preset.json in parallel — sequential await on a
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

        var splitPaths = Directory.EnumerateDirectories(splitsDirectory)
            .Where(dir => Directory.EnumerateFiles(dir, "splits.json", SearchOption.TopDirectoryOnly).Any())
            .ToArray();

        var results = await Task.WhenAll(splitPaths.Select(LoadOnePremadeSplitsAsync));

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

        return (foundSplitFiles.OrderBy(f => f.GameName).ToList(), failures);
    }

    private static async Task<(PremadeSplitsFile? File, LoadFailure? Failure)> LoadOnePremadeSplitsAsync(string splitPath)
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
}
