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

        string prefix = split.Name switch
        {
            "reset" => "reset",
            "start_auto_splitter" => "start_auto_splitter",
            _ => $"{splitIndex.ToString().PadLeft(totalSplits.ToString().Length, '0')}_{split.Name}"
        };

        string output = $"{prefix}_({split.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)})";

        if (!(Math.Abs(split.PauseTime - 3.0) < 0.01f))
        {
            output += $"_[{split.PauseTime.ToString(System.Globalization.CultureInfo.InvariantCulture)}]";
        }

        if (split.Delay > 0)
        {
            output += $"_#{split.Delay}#";
        }

        if (split.Dummy)
        {
            output += "_{d}";
        }

        if (split.Inverted)
        {
            output += "_{b}";
        }

        return output + ".png";
    }
}
