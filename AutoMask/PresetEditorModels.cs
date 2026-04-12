namespace AutoSplit_AutoMask;

internal enum UnsavedAction { Save, Discard, Cancel }

public class EditablePreset
{
    public string? OriginalFolder { get; set; }
    public string PresetName { get; set; } = "";
    public string GameName { get; set; } = "";
    public List<EditableSplit> Splits { get; } = [];
    public bool IsDirty { get; set; }

    public int SplitCount => Splits.Count(s =>
        !s.Dummy &&
        s.Name != "start_auto_splitter" &&
        s.Name != "reset");

    public int TotalImages => Splits.Count;
}

public class EditableSplit
{
    public string Name { get; set; } = "";
    public string MaskAbsolutePath { get; set; } = "";
    public bool ThresholdEnabled { get; set; } = true;
    public double Threshold { get; set; } = 0.95;
    public bool PauseTimeEnabled { get; set; } = false;
    public double PauseTime { get; set; } = 3.0;
    public bool DelayEnabled { get; set; } = false;
    public int Delay { get; set; } = 0;
    public bool Dummy { get; set; } = false;
    public bool Inverted { get; set; } = false;
}

// Wraps either a group header or an EditablePreset for display in the preset ListBox.
// A single DataTemplate can render both types by checking IsHeader.
public sealed class PresetDisplayItem
{
    public bool IsHeader { get; private init; }
    public string GroupName { get; private init; } = "";
    public EditablePreset? Preset { get; private init; }

    public bool IsCollapsed { get; private init; }
    public string CollapseArrow => IsCollapsed ? "▶" : "▼";

    public static PresetDisplayItem ForHeader(string gameName, bool isCollapsed = false)
        => new() { IsHeader = true, GroupName = gameName, IsCollapsed = isCollapsed };

    public static PresetDisplayItem ForPreset(EditablePreset p)
        => new() { Preset = p };

    // Properties forwarded to DataTemplate bindings
    public string PresetName => Preset?.PresetName ?? "";
    public string GameName   => IsHeader ? GroupName : Preset?.GameName ?? "";
    public int SplitCount    => Preset?.SplitCount ?? 0;
    public int TotalImages   => Preset?.TotalImages ?? 0;
    public bool IsDirty      => Preset?.IsDirty ?? false;
}
