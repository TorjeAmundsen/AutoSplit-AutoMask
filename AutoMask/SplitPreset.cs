namespace AutoSplit_AutoMask;

public class SplitPreset
{
    public string? PresetFolder { get; set; }
    public string? GameName { get; set; }
    public string? PresetName { get; init; }
    public IList<SingleSplit>? Splits { get; init; }

    public override bool Equals(object obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        SplitPreset other = obj as SplitPreset;
        if (other == null || other.PresetFolder != PresetFolder || other.PresetName != PresetName)
        {
            return false;
        }

        return PresetFolder == other.PresetFolder
               && GameName == other.GameName
               && PresetName == other.PresetName
               && Splits.SequenceEqual(other.Splits);
    }
}

public record SingleSplit
(
    string MaskImagePath,
    string Name = "",
    float Threshold = 0.95f,
    float PauseTime = 3.0f,
    uint SplitDelay = 0,
    bool Dummy = false,
    bool Inverted = false,
    bool Enabled = true
);
