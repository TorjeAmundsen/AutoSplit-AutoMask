using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SkiaSharp;

namespace AutoSplit_AutoMask;

using static Utils;

public record InputImageItem(string FullPath, string FileName, Bitmap Thumbnail);

public partial class MainWindow : Window
{
    public ObservableCollection<object> presetComboBoxItems { get; set; }
    public ObservableCollection<ComboBoxItem> splitsComboBoxItems { get; set; }
    public ObservableCollection<InputImageItem> inputImagesComboBoxItems { get; set; }
    // Maps each ComboBox display index to a _splitPresets index (null = group header)
    private List<int?> _presetDisplayMap = [];
    private int selectedPresetIndex = -1;
    public int selectedSplitIndex { get; set; }

    private List<string>? _inputImagePaths;
    private string? _selectedInputImagePath;
    private string? _outputDirectoryPath;
    private string? _alphaImagePath;
    private SKBitmap? _maskedImage;
    private Bitmap? _previewBitmap;
    private List<SplitPreset> _splitPresets;
    private string _createdFilename;
    private readonly string _currentPresetsDirectory;
    private readonly string _currentConfigDirectory;
    private readonly string _currentSplitsDirectory;

    private Dictionary<string, Bitmap> _inputPreviewCache = new();
    private Dictionary<string, Bitmap> _inputThumbnailCache = new();
    private Dictionary<string, SKBitmap> _maskSkBitmapCache = new();
    private CancellationTokenSource? _savedNotificationCts;
#if DEBUG
    private TestOutputWindow? _testOutputWindow;
#endif

    public MainWindow()
    {
        InitializeComponent();

        presetComboBoxItems = new ObservableCollection<object>();
        splitsComboBoxItems = [];
        inputImagesComboBoxItems = new ObservableCollection<InputImageItem>();

        _splitPresets = [];
        _createdFilename = "Output preview";

        var rootDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName);

        if (string.IsNullOrEmpty(rootDir))
        {
            throw new InvalidOperationException("Could not locate root directory");
        }

        _currentPresetsDirectory = Path.Combine(rootDir, "presets") + Path.DirectorySeparatorChar;
        _currentSplitsDirectory = Path.Combine(rootDir, "splits") + Path.DirectorySeparatorChar;
        _currentConfigDirectory = Path.Combine(rootDir, "config") + Path.DirectorySeparatorChar;

        Directory.CreateDirectory(_currentPresetsDirectory);
        Directory.CreateDirectory(_currentConfigDirectory);

        OutputCheckerBg.Source = ImageProcessor.CreateCheckerBitmap(320, 240);

        if (!OperatingSystem.IsWindows())
        {
            BtnOpenLiveTester.IsEnabled = false;
            ToolTip.SetTip(BtnOpenLiveTester, "Live tester is only available on Windows");
        }
#if !DEBUG
        BtnOpenLiveTester.IsEnabled = false;
        ToolTip.SetTip(BtnOpenLiveTester, "Live tester is disabled in release builds (work in progress)");
#endif

        // Set DataContext last so binding-triggered event handlers fire with all fields initialized
        DataContext = this;

        Title = "AutoMask v" + AutoMaskVersion;

        ComboBoxSelectPreset.ContainerPrepared += (_, e) =>
        {
            if (e.Container is not ComboBoxItem item || e.Index >= _presetDisplayMap.Count)
            {
                return;
            }

            bool isHeader = _presetDisplayMap[e.Index] == null;
            item.IsHitTestVisible = !isHeader;
            item.Focusable = !isHeader;
        };

