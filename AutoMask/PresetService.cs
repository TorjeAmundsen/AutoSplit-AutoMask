using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoSplit_AutoMask;

public static class PresetService
{
    public static async Task<List<SplitPreset>> LoadPresetsAsync(string presetsDirectory)
    {
        var presetPaths = Directory.EnumerateDirectories(presetsDirectory)
            .Where(dir => Directory.EnumerateFiles(dir, "preset.json", SearchOption.TopDirectoryOnly).Any())
            .ToArray();

        List<SplitPreset> foundPresets = [];

        foreach (string presetPath in presetPaths)
        {
            SplitPreset? preset = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(Path.Combine(presetPath, "preset.json")),
                AppJsonContext.Default.SplitPreset);

            if (preset is not null)
            {
                preset.PresetFolder = presetPath;
                foundPresets.Add(preset);
            }
        }

        return foundPresets;
    }

    public static async Task<List<PremadeSplitsFile>> LoadPremadeSplitsAsync(string splitsDirectory)
    {
        if (!Directory.Exists(splitsDirectory))
        {
            return [];
        }

        var splitPaths = Directory.EnumerateDirectories(splitsDirectory)
            .Where(dir => Directory.EnumerateFiles(dir, "splits.json", SearchOption.TopDirectoryOnly).Any())
            .ToArray();

        List<PremadeSplitsFile> foundSplitFiles = [];

        foreach (string splitPath in splitPaths)
        {
            PremadeSplitsFile? splitsFile = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(Path.Combine(splitPath, "splits.json")),
                AppJsonContext.Default.PremadeSplitsFile);

            if (splitsFile is not null)
            {
                splitsFile.FolderPath = splitPath;
                foundSplitFiles.Add(splitsFile);
            }
        }

        return foundSplitFiles.OrderBy(f => f.GameName).ToList();
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
    /// Throws on any I/O failure — the caller is responsible for showing error UI.
    /// </summary>
    internal static async Task SavePresetToFolderAsync(EditablePreset preset, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);

        string targetFolderFull = Path.GetFullPath(targetFolder);
        // Normalize with a trailing separator so StartsWith can't match a sibling folder
        // that shares a name prefix (e.g. "Foo/" won't match "FooBar/mask.png")
        string targetFolderPrefix = targetFolderFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;
        var splitRelPaths = new List<string>();

        foreach (var split in preset.Splits)
        {
            if (string.IsNullOrEmpty(split.MaskAbsolutePath))
            {
                splitRelPaths.Add("");
                continue;
            }

            string maskFull = Path.GetFullPath(split.MaskAbsolutePath);

            if (maskFull.StartsWith(targetFolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                splitRelPaths.Add(Path.GetRelativePath(targetFolderFull, maskFull));
            }
            else
            {
                string destFilename = Path.GetFileName(maskFull);
                string destPath = Path.Combine(targetFolderFull, destFilename);
                File.Copy(maskFull, destPath, overwrite: true);
                // Update the model so subsequent saves treat this file as already in place
                split.MaskAbsolutePath = destPath;
                splitRelPaths.Add(destFilename);
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
        await File.WriteAllTextAsync(Path.Combine(targetFolderFull, "preset.json"), json);

        preset.OriginalFolder = targetFolderFull;
    }
}
