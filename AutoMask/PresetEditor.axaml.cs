using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SkiaSharp;
using MessageBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsgBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace AutoSplit_AutoMask;

public partial class PresetEditor : Window
{
    // Characters forbidden in split names by the AutoSplit file naming spec
    private static readonly Regex InvalidNameChars = new(@"[#@{}\(\)\[\]\^]");

    private readonly string _presetsDirectory;
    private List<EditablePreset> _editablePresets = [];
    private List<PresetDisplayItem> _presetDisplayItems = [];
    private EditablePreset? _selectedPreset;
    private EditableSplit? _selectedSplit;

    // Prevents form event handlers from writing back to the model while we're populating the form
    private bool _suppressFormEvents;
    private bool _suppressPresetSelection;
    private bool _closingConfirmed;

    private string? _baseImagePath;
    private Bitmap? _maskPreviewBitmap;
    private Bitmap? _outputPreviewBitmap;
    private readonly Dictionary<string, SKBitmap> _previewMaskCache = [];

    public bool PresetsModified { get; private set; }

    public PresetEditor(List<SplitPreset> presets, string presetsDirectory)
    {
        InitializeComponent();
        _presetsDirectory = presetsDirectory;
        LoadFromPresets(presets);
        PopulatePresetList();
        RefreshGameNameSuggestions();

        PresetListBox.ContainerPrepared += (_, e) =>
        {
            if (e.Container is not ListBoxItem item || e.Index >= _presetDisplayItems.Count)
            {
                return;
            }

            var displayItem = _presetDisplayItems[e.Index];

            if (displayItem.IsHeader)
            {
                item.IsHitTestVisible = false;
                item.Focusable = false;
                item.Classes.Remove("Dirty");
            }
            else
            {
                item.IsHitTestVisible = true;
                item.Focusable = true;
                if (displayItem.IsDirty)
                {
                    item.Classes.Add("Dirty");
                }
                else
                {
                    item.Classes.Remove("Dirty");
                }
            }
        };
    }

    private void LoadFromPresets(List<SplitPreset> presets)
    {
        _editablePresets = presets
            .Select(p => new EditablePreset
            {
                OriginalFolder = p.PresetFolder,
                PresetName = p.PresetName ?? "",
                GameName = p.GameName ?? "",
                Splits =
                {
                    // Using AddRange via object initializer isn't supported, so we populate in Select
                }
            })
            .ToList();

        // Populate splits separately since the Splits list is get-only
        for (int i = 0; i < presets.Count; i++)
        {
            var source = presets[i];
            var dest = _editablePresets[i];

            if (source.Splits == null || source.PresetFolder == null)
            {
                continue;
            }

            foreach (var split in source.Splits)
            {
                dest.Splits.Add(new EditableSplit
                {
                    Name = split.Name,
                    MaskAbsolutePath = Path.GetFullPath(Path.Combine(source.PresetFolder, split.Mask)),
                    ThresholdEnabled = true,
                    // Round to 2 decimal places to match the TextBox display precision and avoid
                    // float → double conversion noise causing false dirty comparisons.
                    Threshold = Math.Round((double)split.Threshold, 2),
                    // Treat as explicitly set only when value differs from the record default
                    PauseTimeEnabled = Math.Abs(split.PauseTime - 3.0f) > 0.001f,
                    PauseTime = Math.Round((double)split.PauseTime, 2),
                    DelayEnabled = split.Delay > 0,
                    Delay = (int)split.Delay,
                    Dummy = split.Dummy,
                    Inverted = split.Inverted,
                });
            }
        }
    }

    private void PopulatePresetList()
    {
        _presetDisplayItems.Clear();
        PresetListBox.Items.Clear();

        var sorted = _editablePresets
            .OrderBy(p => p.GameName)
            .ThenBy(p => p.PresetName)
            .ToList();

        string? currentGame = null;

        foreach (var preset in sorted)
        {
            if (preset.GameName != currentGame)
            {
                currentGame = preset.GameName;
                var header = PresetDisplayItem.ForHeader(currentGame);
                _presetDisplayItems.Add(header);
                PresetListBox.Items.Add(header);
            }

            var item = PresetDisplayItem.ForPreset(preset);
            _presetDisplayItems.Add(item);
            PresetListBox.Items.Add(item);
        }
    }