        // Override the selection-box ContentPresenter's ContentTemplate with a local value so
        // it always shows just the preset name, regardless of what SelectionBoxItemTemplate
        // (which automatically mirrors ItemTemplate) says.  Local values have higher priority
        // than TemplateBindings in Avalonia, so this assignment wins.
        ComboBoxSelectPreset.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<ContentControl>("ContentPresenter") is { } cp)
            {
                cp.ContentTemplate = new FuncDataTemplate<PresetComboItem>((item, _) =>
                {
                    var tb = new TextBlock { Text = item?.PresetName ?? "" };
                    return tb;
                });
            }
        };

        Opened += async (_, _) => await RefreshPresetsAsync();

        Closed += (_, _) => CleanupSavestateTempDirs();
    }

    private static void CleanupSavestateTempDirs()
    {
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(Path.GetTempPath(), "AutoMask_savestates_*"))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private void CheckSavePossible()
    {
        bool hasOutputDir = !string.IsNullOrEmpty(_outputDirectoryPath);
        bool hasPreview = !string.IsNullOrEmpty(_selectedInputImagePath) && !string.IsNullOrEmpty(_alphaImagePath);
        bool saveAllAllowed = hasOutputDir
            && selectedPresetIndex >= 0
            && selectedPresetIndex < _splitPresets.Count
            && _splitPresets[selectedPresetIndex].Splits?.Count == _inputImagePaths?.Count;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            BtnSave.IsEnabled = hasOutputDir;
            BtnSaveAs.IsEnabled = hasPreview;
            BtnSaveAllSplits.IsEnabled = saveAllAllowed;
        });
    }

    private async Task RefreshPresetsAsync()
    {
        var foundPresets = await PresetService.LoadPresetsAsync(_currentPresetsDirectory);

        DebugLog($"Found {foundPresets.Count} presets:");
        foreach (var preset in foundPresets)
        {
            DebugLog(Path.Combine(preset.PresetFolder!, "preset.json"));
        }

        // Sort by game name then preset name so grouping is stable
        var sortedPresets = foundPresets
            .OrderBy(p => p.GameName ?? "")
            .ThenBy(p => p.PresetName ?? "")
            .ToList();

        if (_splitPresets.Count > 0 && sortedPresets.SequenceEqual(_splitPresets))
        {
            DebugLog("No changes were detected");
            return;
        }

        _splitPresets = sortedPresets;
        presetComboBoxItems.Clear();
        _presetDisplayMap.Clear();
        selectedPresetIndex = -1;
        selectedSplitIndex = 0;

        List<string> invalidPresetFolders = [];
        string? currentGame = null;

        for (int i = 0; i < _splitPresets.Count; i++)
        {
            var preset = _splitPresets[i];

            if (preset.Splits == null || preset.Splits.Count == 0)
            {
                invalidPresetFolders.Add(preset.PresetFolder ?? "(unknown)");
                continue;
            }

            if (preset.GameName != currentGame)
            {
                currentGame = preset.GameName;
                presetComboBoxItems.Add(new PresetGroupHeader(currentGame ?? ""));
                _presetDisplayMap.Add(null);
            }

            int splitCount = preset.Splits.Count(s =>
                !s.Dummy &&
                s.Name != "start_auto_splitter" &&
                s.Name != "reset");
            presetComboBoxItems.Add(new PresetComboItem(preset.PresetName ?? "", i, splitCount, preset.Splits.Count));
            _presetDisplayMap.Add(i);

            DebugLog($"Found preset: {preset.PresetName}");

            for (int j = 0; j < preset.Splits.Count; ++j)
            {
                var cur = preset.Splits[j];
                DebugLog($"{j + 1}. {cur.Name}, threshold: {cur.Threshold}, filename: {preset.PresetFolder}");
            }
        }

        if (invalidPresetFolders.Count > 0)
        {
            await ShowMessage("Warning", $"Invalid preset format found in: {string.Join(", ", invalidPresetFolders)}");
        }

        // Select the first actual preset (skip the leading group header)
        int firstPreset = _presetDisplayMap.FindIndex(m => m != null);
        if (firstPreset >= 0)
        {
            ComboBoxSelectPreset.SelectedIndex = firstPreset;
        }

        CheckSavePossible();
        UpdateSavestateStatus();
    }

    private async void ComboBoxSelectPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int displayIdx = ComboBoxSelectPreset.SelectedIndex;
        if (displayIdx < 0 || displayIdx >= _presetDisplayMap.Count)
        {
            return;
        }

        int? dataIdx = _presetDisplayMap[displayIdx];
        if (dataIdx == null)
        {
            // Group header — not selectable, nothing to do
            return;
        }

        selectedPresetIndex = dataIdx.Value;

        if (selectedPresetIndex < 0 || selectedPresetIndex >= _splitPresets.Count)
        {
            return;
        }

        var preset = _splitPresets[selectedPresetIndex];

        if (preset is { Splits: not null, PresetFolder: not null })
        {
            foreach (var b in _maskSkBitmapCache.Values)
            {
                b.Dispose();
            }

            var paths = preset.Splits
                .Select(s => Path.Combine(preset.PresetFolder, s.Mask))
                .Distinct()
                .ToList();
            _maskSkBitmapCache = await Task.Run(() => paths
                .Select(p => (Key: p, Value: SKBitmap.Decode(p)))
                .Where(x => x.Value is not null)
                .ToDictionary(x => x.Key, x => x.Value!));
        }

        splitsComboBoxItems.Clear();

        foreach (var split in preset.Splits!)
        {
            splitsComboBoxItems.Add(new ComboBoxItem { Content = split.Name });
        }

        ComboBoxSelectSplit.SelectedIndex = 0;
        UpdateNavigationButtons();
        CheckSavePossible();
        UpdateSavestateStatus();
    }

    private void UpdateSavestateStatus()
    {
        bool hasSavestates = selectedPresetIndex >= 0
            && selectedPresetIndex < _splitPresets.Count
            && _splitPresets[selectedPresetIndex].Splits?.Any(s => !string.IsNullOrEmpty(s.Savestate)) == true;

        bool hasInstructions = selectedPresetIndex >= 0
            && selectedPresetIndex < _splitPresets.Count
            && _splitPresets[selectedPresetIndex].Splits?.Any(s =>
                !string.IsNullOrEmpty(s.Savestate) && !string.IsNullOrEmpty(s.SavestateInstructions)) == true;

        SavestateStatusLabel.Text = hasSavestates ? "Savestates available" : "No savestates";
        BtnCopySavestates.IsEnabled = hasSavestates;
        BtnShowSavestateInstructions.IsEnabled = hasInstructions;
    }

    private void BtnShowSavestateInstructions_Click(object sender, RoutedEventArgs e)
    {
        if (selectedPresetIndex < 0 || selectedPresetIndex >= _splitPresets.Count)
        {
            return;
        }

        var preset = _splitPresets[selectedPresetIndex];
        if (preset.Splits is null)
        {
            return;
        }

        var window = new SavestateInstructionsWindow(preset);
        window.Show(this);
    }

    private async void BtnCopySavestates_Click(object sender, RoutedEventArgs e)
    {
        if (selectedPresetIndex < 0 || selectedPresetIndex >= _splitPresets.Count)
        {
            return;
        }

        var preset = _splitPresets[selectedPresetIndex];
        if (preset.Splits is null || preset.PresetFolder is null)
        {
            return;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), $"AutoMask_savestates_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        int padWidth = preset.Splits.Count.ToString().Length;
        var copiedPaths = new List<string>();

        for (int i = 0; i < preset.Splits.Count; i++)
        {
            var split = preset.Splits[i];
            if (string.IsNullOrEmpty(split.Savestate))
            {
                continue;
            }

            string src = Path.Combine(preset.PresetFolder, split.Savestate);
            if (!File.Exists(src))
            {
                continue;
            }

            string ext = Path.GetExtension(src);
            string instructionsSuffix = string.IsNullOrEmpty(split.SavestateInstructions) ? "" : "_SEE_INSTRUCTIONS";
            string destName = $"{i.ToString().PadLeft(padWidth, '0')}_{split.Name}{instructionsSuffix}{ext}";
            string destPath = Path.Combine(tempDir, destName);
            File.Copy(src, destPath, overwrite: true);
            copiedPaths.Add(destPath);
        }

        if (copiedPaths.Count == 0)
        {
            return;
        }

        if (Clipboard is null)
        {
            return;
        }

        var data = new DataTransfer();
        int addedCount = 0;
        foreach (string path in copiedPaths)
        {
            var file = await StorageProvider.TryGetFileFromPathAsync(new Uri(path));
            if (file is null)
            {
                continue;
            }

            data.Add(DataTransferItem.Create(DataFormat.File, file));
            addedCount++;
        }

        if (addedCount == 0)
        {
            return;
        }

        await Clipboard.SetDataAsync(data);

        ShowStatus($"Copied {addedCount} savestate(s) to clipboard");
    }

    private async void BtnSelectOutputDirectory_Click(object sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select output directory"
        });

        if (folders.Count > 0)
        {
            _outputDirectoryPath = folders[0].Path.LocalPath;
            OutputDirectoryTextBox.Text = _outputDirectoryPath;
            CheckSavePossible();
        }
    }

    private void BtnOpenPresetsFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenInFileManager(_currentPresetsDirectory);
    }

    private async Task UpdateOutputPreview()
    {
        ImageSavedLabel.Opacity = 0;

        if (string.IsNullOrEmpty(_selectedInputImagePath) || string.IsNullOrEmpty(_alphaImagePath))
        {
            OutputImageView.Source = null;
            return;
        }

        OutputLoadingText.Text = "Loading...";
        OutputLoadingText.Foreground = Avalonia.Media.Brushes.White;
        OutputLoadingOverlay.IsVisible = true;

        if (!File.Exists(_selectedInputImagePath))
        {
            OutputImageView.Source = null;
            ShowOutputError("Failed to load image");
            UpdateNavigationButtons();
            await ShowMessage("Error", "Specified input image not found!", detail: _selectedInputImagePath);
            return;
        }

        if (!File.Exists(_alphaImagePath))
        {
            OutputImageView.Source = null;
            ShowOutputError("Failed to load image");
            UpdateNavigationButtons();
            await ShowMessage("Error", "Specified mask image not found!", detail: _alphaImagePath);
            return;
        }

        _maskedImage?.Dispose();
        _maskedImage = await Task.Run(() => ImageProcessor.ApplyScaledAlphaChannel(_selectedInputImagePath!, _alphaImagePath!, _maskSkBitmapCache));

        _previewBitmap?.Dispose();
        _previewBitmap = await Task.Run(() =>
        {
            using var scaled = _maskedImage!.Resize(new SKImageInfo(320, 240), new SKSamplingOptions(SKFilterMode.Nearest));
            return ImageProcessor.ToAvaloniaBitmap(scaled);
        });
        OutputImageView.Source = _previewBitmap;
        OutputLoadingOverlay.IsVisible = false;
        UpdateNavigationButtons();

        _createdFilename = CreateCurrentFilename();
        PreviewImageLabel.Text = _createdFilename;

#if DEBUG
        if (_testOutputWindow is not null && OperatingSystem.IsWindows())
        {
            SplitPreset? preset = selectedPresetIndex >= 0 && selectedPresetIndex < _splitPresets.Count
                ? _splitPresets[selectedPresetIndex]
                : null;
            _testOutputWindow.UpdateFromMainWindow(preset, selectedSplitIndex, _selectedInputImagePath, _maskSkBitmapCache);
        }
#endif
    }

    private async void BtnLoadInputImages_Click(object sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select base image",
            FileTypeFilter =
            [
                new FilePickerFileType("PNG Files") { Patterns = ["*.png"] },
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        BtnSaveAllSplits.IsEnabled = false;

        _inputImagePaths = [..files.Select(f => f.Path.LocalPath)];

        foreach (var b in _inputPreviewCache.Values)
        {
            b.Dispose();
        }

        foreach (var b in _inputThumbnailCache.Values)
        {
            b.Dispose();
        }

        InputLoadingOverlay.IsVisible = true;
        var (previewCache, thumbCache) = await Task.Run(() =>
        {
            var previews = new ConcurrentDictionary<string, Bitmap>();
            var thumbs = new ConcurrentDictionary<string, Bitmap>();
            Parallel.ForEach(_inputImagePaths!, path =>
            {
                using var sk = SKBitmap.Decode(path);
                using var skPreview = sk.Resize(new SKImageInfo(320, 240), new SKSamplingOptions(SKFilterMode.Nearest));
                previews[path] = ImageProcessor.ToAvaloniaBitmap(skPreview);
                using var skThumb = sk.Resize(new SKImageInfo(32, 24), new SKSamplingOptions(SKFilterMode.Linear));
                thumbs[path] = ImageProcessor.ToAvaloniaBitmap(skThumb);
            });
            return (new Dictionary<string, Bitmap>(previews), new Dictionary<string, Bitmap>(thumbs));
        });
        _inputPreviewCache = previewCache;
        _inputThumbnailCache = thumbCache;
        InputLoadingOverlay.IsVisible = false;

        _selectedInputImagePath = _inputImagePaths[0];
        InputImageLabel.Text = Path.GetFileName(_selectedInputImagePath);
        InputImageView.Source = _inputPreviewCache[_selectedInputImagePath];
        UpdateNavigationButtons();

        inputImagesComboBoxItems.Clear();

        foreach (var path in _inputImagePaths)
        {
            inputImagesComboBoxItems.Add(new InputImageItem(path, Path.GetFileName(path), _inputThumbnailCache[path]));
        }

        ComboBoxSelectInputImage.SelectedIndex = 0;
        CheckSavePossible();
    }

    private async void BtnSaveImageAs_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedInputImagePath) || string.IsNullOrEmpty(_alphaImagePath))
        {
            await ShowMessage("Error", "Please load both the input image and the mask image.");
            return;
        }

        _maskedImage?.Dispose();
        _maskedImage = await Task.Run(() => ImageProcessor.ApplyScaledAlphaChannel(_selectedInputImagePath!, _alphaImagePath!, _maskSkBitmapCache));

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Masked Image",
            SuggestedFileName = _createdFilename,
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG Files") { Patterns = ["*.png"] }]
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            ImageProcessor.SaveBitmapToStream(_maskedImage!, stream);
        }
    }

    private void OutputImageBorder_PointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        OutputCheckerBg.IsVisible = !OutputCheckerBg.IsVisible;
        OutputDarkBg.IsVisible = !OutputDarkBg.IsVisible;
    }

    private void BtnPrevAlphaImage_Click(object sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectSplit.SelectedIndex <= 0)
        {
            return;
        }

        ComboBoxSelectSplit.SelectedIndex -= 1;
    }

    private void BtnNextAlphaImage_Click(object sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectSplit.SelectedIndex >= splitsComboBoxItems.Count - 1)
        {
            return;
        }

        ComboBoxSelectSplit.SelectedIndex += 1;
    }

    private async void BtnAutoSave_Click(object sender, RoutedEventArgs e)
    {
        if (_maskedImage == null)
        {
            await ShowMessage("Error", "Error getting masked output image.");
            return;
        }

        if (string.IsNullOrEmpty(_outputDirectoryPath))
        {
            await ShowMessage("Error", "Please select an output directory.");
            return;
        }

        if (!Directory.Exists(_outputDirectoryPath))
        {
            await ShowMessage("Error", "Specified directory not found!");
            return;
        }

        var outputPath = Path.Combine(_outputDirectoryPath, _createdFilename);

        if (File.Exists(outputPath))
        {
            var result = await MessageBox.Show(this, "Alert", "File already exists! Do you want to overwrite it?", MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        ImageProcessor.SaveBitmapToPath(_maskedImage!, outputPath);
        ShowStatus($"Saved {_createdFilename}");
    }

    private async void BtnSaveAllSplits_Click(object sender, RoutedEventArgs e)
    {
        var preset = _splitPresets[selectedPresetIndex];
        var maskCache = _maskSkBitmapCache;
        var inputPaths = _inputImagePaths!;
        var outDir = _outputDirectoryPath!;

        await Task.Run(() =>
        {
            Parallel.For(0, inputPaths.Count, i =>
            {
                var maskPath = Path.Combine(preset.PresetFolder!, preset.Splits![i].Mask);
                using var result = ImageProcessor.ApplyScaledAlphaChannel(inputPaths[i], maskPath, maskCache);
                var filename = PresetService.CreateFilenameForSplit(preset, i);
                ImageProcessor.SaveBitmapToPath(result, Path.Combine(outDir, filename));
            });
        });

        ShowStatus($"Saved {inputPaths.Count} images");
    }

    private async void ShowStatus(string text)
    {
        _savedNotificationCts?.Cancel();
        _savedNotificationCts = new CancellationTokenSource();
        var token = _savedNotificationCts.Token;

        ImageSavedLabel.Text = text;
        ImageSavedLabel.Opacity = 1;

        try
        {
            await Task.Delay(3000, token);
            ImageSavedLabel.Opacity = 0;
        }
        catch (OperationCanceledException) { }
    }

    private string CreateCurrentFilename()
    {
        if (string.IsNullOrEmpty(_selectedInputImagePath) || string.IsNullOrEmpty(_alphaImagePath))
        {
            return "";
        }

        if (selectedPresetIndex == -1)
        {
            var name = Path.GetFileNameWithoutExtension(_selectedInputImagePath);
            DebugLog(name);
            return name + "_masked.png";
        }

        var currentPreset = _splitPresets[selectedPresetIndex];

        if (currentPreset.Splits == null)
        {
            LogError("Error loading selected split!");
            return "";
        }

        return PresetService.CreateFilenameForSplit(currentPreset, selectedSplitIndex);
    }

    private async void ComboBoxSelectSplit_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (presetComboBoxItems.Count == 0)
        {
            ComboBoxSelectPreset.SelectedIndex = -1;
            return;
        }

        if (ComboBoxSelectSplit.SelectedIndex == -1)
        {
            _alphaImagePath = "";
            await UpdateOutputPreview();
            return;
        }

        var currentPreset = _splitPresets[selectedPresetIndex];

        if (currentPreset.Splits == null)
        {
            await ShowMessage("Error", "Error loading selected split!");
            _alphaImagePath = "";
            await UpdateOutputPreview();
            return;
        }

        _alphaImagePath = Path.Combine(currentPreset.PresetFolder!, currentPreset.Splits[selectedSplitIndex].Mask);
        DebugLog(_alphaImagePath);
        await UpdateOutputPreview();
    }

    private void BtnPrevInputImage_Click(object sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectInputImage.SelectedIndex <= 0)
        {
            return;
        }

        ComboBoxSelectInputImage.SelectedIndex -= 1;
    }

    private void BtnNextInputImage_Click(object sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectInputImage.SelectedIndex >= inputImagesComboBoxItems.Count - 1)
        {
            return;
        }

        ComboBoxSelectInputImage.SelectedIndex += 1;
    }

    private async void ComboBoxSelectInputImage_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboBoxSelectInputImage.SelectedIndex == -1)
        {
            return;
        }

        if (_inputImagePaths == null)
        {
            await ShowMessage("Error", "No input images loaded!");
            await UpdateOutputPreview();
            return;
        }

        _selectedInputImagePath = _inputImagePaths[ComboBoxSelectInputImage.SelectedIndex];
        InputImageLabel.Text = Path.GetFileName(_selectedInputImagePath);
        InputImageView.Source = _inputPreviewCache[_selectedInputImagePath];
        UpdateNavigationButtons();
        await UpdateOutputPreview();
    }

    private async void BtnShowOutput_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_outputDirectoryPath))
        {
            await ShowMessage("Error", "Please select an output directory below.");
            return;
        }

        OpenInFileManager(_outputDirectoryPath);
    }

    private async void BtnOpenPresetEditor_Click(object sender, RoutedEventArgs e)
    {
        var editorWindow = new PresetEditor(_splitPresets, _currentPresetsDirectory, _currentSplitsDirectory);
        await editorWindow.ShowDialog(this);
        if (editorWindow.PresetsModified)
        {
            await RefreshPresetsAsync();
        }
    }

    private void BtnOpenTestOutput_Click(object? sender, RoutedEventArgs e)
    {
#if DEBUG
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_testOutputWindow is not null)
        {
            _testOutputWindow.Activate();
            return;
        }

        SplitPreset? preset = selectedPresetIndex >= 0 && selectedPresetIndex < _splitPresets.Count
            ? _splitPresets[selectedPresetIndex]
            : null;

        var prefsPath = Path.Combine(_currentConfigDirectory, "capture-prefs.json");
        var win = new TestOutputWindow();
        win.InitializeFromMainWindow(preset, selectedSplitIndex, _selectedInputImagePath, _maskSkBitmapCache, prefsPath);
        win.Closed += (_, _) => _testOutputWindow = null;
        _testOutputWindow = win;
        win.Show(this);
#endif
    }

    private void UpdateNavigationButtons()
    {
        int inputIndex = ComboBoxSelectInputImage.SelectedIndex;
        int inputCount = inputImagesComboBoxItems.Count;
        ComboBoxSelectInputImage.IsEnabled = inputCount > 0;
        BtnInputImagePrev.IsEnabled = inputIndex > 0;
        BtnInputImageNext.IsEnabled = inputIndex >= 0 && inputIndex < inputCount - 1;

        int splitIndex = ComboBoxSelectSplit.SelectedIndex;
        int splitCount = splitsComboBoxItems.Count;
        BtnPrevAlphaImage.IsEnabled = splitIndex > 0;
        BtnNextAlphaImage.IsEnabled = splitIndex >= 0 && splitIndex < splitCount - 1;
    }

    private void ShowOutputError(string message)
    {
        OutputLoadingText.Text = message;
        OutputLoadingText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CC4444"));
    }

    private async Task ShowMessage(string title, string message, string? detail = null)
    {
        var fullMessage = detail is null ? message : $"{message}\n\n{detail}";
        await MessageBox.Show(this, title, fullMessage);
    }
}
