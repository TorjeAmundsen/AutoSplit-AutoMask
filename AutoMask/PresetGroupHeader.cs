namespace AutoSplit_AutoMask;

/// <summary>Group header item displayed in preset lists and dropdowns.</summary>
public sealed record PresetGroupHeader(string GameName);

/// <summary>Selectable preset entry in the main-window ComboBox, carrying its index into _splitPresets.</summary>
public sealed record PresetComboItem(string PresetName, int DataIndex, int SplitCount, int TotalImages);
