using Avalonia.Controls;

namespace AutoSplit_AutoMask;

public record SavestateInstructionItem(string Heading, string Instructions);

public partial class SavestateInstructionsWindow : Window
{
    public SavestateInstructionsWindow()
    {
        InitializeComponent();
    }

    public SavestateInstructionsWindow(SplitPreset preset) : this()
    {
        Title = $"Savestate Instructions - {preset.PresetName}";

        var items = new List<SavestateInstructionItem>();
        if (preset.Splits is not null)
        {
            int padWidth = preset.Splits.Count.ToString().Length;
            for (int i = 0; i < preset.Splits.Count; i++)
            {
                var split = preset.Splits[i];
                if (string.IsNullOrEmpty(split.Savestate) || string.IsNullOrEmpty(split.SavestateInstructions))
                {
                    continue;
                }

                string heading = $"{i.ToString().PadLeft(padWidth, '0')}. {split.Name}";
                items.Add(new SavestateInstructionItem(heading, split.SavestateInstructions));
            }
        }

        InstructionsList.ItemsSource = items;
    }
}
