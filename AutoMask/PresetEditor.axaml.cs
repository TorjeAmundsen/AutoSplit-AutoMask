using System.Text.Json.Nodes;
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
    private class EditablePreset
    {
        public string? OriginalFolder { get; set; }
        public string PresetName { get; set; } = "";
        public string GameName { get; set; } = "";
        public List<EditableSplit> Splits { get; } = [];
    }

    private class EditableSplit
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

    // Characters forbidden in split names by the AutoSplit file naming spec
    private static readonly Regex InvalidNameChars = new(@"[#@{}\(\)\[\]\^]");

    private readonly string _presetsDirectory;
    private List<EditablePreset> _editablePresets = [];
    private EditablePreset? _selectedPreset;
    private EditableSplit? _selectedSplit;

    // Prevents form event handlers from writing back to the model while we're populating the form
    private bool _suppressFormEvents;

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
                    Threshold = split.Threshold,
                    // Treat as explicitly set only when value differs from the record default
                    PauseTimeEnabled = Math.Abs(split.PauseTime - 3.0f) > 0.001f,
                    PauseTime = split.PauseTime,
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
        PresetListBox.Items.Clear();
        foreach (var preset in _editablePresets)
        {
            PresetListBox.Items.Add(preset.PresetName.Length > 0 ? preset.PresetName : "(unnamed preset)");
        }
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
        ClearSplitForm();
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

        if (_selectedPreset?.OriginalFolder != null)
        {
            string folderFull = Path.GetFullPath(_selectedPreset.OriginalFolder);
            string maskFull = Path.GetFullPath(split.MaskAbsolutePath);
            if (maskFull.StartsWith(folderFull, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(folderFull, maskFull);
            }
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
            BtnSaveAsNew.IsEnabled = false;
            return;
        }

        bool anyInvalidName = _selectedPreset.Splits.Any(s => InvalidNameChars.IsMatch(s.Name));
        bool nameValid = !string.IsNullOrWhiteSpace(_selectedPreset.PresetName);

        BtnSave.IsEnabled = nameValid && !anyInvalidName;
        BtnSaveAsNew.IsEnabled = nameValid && !anyInvalidName;
    }

    private void PresetListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int index = PresetListBox.SelectedIndex;
        if (index < 0 || index >= _editablePresets.Count)
        {
            return;
        }

        SelectPreset(_editablePresets[index]);
        UpdateSaveButtonState();
    }

    private void BtnNewPreset_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var newPreset = new EditablePreset { PresetName = "New Preset" };
        _editablePresets.Add(newPreset);
        PopulatePresetList();
        PresetListBox.SelectedIndex = _editablePresets.Count - 1;
    }

    private void PresetNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressFormEvents || _selectedPreset == null)
        {
            return;
        }

        _selectedPreset.PresetName = PresetNameBox.Text ?? "";

        // Refresh the preset list label for the currently selected item
        int index = PresetListBox.SelectedIndex;
        if (index >= 0)
        {
            PresetListBox.Items[index] = _selectedPreset.PresetName.Length > 0
                ? _selectedPreset.PresetName
                : "(unnamed preset)";
        }

        UpdateSaveButtonState();
    }

    private void GameNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressFormEvents || _selectedPreset == null)
        {
            return;
        }

        _selectedPreset.GameName = GameNameBox.Text ?? "";
    }

    private void SplitsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
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
    }

    private void SplitNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        string name = SplitNameBox.Text ?? "";
        _selectedSplit.Name = name;
        ValidateSplitName(name);
        RefreshSelectedSplitLabel();
        UpdateFilenamePreview();
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
    }

    private void SplitThresholdEnabledCheck_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        _selectedSplit.ThresholdEnabled = SplitThresholdEnabledCheck.IsChecked == true;
        UpdateFilenamePreview();
    }

    private void SplitPauseTimeEnabledCheck_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        _selectedSplit.PauseTimeEnabled = SplitPauseTimeEnabledCheck.IsChecked == true;
        UpdateFilenamePreview();
    }

    private void SplitDelayEnabledCheck_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        _selectedSplit.DelayEnabled = SplitDelayEnabledCheck.IsChecked == true;
        UpdateFilenamePreview();
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
            _selectedSplit.Threshold = value;
            _suppressFormEvents = true;
            SplitThresholdSlider.Value = value;
            _suppressFormEvents = false;
            SplitThresholdBox.Classes.Remove("Invalid");
            UpdateFilenamePreview();
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
            _selectedSplit.PauseTime = value;
            SplitPauseTimeBox.Classes.Remove("Invalid");
            UpdateFilenamePreview();
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
            _selectedSplit.Delay = value;
            SplitDelayBox.Classes.Remove("Invalid");
            UpdateFilenamePreview();
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
    }

    private void SplitInvertedCheck_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressFormEvents || _selectedSplit == null)
        {
            return;
        }

        _selectedSplit.Inverted = SplitInvertedCheck.IsChecked == true;
        UpdateFilenamePreview();
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
        string folderName = SanitizeFolderName(preset.PresetName);
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
            int idx = PresetListBox.SelectedIndex;
            if (idx >= 0)
            {
                PresetListBox.Items[idx] = newName;
            }
            _suppressFormEvents = true;
            PresetNameBox.Text = newName;
            _suppressFormEvents = false;

            folderName = SanitizeFolderName(newName);
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
            Directory.CreateDirectory(targetFolder);

            string targetFolderFull = Path.GetFullPath(targetFolder);
            // Normalize with a trailing separator so StartsWith can't match a sibling folder
            // that shares a name prefix (e.g. "Foo/" won't match "FooBar/mask.png")
            string targetFolderPrefix = targetFolderFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                        + Path.DirectorySeparatorChar;
            var splitRelPaths = new List<string>();

            foreach (var split in preset.Splits)
            {
                if (string.IsNullOrEmpty(split.MaskAbsolutePath))
                {
                    splitRelPaths.Add("");
                    continue;
                }

                string maskFull = Path.GetFullPath(split.MaskAbsolutePath);

                if (maskFull.StartsWith(targetFolderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    splitRelPaths.Add(Path.GetRelativePath(targetFolderFull, maskFull));
                }
                else
                {
                    string destFilename = Path.GetFileName(maskFull);
                    string destPath = Path.Combine(targetFolderFull, destFilename);
                    File.Copy(maskFull, destPath, overwrite: true);
                    // Update the model so subsequent saves treat this file as already in place
                    split.MaskAbsolutePath = destPath;
                    splitRelPaths.Add(destFilename);
                }
            }

            var splitsArray = new JsonArray();
            for (int i = 0; i < preset.Splits.Count; i++)
            {
                var split = preset.Splits[i];
                var splitObj = new JsonObject
                {
                    ["mask"] = splitRelPaths[i],
                    ["name"] = split.Name,
                };

                if (split.ThresholdEnabled)
                {
                    splitObj["threshold"] = split.Threshold;
                }

                if (split.PauseTimeEnabled)
                {
                    splitObj["pauseTime"] = split.PauseTime;
                }

                if (split.DelayEnabled)
                {
                    splitObj["delay"] = split.Delay;
                }

                if (split.Dummy)
                {
                    splitObj["dummy"] = true;
                }

                if (split.Inverted)
                {
                    splitObj["inverted"] = true;
                }

                splitsArray.Add(splitObj);
            }

            var jsonObj = new JsonObject
            {
                ["$schema"] = "../preset-schema.json",
                ["presetName"] = preset.PresetName,
            };

            if (!string.IsNullOrWhiteSpace(preset.GameName))
            {
                jsonObj["gameName"] = preset.GameName;
            }

            jsonObj["splits"] = splitsArray;

            string json = jsonObj.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(targetFolderFull, "preset.json"), json);

            preset.OriginalFolder = targetFolderFull;
            PresetsModified = true;

            await MessageBoxManager
                .GetMessageBoxStandard("Saved", $"Preset saved to \"{folderName(targetFolderFull)}\".")
                .ShowWindowDialogAsync(this);
        }
        catch (Exception ex)
        {
            await MessageBoxManager
                .GetMessageBoxStandard("Save failed", ex.Message, ButtonEnum.Ok, MsgBoxIcon.Error)
                .ShowWindowDialogAsync(this);
        }

        static string folderName(string path) => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static string SanitizeFolderName(string presetName)
    {
        // Replace spaces with underscores and remove characters invalid in directory names
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = presetName.Replace(' ', '_');
        sanitized = new string(sanitized.Where(c => !invalidChars.Contains(c)).ToArray());
        return sanitized.Length > 0 ? sanitized : "NewPreset";
    }

    private void BtnClose_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
