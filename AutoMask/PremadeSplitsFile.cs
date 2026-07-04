namespace AutoSplit_AutoMask;

public class PremadeSplitsFile
{
    public string? FolderPath { get; set; }
    public string? SectionName { get; set; }
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

/// <summary>
/// User-entered data for authoring one new pre-made split, passed to
/// <see cref="PresetService.AddPremadeSplitAsync"/>. Mask/base images are given as source paths;
/// the service copies the mask and scales the base image into the game's shared folders.
/// </summary>
public sealed record NewPremadeSplitInput(
    string Name,
    string Description,
    string MaskSourcePath,
    string? BaseImageSourcePath,
    bool ThresholdEnabled,
    double Threshold,
    bool PauseEnabled,
    double PauseTime,
    bool DelayEnabled,
    int Delay,
    bool Dummy,
    bool Inverted);
