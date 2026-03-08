namespace AutoSplit_AutoMask;

public class SplitPreset
{
    public string? PresetFolder { get; set; }
    public string? GameName { get; set; }
    public string? PresetName { get; set; }
    public IList<Split>? Splits { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj is not SplitPreset other)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return PresetFolder == other.PresetFolder
               && GameName == other.GameName
               && PresetName == other.PresetName
               && (Splits?.SequenceEqual(other.Splits ?? []) ?? other.Splits == null);
    }
}

public record Split
(
    string Mask,
    string Name,
    float Threshold = 0.95f,
    float PauseTime = 3.0f,
    uint Delay = 0,
    bool Dummy = false,
    bool Inverted = false
);
