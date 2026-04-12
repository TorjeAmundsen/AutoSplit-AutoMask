namespace AutoSplit_AutoMask;

public class PremadeSplitsFile
{
    public string? FolderPath { get; set; }
    public string? GameName { get; init; }
    public IList<Split>? Splits { get; init; }
}