    private void RefreshGameNameSuggestions()
    {
        var gameNames = _editablePresets
            .Select(p => p.GameName)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g)
            .ToList();

        GameNameBox.ItemsSource = gameNames;
    }

    // Forces the DataTemplate for a preset list item to re-render by replacing the wrapper.
    // Necessary because EditablePreset doesn't implement INotifyPropertyChanged.
    private void RefreshPresetListItem(EditablePreset preset)
    {
        int idx = _presetDisplayItems.FindIndex(d => d.Preset == preset);

        if (idx < 0)
        {
            return;
        }

        var newItem = PresetDisplayItem.ForPreset(preset);
        _presetDisplayItems[idx] = newItem;
        PresetListBox.Items[idx] = newItem;

        if (PresetListBox.ContainerFromIndex(idx) is ListBoxItem container)
        {
            if (preset.IsDirty)
            {
                container.Classes.Add("Dirty");
            }
            else
            {
                container.Classes.Remove("Dirty");
            }
        }
    }

    private void MarkCurrentPresetDirty()
    {
        if (_selectedPreset == null)
        {
            return;
        }

        _selectedPreset.IsDirty = true;
        RefreshPresetListItem(_selectedPreset);
        UpdateSaveButtonState();
    }

    private void SelectPreset(EditablePreset preset)
    {
        _suppressFormEvents = true;
        _selectedPreset = preset;
        _selectedSplit = null;

        PresetNameBox.Text = preset.PresetName;
        GameNameBox.Text = preset.GameName;

        PopulateSplitsList();
        ShowSplitForm(false);

        // Clear only the split-related visuals — form controls stay as-is since the form
        // is hidden and will be fully repopulated by SelectSplit when a split is picked.
        MaskPreviewImage.Source = null;
        _maskPreviewBitmap?.Dispose();
        _maskPreviewBitmap = null;
        FilenamePreviewLabel.Text = "";

        _suppressFormEvents = false;
    }

    private void PopulateSplitsList()
    {
        SplitsListBox.Items.Clear();

        if (_selectedPreset == null)
        {
            return;
        }

        for (int i = 0; i < _selectedPreset.Splits.Count; i++)
        {
            SplitsListBox.Items.Add($"{i + 1}. {_selectedPreset.Splits[i].Name}");
        }
    }

    private void SelectSplit(EditableSplit split)
    {
        _suppressFormEvents = true;
        _selectedSplit = split;

        SplitNameBox.Text = split.Name;
        SplitMaskBox.Text = MaskDisplayPath(split);
        SplitThresholdEnabledCheck.IsChecked = split.ThresholdEnabled;
        SplitThresholdBox.Text = split.Threshold.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        SplitThresholdSlider.Value = split.Threshold;
        SplitPauseTimeEnabledCheck.IsChecked = split.PauseTimeEnabled;
        SplitPauseTimeBox.Text = split.PauseTime.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        SplitDelayEnabledCheck.IsChecked = split.DelayEnabled;
        SplitDelayBox.Text = split.Delay.ToString();
        SplitDummyCheck.IsChecked = split.Dummy;
        SplitInvertedCheck.IsChecked = split.Inverted;

        ShowSplitForm(true);
        ValidateSplitName(split.Name);
        UpdateMaskPreview(split.MaskAbsolutePath);
        UpdateFilenamePreview();
        _ = UpdateOutputPreview();

        _suppressFormEvents = false;
    }

    private void ShowSplitForm(bool visible)
    {
        SplitFormScrollViewer.IsVisible = visible;
        NoSplitSelectedHint.IsVisible = !visible;
    }

    private void ClearSplitForm()
    {
        SplitNameBox.Text = "";
        SplitMaskBox.Text = "";
        SplitThresholdEnabledCheck.IsChecked = true;
        SplitThresholdBox.Text = "0.95";
        SplitThresholdSlider.Value = 0.95;
        SplitPauseTimeEnabledCheck.IsChecked = false;
        SplitPauseTimeBox.Text = "3";
        SplitDelayEnabledCheck.IsChecked = false;
        SplitDelayBox.Text = "0";
        SplitDummyCheck.IsChecked = false;
        SplitInvertedCheck.IsChecked = false;
        MaskPreviewImage.Source = null;
        _maskPreviewBitmap?.Dispose();
        _maskPreviewBitmap = null;
        FilenamePreviewLabel.Text = "";
    }

    private void UpdateFilenamePreview()
    {
        if (_selectedSplit == null || _selectedPreset == null)
        {
            FilenamePreviewLabel.Text = "";
            return;
        }

        int splitIndex = _selectedPreset.Splits.IndexOf(_selectedSplit);
        int totalSplits = _selectedPreset.Splits.Count;

        FilenamePreviewLabel.Text = PresetService.BuildFilename(
            _selectedSplit.Name,
            splitIndex, totalSplits,
            _selectedSplit.ThresholdEnabled ? (float?)_selectedSplit.Threshold : null,
            _selectedSplit.PauseTimeEnabled ? (float?)_selectedSplit.PauseTime : null,
            _selectedSplit.DelayEnabled ? (uint?)_selectedSplit.Delay : null,
            _selectedSplit.Dummy,
            _selectedSplit.Inverted);
    }

    // Returns a relative path for display when the mask is inside the preset folder,
    // otherwise returns the full absolute path so the user knows what file is referenced.
    private string MaskDisplayPath(EditableSplit split)
    {
        if (string.IsNullOrEmpty(split.MaskAbsolutePath))
        {
            return "";
        }

        if (_selectedPreset?.OriginalFolder is null)
        {
            return split.MaskAbsolutePath;
        }

        string folderFull = Path.GetFullPath(_selectedPreset.OriginalFolder);
        string maskFull = Path.GetFullPath(split.MaskAbsolutePath);
        if (maskFull.StartsWith(folderFull, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(folderFull, maskFull);
        }

        return split.MaskAbsolutePath;
    }

    private void UpdateMaskPreview(string absolutePath)
    {
        _maskPreviewBitmap?.Dispose();
        _maskPreviewBitmap = null;
        MaskPreviewImage.Source = null;

        if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
        {
            return;
        }

        try
        {
            _maskPreviewBitmap = new Bitmap(absolutePath);
            MaskPreviewImage.Source = _maskPreviewBitmap;
        }
        catch
        {
            // If the file can't be loaded as a bitmap, simply don't show a preview
        }
    }

    private async void OutputPreviewBorder_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select base image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image files") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"] }
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        _baseImagePath = files[0].Path.LocalPath;
        OutputPreviewHint.IsVisible = false;
        await UpdateOutputPreview();
    }

    private async Task UpdateOutputPreview()
    {
        _outputPreviewBitmap?.Dispose();
        _outputPreviewBitmap = null;
        OutputPreviewImage.Source = null;

        string? maskPath = _selectedSplit?.MaskAbsolutePath;
        if (string.IsNullOrEmpty(_baseImagePath) || !File.Exists(_baseImagePath)
            || string.IsNullOrEmpty(maskPath) || !File.Exists(maskPath))
        {
            return;
        }

        try
        {
            var skResult = await Task.Run(() =>
                ImageProcessor.ApplyScaledAlphaChannel(_baseImagePath, maskPath, _previewMaskCache));
            _outputPreviewBitmap = ImageProcessor.ToAvaloniaBitmap(skResult);
            skResult.Dispose();
            OutputPreviewImage.Source = _outputPreviewBitmap;
        }
        catch
        {
            // If preview generation fails, leave the output box empty
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        foreach (var bitmap in _previewMaskCache.Values)
        {
            bitmap.Dispose();
        }
        _maskPreviewBitmap?.Dispose();
        _outputPreviewBitmap?.Dispose();
    }

    private void ValidateSplitName(string name)
    {
        bool isInvalid = InvalidNameChars.IsMatch(name);

        if (isInvalid)
        {
            SplitNameBox.Classes.Add("Invalid");
        }
        else
        {
            SplitNameBox.Classes.Remove("Invalid");
        }

        UpdateSaveButtonState();
    }

    private void UpdateSaveButtonState()
    {
        if (_selectedPreset == null)
        {
            BtnSave.IsEnabled = false;
            BtnSaveAll.IsEnabled = false;
            BtnSaveAsNew.IsEnabled = false;
            return;
        }

        bool anyInvalidName = _selectedPreset.Splits.Any(s => InvalidNameChars.IsMatch(s.Name));
        bool nameValid = !string.IsNullOrWhiteSpace(_selectedPreset.PresetName);

        BtnSave.IsEnabled = nameValid && !anyInvalidName;
        BtnSaveAll.IsEnabled = _editablePresets.Any(p => p != _selectedPreset && p.IsDirty);
        BtnSaveAsNew.IsEnabled = nameValid && !anyInvalidName;
    }

    private async void PresetListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetSelection)
        {
            return;
        }

        int displayIdx = PresetListBox.SelectedIndex;
        if (displayIdx < 0 || displayIdx >= _presetDisplayItems.Count)
        {
            return;
        }

        // Ignore clicks on group headers (shouldn't reach here since they're non-interactive)
        if (_presetDisplayItems[displayIdx].IsHeader)
        {
            return;
        }

        EditablePreset? newPreset = _presetDisplayItems[displayIdx].Preset;
        if (newPreset == null)
        {
            return;
        }

        if (_selectedPreset?.IsDirty == true)
        {
            // Revert the visual selection while we ask the user
            _suppressPresetSelection = true;
            PresetListBox.SelectedIndex = _presetDisplayItems.FindIndex(d => d.Preset == _selectedPreset);
            _suppressPresetSelection = false;

            var action = await ShowUnsavedChangesDialogAsync(_selectedPreset.PresetName);

            if (action == UnsavedAction.Cancel)
            {
                return;
            }

            if (action == UnsavedAction.Save)
            {
                if (_selectedPreset.OriginalFolder != null)
                {
                    await SavePreset(_selectedPreset, _selectedPreset.OriginalFolder);
                }
                else
                {
                    await SaveToNewFolder(_selectedPreset);
                }

                // If save was cancelled (e.g. user dismissed the rename prompt), abort the switch
                if (_selectedPreset.IsDirty)
                {
                    return;
                }
            }

            // Commit the switch
            _suppressPresetSelection = true;
            PresetListBox.SelectedIndex = displayIdx;
            _suppressPresetSelection = false;
        }

        SelectPreset(newPreset);
        UpdateSaveButtonState();
    }

    private void BtnNewPreset_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var newPreset = new EditablePreset { PresetName = "New Preset" };
        _editablePresets.Add(newPreset);
        PopulatePresetList();
        int displayIdx = _presetDisplayItems.FindIndex(d => d.Preset == newPreset);
        if (displayIdx >= 0)
        {
            PresetListBox.SelectedIndex = displayIdx;
        }
    }

    private void PresetNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressFormEvents || _selectedPreset == null)
        {
            return;
        }

        string newPresetName = PresetNameBox.Text ?? "";
        if (newPresetName == _selectedPreset.PresetName)
        {
            return;
        }

        _selectedPreset.PresetName = newPresetName;
        MarkCurrentPresetDirty();
        UpdateSaveButtonState();
    }

    private void GameNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressFormEvents || _selectedPreset == null)
        {
            return;
        }

        string newGameName = GameNameBox.Text ?? "";
        if (newGameName == _selectedPreset.GameName)
        {
            return;
        }

        _selectedPreset.GameName = newGameName;
        MarkCurrentPresetDirty();
    }

    private void SplitsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressFormEvents)
        {
            return;
        }

        int index = SplitsListBox.SelectedIndex;
        if (_selectedPreset == null || index < 0 || index >= _selectedPreset.Splits.Count)
        {
            return;
        }

        SelectSplit(_selectedPreset.Splits[index]);
    }

    private void BtnAddSplit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedPreset == null)
        {
            return;
        }

        var newSplit = new EditableSplit { Name = "NewSplit" };
        int insertAt = SplitsListBox.SelectedIndex + 1;

        if (insertAt <= 0 || insertAt > _selectedPreset.Splits.Count)
        {
            insertAt = _selectedPreset.Splits.Count;
        }

        _selectedPreset.Splits.Insert(insertAt, newSplit);
        PopulateSplitsList();
        SplitsListBox.SelectedIndex = insertAt;
        MarkCurrentPresetDirty();
    }

    private void BtnRemoveSplit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedPreset == null)
        {
            return;
        }

        int index = SplitsListBox.SelectedIndex;
        if (index < 0 || index >= _selectedPreset.Splits.Count)
        {
            return;
        }

        _selectedPreset.Splits.RemoveAt(index);
        PopulateSplitsList();
        MarkCurrentPresetDirty();

        int nextIndex = Math.Min(index, _selectedPreset.Splits.Count - 1);
        SplitsListBox.SelectedIndex = nextIndex;

        if (nextIndex < 0)
        {
            _selectedSplit = null;
            ShowSplitForm(false);
            ClearSplitForm();
        }
    }

    private void BtnMoveSplitUp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedPreset == null)
        {
            return;
        }

        int index = SplitsListBox.SelectedIndex;
        if (index <= 0)
        {
            return;
        }

        (_selectedPreset.Splits[index], _selectedPreset.Splits[index - 1]) =
            (_selectedPreset.Splits[index - 1], _selectedPreset.Splits[index]);

        PopulateSplitsList();
        SplitsListBox.SelectedIndex = index - 1;
        MarkCurrentPresetDirty();
    }

    private void BtnMoveSplitDown_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedPreset == null)
        {
            return;
        }

        int index = SplitsListBox.SelectedIndex;
        if (index < 0 || index >= _selectedPreset.Splits.Count - 1)
        {
            return;
        }

        (_selectedPreset.Splits[index], _selectedPreset.Splits[index + 1]) =
            (_selectedPreset.Splits[index + 1], _selectedPreset.Splits[index]);

        PopulateSplitsList();
        SplitsListBox.SelectedIndex = index + 1;
        MarkCurrentPresetDirty();
    }

    private void SplitNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        string name = SplitNameBox.Text ?? "";
        if (name == _selectedSplit.Name)
        {
            return;
        }

        _selectedSplit.Name = name;
        ValidateSplitName(name);
        RefreshSelectedSplitLabel();
        UpdateFilenamePreview();
        MarkCurrentPresetDirty();
    }

    private async void BtnBrowseMask_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedSplit == null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select mask image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PNG images") { Patterns = ["*.png"] }
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        string pickedPath = files[0].Path.LocalPath;
        _selectedSplit.MaskAbsolutePath = pickedPath;

        _suppressFormEvents = true;
        SplitMaskBox.Text = MaskDisplayPath(_selectedSplit);
        _suppressFormEvents = false;

        UpdateMaskPreview(pickedPath);
        await UpdateOutputPreview();
        RefreshSelectedSplitLabel();
        MarkCurrentPresetDirty();
    }

    private void SplitThresholdEnabledCheck_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        _selectedSplit.ThresholdEnabled = SplitThresholdEnabledCheck.IsChecked == true;
        UpdateFilenamePreview();
        MarkCurrentPresetDirty();
    }

    private void SplitPauseTimeEnabledCheck_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        _selectedSplit.PauseTimeEnabled = SplitPauseTimeEnabledCheck.IsChecked == true;
        UpdateFilenamePreview();
        MarkCurrentPresetDirty();
    }

    private void SplitDelayEnabledCheck_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        _selectedSplit.DelayEnabled = SplitDelayEnabledCheck.IsChecked == true;
        UpdateFilenamePreview();
        MarkCurrentPresetDirty();
    }

    private void SplitThresholdBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        if (double.TryParse(SplitThresholdBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value)
            && value is >= 0 and <= 1)
        {
            if (value == _selectedSplit.Threshold)
            {
                return;
            }

            _selectedSplit.Threshold = value;
            _suppressFormEvents = true;
            SplitThresholdSlider.Value = value;
            _suppressFormEvents = false;
            SplitThresholdBox.Classes.Remove("Invalid");
            UpdateFilenamePreview();
            MarkCurrentPresetDirty();
        }
        else
        {
            SplitThresholdBox.Classes.Add("Invalid");
        }
    }

    // Snap to two decimal places when the user leaves the text box so it matches the slider's precision
    private void SplitThresholdBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedSplit == null)
        {
            return;
        }

        _suppressFormEvents = true;
        SplitThresholdBox.Text = _selectedSplit.Threshold.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        SplitThresholdBox.Classes.Remove("Invalid");
        _suppressFormEvents = false;
    }

    private void SplitThresholdSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        double value = Math.Round(e.NewValue, 2);
        _selectedSplit.Threshold = value;
        _suppressFormEvents = true;
        SplitThresholdBox.Text = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        SplitThresholdBox.Classes.Remove("Invalid");
        _suppressFormEvents = false;
        UpdateFilenamePreview();
        MarkCurrentPresetDirty();
    }

    private void SplitPauseTimeBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        if (double.TryParse(SplitPauseTimeBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value) && value >= 0)
        {
            if (value == _selectedSplit.PauseTime)
            {
                return;
            }

            _selectedSplit.PauseTime = value;
            SplitPauseTimeBox.Classes.Remove("Invalid");
            UpdateFilenamePreview();
            MarkCurrentPresetDirty();
        }
        else
        {
            SplitPauseTimeBox.Classes.Add("Invalid");
        }
    }

    private void SplitPauseTimeBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedSplit == null)
        {
            return;
        }

        _suppressFormEvents = true;
        SplitPauseTimeBox.Text = _selectedSplit.PauseTime.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        SplitPauseTimeBox.Classes.Remove("Invalid");
        _suppressFormEvents = false;
    }

    private void SplitDelayBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        if (int.TryParse(SplitDelayBox.Text, out int value) && value >= 0)
        {
            if (value == _selectedSplit.Delay)
            {
                return;
            }

            _selectedSplit.Delay = value;
            SplitDelayBox.Classes.Remove("Invalid");
            UpdateFilenamePreview();
            MarkCurrentPresetDirty();
        }
        else
        {
            SplitDelayBox.Classes.Add("Invalid");
        }
    }

    private void SplitDelayBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedSplit == null)
        {
            return;
        }

        _suppressFormEvents = true;
        SplitDelayBox.Text = _selectedSplit.Delay.ToString();
        SplitDelayBox.Classes.Remove("Invalid");
        _suppressFormEvents = false;
    }

    private void SplitDummyCheck_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        _selectedSplit.Dummy = SplitDummyCheck.IsChecked == true;
        UpdateFilenamePreview();
        MarkCurrentPresetDirty();
    }

    private void SplitInvertedCheck_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        _selectedSplit.Inverted = SplitInvertedCheck.IsChecked == true;
        UpdateFilenamePreview();
        MarkCurrentPresetDirty();
    }

    private void RefreshSelectedSplitLabel()
    {
        if (_selectedPreset == null)
        {
            return;
        }

        int index = SplitsListBox.SelectedIndex;
        if (index < 0 || index >= _selectedPreset.Splits.Count)
        {
            return;
        }

        SplitsListBox.Items[index] = $"{index + 1}. {_selectedPreset.Splits[index].Name}";
    }
    
    private async void BtnSave_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedPreset == null)
        {
            return;
        }

        if (_selectedPreset.OriginalFolder != null)
        {
            await SavePreset(_selectedPreset, _selectedPreset.OriginalFolder);
        }
        else
        {
            await SaveToNewFolder(_selectedPreset);
        }
    }

    private async void BtnSaveAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dirtyPresets = _editablePresets.Where(p => p.IsDirty).ToList();
        foreach (var preset in dirtyPresets)
        {
            if (preset.OriginalFolder != null)
            {
                await SavePreset(preset, preset.OriginalFolder);
            }
            else
            {
                await SaveToNewFolder(preset);
            }
        }
    }

    private async void BtnSaveAsNew_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedPreset == null)
        {
            return;
        }

        await SaveToNewFolder(_selectedPreset);
    }

    private async Task SaveToNewFolder(EditablePreset preset)
    {
        string folderName = PresetService.SanitizeFolderName(preset.PresetName);
        string targetFolder = Path.Combine(_presetsDirectory, folderName);

        if (Directory.Exists(targetFolder))
        {
            string? newName = await ShowRenamePromptAsync(
                $"A preset folder named \"{folderName}\" already exists. Enter a new name:",
                preset.PresetName);

            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            preset.PresetName = newName;
            _suppressFormEvents = true;
            PresetNameBox.Text = newName;
            _suppressFormEvents = false;
            RefreshPresetListItem(preset);

            folderName = PresetService.SanitizeFolderName(newName);
            targetFolder = Path.Combine(_presetsDirectory, folderName);

            if (Directory.Exists(targetFolder))
            {
                var overwrite = await MessageBoxManager
                    .GetMessageBoxStandard(
                        "Folder already exists",
                        $"A preset folder named \"{folderName}\" already exists. Overwrite it?",
                        ButtonEnum.YesNo,
                        MsgBoxIcon.Warning)
                    .ShowWindowDialogAsync(this);

                if (overwrite != ButtonResult.Yes)
                {
                    return;
                }
            }
        }

        await SavePreset(preset, targetFolder);
    }

    private async Task<string?> ShowRenamePromptAsync(string message, string initialValue)
    {
        var tcs = new TaskCompletionSource<string?>();

        var textBox = new TextBox
        {
            Text = initialValue,
            Height = 24,
            Margin = new Avalonia.Thickness(0, 6, 0, 10),
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width = 72,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 72,
            Margin = new Avalonia.Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };

        var dialog = new Window
        {
            Title = "Name already exists",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C2C2C")),
            FontSize = 12,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(12),
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    textBox,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Children = { okBtn, cancelBtn },
                    },
                },
            },
        };

        okBtn.Click += (_, _) => { tcs.TrySetResult(textBox.Text); dialog.Close(); };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(null); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(null);
        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                tcs.TrySetResult(textBox.Text);
                dialog.Close();
            }
            else if (e.Key == Avalonia.Input.Key.Escape)
            {
                tcs.TrySetResult(null);
                dialog.Close();
            }
        };
        dialog.Opened += (_, _) => { textBox.Focus(); textBox.SelectAll(); };

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    private async Task SavePreset(EditablePreset preset, string targetFolder)
    {
        try
        {
            await PresetService.SavePresetToFolderAsync(preset, targetFolder);

            preset.IsDirty = false;
            PresetsModified = true;

            // Rebuild the grouped list — the game name may have changed, moving this
            // preset to a different group or creating/removing a group header.
            _suppressPresetSelection = true;
            PopulatePresetList();
            RefreshGameNameSuggestions();
            int newDisplayIdx = _presetDisplayItems.FindIndex(d => d.Preset == preset);
            if (newDisplayIdx >= 0)
            {
                PresetListBox.SelectedIndex = newDisplayIdx;
            }
            _suppressPresetSelection = false;

            string folderLabel = Path.GetFileName(
                preset.OriginalFolder!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            await MessageBoxManager
                .GetMessageBoxStandard("Saved", $"Preset saved to \"{folderLabel}\".")
                .ShowWindowDialogAsync(this);
        }
        catch (Exception ex)
        {
            await MessageBoxManager
                .GetMessageBoxStandard("Save failed", ex.Message, ButtonEnum.Ok, MsgBoxIcon.Error)
                .ShowWindowDialogAsync(this);
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closingConfirmed && _editablePresets.Any(p => p.IsDirty))
        {
            e.Cancel = true;

            var dirtyNames = _editablePresets
                .Where(p => p.IsDirty)
                .Select(p => string.IsNullOrWhiteSpace(p.PresetName) ? "(unnamed)" : p.PresetName);

            var result = await MessageBoxManager
                .GetMessageBoxStandard(
                    "Unsaved changes",
                    $"The following presets have unsaved changes:\n\n{string.Join("\n", dirtyNames)}\n\nClose and discard changes?",
                    ButtonEnum.YesNo,
                    MsgBoxIcon.Warning)
                .ShowWindowDialogAsync(this);

            if (result == ButtonResult.Yes)
            {
                _closingConfirmed = true;
                Close();
            }
        }

        base.OnClosing(e);
    }

    private async Task<UnsavedAction> ShowUnsavedChangesDialogAsync(string presetName)
    {
        var result = await MessageBoxManager
            .GetMessageBoxStandard(
                "Unsaved changes",
                $"\"{presetName}\" has unsaved changes. Save before switching?",
                ButtonEnum.YesNoCancel,
                MsgBoxIcon.Warning)
            .ShowWindowDialogAsync(this);

        return result switch
        {
            ButtonResult.Yes => UnsavedAction.Save,
            ButtonResult.No  => UnsavedAction.Discard,
            _                => UnsavedAction.Cancel,
        };
    }

    private void BtnClose_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
