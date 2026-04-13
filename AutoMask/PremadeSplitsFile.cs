namespace AutoSplit_AutoMask;

public class PremadeSplitsFile
{
    public string? FolderPath { get; set; }
    public string? GameName { get; init; }
    public List<PremadeSplit>? Splits { get; init; }
}

public record PremadeSplit
(
    string Mask,
    string Name,
    string Description = "",
    string BaseImage = "",
    float Threshold = 0.95f,
    float PauseTime = 3.0f,
    uint Delay = 0,
    bool Dummy = false,
    bool Inverted = false
);
