using System.Text.Json;

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
            SplitPreset? preset = JsonSerializer.Deserialize<SplitPreset>(
                await File.ReadAllTextAsync(Path.Combine(presetPath, "preset.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (preset is not null)
            {
                preset.PresetFolder = presetPath;
                foundPresets.Add(preset);
            }
        }

        return foundPresets;
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
}
