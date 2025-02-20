using System.IO;
using System.Text.Json;

namespace AutoSplit_AutoMask;

public class SplitPreset
{
    public string PresetName;
    public IList<SingleSplit>? Splits { get; set; }
}

public class FooBar
{
    public void BarFoo()
    {
        SplitPreset? splitPreset = JsonSerializer.Deserialize<SplitPreset>(File.ReadAllText("G:\\Torje Source\\C#\\AutoSplit-AutoMask\\AutoSplit-AutoMask\\SplitPresetTest.json"));

        if (splitPreset is not null && splitPreset.Splits is not null)
        {
            foreach (SingleSplit split in splitPreset.Splits)
            {
                Console.WriteLine($"Name: {split.Name}, Threshold: {split.Threshold}, PauseTime: {split.PauseTime}, SplitDelay: {split.SplitDelay}, Dummy: {split.Dummy}, Inverted: {split.Inverted}, MaskImagePath: {split.MaskImagePath}");
            }
        }
    }
}

public record SingleSplit
{
    public string MaskImagePath { get; }
    public string Name { get; }
    public float Threshold { get; }
    public float PauseTime { get; }
    public uint SplitDelay { get; }
    public bool Dummy { get; }
    public bool Inverted { get; }
    
    public SingleSplit(string maskImagePath, string name = "", float threshold = 0.95f, float pauseTime = 3.0f, uint splitDelay = 0,
        bool dummy = false, bool inverted = false)
    {
        MaskImagePath = maskImagePath;
        Name = name;
        Threshold = threshold;
        PauseTime = pauseTime;
        SplitDelay = splitDelay;
        Dummy = dummy;
        Inverted = inverted;
    }
};