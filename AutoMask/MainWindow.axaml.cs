using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MessageBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsgBoxIcon = MsBox.Avalonia.Enums.Icon;
using SkiaSharp;

namespace AutoSplit_AutoMask;

using static Utils;

public record InputImageItem(string FullPath, string FileName, Bitmap Thumbnail);

public partial class MainWindow : Window
{
    public ObservableCollection<ComboBoxItem> presetComboBoxItems { get; set; }
    public ObservableCollection<ComboBoxItem> splitsComboBoxItems { get; set; }
    public ObservableCollection<InputImageItem> inputImagesComboBoxItems { get; set; }
    public int selectedPresetIndex { get; set; }
    public int selectedSplitIndex { get; set; }

    private List<string>? inputImagePaths;
    private string? selectedInputImagePath;
    private string? outputDirectoryPath;
    private string? alphaImagePath;
    private SKBitmap? maskedImage;
    private Bitmap? previewBitmap;
    private List<SplitPreset> splitPresets;
    private string createdFilename;
    private string currentPresetsDirectory;

    private Dictionary<string, Bitmap> _inputBitmapCache = new();
    private Dictionary<string, Bitmap> _inputThumbnailCache = new();
    private Dictionary<string, SKBitmap> _maskSkBitmapCache = new();

    public MainWindow()
    {
        InitializeComponent();

        presetComboBoxItems = [];
        splitsComboBoxItems = [];
        inputImagesComboBoxItems = new ObservableCollection<InputImageItem>();

        splitPresets = [];
        createdFilename = "Output preview";

        var rootDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName);

        if (string.IsNullOrEmpty(rootDir))
        {
            throw new InvalidOperationException("Could not locate root directory");
        }

        currentPresetsDirectory = Path.Combine(rootDir, "presets") + Path.DirectorySeparatorChar;

        Directory.CreateDirectory(currentPresetsDirectory);

        OutputCheckerBg.Source = CreateCheckerBitmap(384, 288);

        // Set DataContext last so binding-triggered event handlers fire with all fields initialised.
        DataContext = this;

        Title = "AutoMask v" + AutoMaskSemVer + (string.IsNullOrEmpty(VersionSuffix) ? string.Empty : "-" + VersionSuffix);

        Opened += async (_, _) => await RefreshPresetsAsync();
    }

    private async Task RefreshPresetsAsync()
    {
        var presetPaths = Directory.EnumerateDirectories(currentPresetsDirectory)
            .Where(dir => Directory.EnumerateFiles(dir, "preset.json", SearchOption.TopDirectoryOnly).Any());

        Log($"Found {presetPaths.Count()} presets:");

        foreach (var preset in presetPaths)
        {
            Log(Path.Combine(preset, "preset.json"));
        }

        List<SplitPreset> foundPresets = [];

        foreach (string presetPath in presetPaths)
        {
            SplitPreset? preset = JsonSerializer.Deserialize<SplitPreset>(
                File.ReadAllText(Path.Combine(presetPath, "preset.json")));

            if (preset is not null)
            {
                preset.PresetFolder = presetPath;
                Log($"Adding preset: {preset.PresetFolder} from {Path.Combine(presetPath, "preset.json")}");
                foundPresets.Add(preset);
            }
        }

        if (splitPresets.Count > 0 && foundPresets.SequenceEqual(splitPresets))
        {
            Log("No changes were detected");
            return;
        }


        splitPresets = foundPresets;
        presetComboBoxItems.Clear();
        selectedPresetIndex = -1;
        selectedSplitIndex = 0;

        List<string> invalidPresetFolders = [];

        foreach (SplitPreset preset in foundPresets)
        {
            if (preset.Splits == null || preset.Splits.Count == 0)
            {
                invalidPresetFolders.Add(preset.PresetFolder ?? "(unknown)");
                continue;
            }

            presetComboBoxItems.Add(new ComboBoxItem { Content = preset.PresetName });
            Log($"Found preset: {preset.PresetName}");

            for (int i = 0; i < preset.Splits.Count; ++i)
            {
                var cur = preset.Splits[i];
                Log($"{i + 1}. {cur.Name}, threshold: {cur.Threshold}, filename: {preset.PresetFolder}, enabled: {cur.Enabled}");
            }
        }

        if (invalidPresetFolders.Count > 0)
        {
            await ShowMessage("Warning", $"Invalid preset format found in: {string.Join(", ", invalidPresetFolders)}", MsgBoxIcon.Warning);
        }

        if (presetComboBoxItems.Count > 0)
        {
            ComboBoxSelectPreset.SelectedIndex = 0;
        }
    }

    private async void BtnRefreshPresets_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPresetsAsync();
    }

    private async void ComboBoxSelectPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectedPresetIndex < 0 || selectedPresetIndex >= splitPresets.Count)
        {
            return;
        }

        var preset = splitPresets[selectedPresetIndex];

        if (preset.Splits != null && preset.PresetFolder != null)
        {
            foreach (var b in _maskSkBitmapCache.Values)
            {
                b.Dispose();
            }

            var paths = preset.Splits
                .Select(s => Path.Combine(preset.PresetFolder, s.MaskImagePath))
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
    }

    private async void BtnSelectOutputDirectory_Click(object sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select output directory"
        });

        if (folders.Count > 0)
        {
            outputDirectoryPath = folders[0].Path.LocalPath;
            OutputDirectoryTextBox.Text = outputDirectoryPath;
        }
    }

    private void BtnOpenPresetsFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenInFileManager(currentPresetsDirectory);
    }

    private async Task UpdateOutputPreview()
    {
        ImageSavedLabel.Opacity = 0;

        if (string.IsNullOrEmpty(selectedInputImagePath) || string.IsNullOrEmpty(alphaImagePath))
        {
            OutputImageView.Source = null;
            return;
        }

        OutputLoadingOverlay.IsVisible = true;

        if (!File.Exists(selectedInputImagePath))
        {
            await ShowMessage("Error", "Specified input image not found!", detail: selectedInputImagePath);
            return;
        }

        if (!File.Exists(alphaImagePath))
        {
            await ShowMessage("Error", "Specified mask image not found!", detail: alphaImagePath);
            return;
        }

        maskedImage?.Dispose();
        maskedImage = await Task.Run(ApplyScaledAlphaChannel);

        previewBitmap?.Dispose();
        previewBitmap = await Task.Run(() => ToAvaloniaBitmap(maskedImage));
        OutputImageView.Source = previewBitmap;
        OutputLoadingOverlay.IsVisible = false;
        UpdateNavigationButtons();

        createdFilename = CreateCurrentFilename();
        PreviewImageLabel.Text = createdFilename;
    }

    private async void BtnLoadInputImages_Click(object sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select Input Image",
            FileTypeFilter =
            [
                new FilePickerFileType("Image Files") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        inputImagePaths = [..files.Select(f => f.Path.LocalPath)];

        foreach (var b in _inputBitmapCache.Values)
        {
            b.Dispose();
        }

        foreach (var b in _inputThumbnailCache.Values)
        {
            b.Dispose();
        }

        InputLoadingOverlay.IsVisible = true;
        var (fullCache, thumbCache) = await Task.Run(() =>
        {
            var full = new Dictionary<string, Bitmap>();
            var thumbs = new Dictionary<string, Bitmap>();
            foreach (var path in inputImagePaths!)
            {
                using var sk = SKBitmap.Decode(path);
                full[path] = ToAvaloniaBitmap(sk);
                using var skThumb = sk.Resize(new SKImageInfo(32, 24), new SKSamplingOptions(SKFilterMode.Linear));
                thumbs[path] = ToAvaloniaBitmap(skThumb);
            }
            return (full, thumbs);
        });
        _inputBitmapCache = fullCache;
        _inputThumbnailCache = thumbCache;
        InputLoadingOverlay.IsVisible = false;

        selectedInputImagePath = inputImagePaths[0];
        InputImageLabel.Text = Path.GetFileName(selectedInputImagePath);
        InputImageView.Source = _inputBitmapCache[selectedInputImagePath];
        UpdateNavigationButtons();

        inputImagesComboBoxItems.Clear();

        foreach (var path in inputImagePaths)
        {
            inputImagesComboBoxItems.Add(new InputImageItem(path, Path.GetFileName(path), _inputThumbnailCache[path]));
        }

        ComboBoxSelectInputImage.SelectedIndex = 0;
    }

    private async void BtnLoadAlphaImage_Click(object sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Mask (Alpha Channel)",
            FileTypeFilter = [new FilePickerFileType("PNG Files") { Patterns = ["*.png"] }]
        });

        if (files.Count > 0)
        {
            alphaImagePath = files[0].Path.LocalPath;
            await UpdateOutputPreview();
        }
    }

    private async void BtnSaveImageAs_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(selectedInputImagePath) || string.IsNullOrEmpty(alphaImagePath))
        {
            await ShowMessage("Error", "Please load both the input image and the mask image.", MsgBoxIcon.Error);
            return;
        }

        maskedImage?.Dispose();
        maskedImage = await Task.Run(ApplyScaledAlphaChannel);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Masked Image",
            SuggestedFileName = createdFilename,
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG Files") { Patterns = ["*.png"] }]
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            SaveMaskedImageToStream(stream);
        }
    }

    private void OutputImageBorder_PointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        OutputCheckerBg.IsVisible = !OutputCheckerBg.IsVisible;
        OutputDarkBg.IsVisible = !OutputDarkBg.IsVisible;
    }

    private void BtnPrevAlphaImage_Click(object sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectSplit.SelectedIndex == splitsComboBoxItems.Count - 1)
        {
            return;
        }

        ComboBoxSelectSplit.SelectedIndex += 1;
    }

    private void BtnNextAlphaImage_Click(object sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectSplit.SelectedIndex == 0)
        {
            return;
        }

        ComboBoxSelectSplit.SelectedIndex -= 1;
    }

    private SKBitmap ApplyScaledAlphaChannel()
    {
        using var inputBitmap = SKBitmap.Decode(selectedInputImagePath);

        bool ownAlpha = !_maskSkBitmapCache.TryGetValue(alphaImagePath!, out var alphaBitmap);
        alphaBitmap ??= SKBitmap.Decode(alphaImagePath);

        using var scaledAlpha = alphaBitmap!.Resize(
            new SKImageInfo(inputBitmap.Width, inputBitmap.Height),
            new SKSamplingOptions(SKFilterMode.Linear))!;

        int width = inputBitmap.Width;
        int height = inputBitmap.Height;
        var outputBitmap = new SKBitmap(width, height);

        SKColor[] inputPixels = inputBitmap.Pixels;
        SKColor[] alphaPixels = scaledAlpha.Pixels;
        SKColor[] outputPixels = new SKColor[width * height];

        Parallel.For(0, height, y =>
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                var src = inputPixels[i];
                outputPixels[i] = alphaPixels[i].Alpha == 255
                    ? new SKColor(src.Red, src.Green, src.Blue)
                    : SKColors.Transparent;
            }
        });

        outputBitmap.Pixels = outputPixels;

        if (ownAlpha)
        {
            alphaBitmap.Dispose();
        }
        return outputBitmap;
    }

    private static Bitmap CreateCheckerBitmap(int width, int height)
    {
        const int tileSize = 8;
        var light = new SKColor(0xBB, 0xBB, 0xBB);
        var dark  = new SKColor(0x88, 0x88, 0x88);
        using var skBitmap = new SKBitmap(width, height);
        var pixels = new SKColor[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = ((x / tileSize + y / tileSize) & 1) == 0 ? light : dark;
            }
        }
        skBitmap.Pixels = pixels;
        return ToAvaloniaBitmap(skBitmap);
    }

    private static Bitmap ToAvaloniaBitmap(SKBitmap skBitmap)
    {
        using var skImage = SKImage.FromBitmap(skBitmap);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        var stream = new MemoryStream();
        encoded.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    private void SaveMaskedImageToStream(Stream stream)
    {
        using var skImage = SKImage.FromBitmap(maskedImage!);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        encoded.SaveTo(stream);
    }

    private void SaveMaskedImageToPath(string path)
    {
        using var stream = File.OpenWrite(path);
        SaveMaskedImageToStream(stream);
    }

    private async void BtnAutoSave_Click(object sender, RoutedEventArgs e)
    {
        if (maskedImage == null)
        {
            await ShowMessage("Error", "Error getting masked output image.", MsgBoxIcon.Error);
            return;
        }

        if (string.IsNullOrEmpty(outputDirectoryPath))
        {
            await ShowMessage("Error", "Please select an output directory.", MsgBoxIcon.Error);
            return;
        }

        if (!Directory.Exists(outputDirectoryPath))
        {
            await ShowMessage("Error", "Specified directory not found!", MsgBoxIcon.Error);
            return;
        }

        var outputPath = Path.Combine(outputDirectoryPath, createdFilename);

        if (File.Exists(outputPath))
        {
            var result = await MessageBoxManager
                .GetMessageBoxStandard("Alert", "File already exists! Do you want to overwrite it?", ButtonEnum.YesNo)
                .ShowWindowDialogAsync(this);

            if (result != ButtonResult.Yes)
            {
                return;
            }
        }

        SaveMaskedImageToPath(outputPath);
        ImageSavedLabel.Opacity = 1;
    }

    private string CreateCurrentFilename()
    {
        if (string.IsNullOrEmpty(selectedInputImagePath) || string.IsNullOrEmpty(alphaImagePath))
        {
            return "";
        }

        if (selectedPresetIndex == -1)
        {
            var name = Path.GetFileNameWithoutExtension(selectedInputImagePath);
            Log(name);
            return name + "_masked.png";
        }

        var currentPreset = splitPresets[selectedPresetIndex];

        if (currentPreset.Splits == null)
        {
            LogError("Error loading selected split!");
            return "";
        }

        var currentSplit = currentPreset.Splits[selectedSplitIndex];
        int totalSplits = currentPreset.Splits.Count;

        string prefix = currentSplit.Name switch
        {
            "reset" => "reset",
            "start_auto_splitter" => "start_auto_splitter",
            _ => $"{selectedSplitIndex.ToString().PadLeft(totalSplits.ToString().Length, '0')}_{currentSplit.Name}"
        };

        string output = $"{prefix}_({currentSplit.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)})";

        if (!(Math.Abs(currentSplit.PauseTime - 3.0) < 0.01f))
        {
            output += $"_[{currentSplit.PauseTime.ToString(System.Globalization.CultureInfo.InvariantCulture)}]";
        }

        if (currentSplit.SplitDelay > 0)
        {
            output += $"_#{currentSplit.SplitDelay}#";
        }

        if (currentSplit.Dummy)
        {
            output += "_{d}";
        }

        if (currentSplit.Inverted)
        {
            output += "_{b}";
        }

        return output + ".png";
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
            alphaImagePath = "";
            await UpdateOutputPreview();
            return;
        }

        var currentPreset = splitPresets[selectedPresetIndex];

        if (currentPreset.Splits == null)
        {
            await ShowMessage("Error", "Error loading selected split!", MsgBoxIcon.Error);
            alphaImagePath = "";
            await UpdateOutputPreview();
            return;
        }

        alphaImagePath = Path.Combine(currentPreset.PresetFolder!, currentPreset.Splits[selectedSplitIndex].MaskImagePath);
        Log(alphaImagePath);
        await UpdateOutputPreview();
    }

    private void BtnPrevInputImage_Click(object sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectInputImage.SelectedIndex == splitsComboBoxItems.Count - 1)
        {
            return;
        }

        ComboBoxSelectInputImage.SelectedIndex += 1;
    }

    private void BtnNextInputImage_Click(object sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectInputImage.SelectedIndex == 0)
        {
            return;
        }

        ComboBoxSelectInputImage.SelectedIndex -= 1;
    }

    private async void ComboBoxSelectInputImage_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboBoxSelectInputImage.SelectedIndex == -1)
        {
            return;
        }

        if (inputImagePaths == null)
        {
            await ShowMessage("Error", "No input images loaded!", MsgBoxIcon.Error);
            await UpdateOutputPreview();
            return;
        }

        selectedInputImagePath = inputImagePaths[ComboBoxSelectInputImage.SelectedIndex];
        InputImageLabel.Text = Path.GetFileName(selectedInputImagePath);
        InputImageView.Source = _inputBitmapCache[selectedInputImagePath];
        UpdateNavigationButtons();
        await UpdateOutputPreview();
    }

    private async void BtnShowOutput_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(outputDirectoryPath))
        {
            await ShowMessage("Error", "Please select an output directory below.", MsgBoxIcon.Error);
            return;
        }

        OpenInFileManager(outputDirectoryPath);
    }

    private async void BtnOpenPresetEditor_Click(object sender, RoutedEventArgs e)
    {
        var editorWindow = new PresetEditor(splitPresets);
        await editorWindow.ShowDialog(this);
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void Log(string message) => System.Console.WriteLine(message);

    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogError(string message) => System.Console.Error.WriteLine(message);

    private void UpdateNavigationButtons()
    {
        int inputIndex = ComboBoxSelectInputImage.SelectedIndex;
        int inputCount = inputImagesComboBoxItems.Count;
        BtnInputImagePrev.IsEnabled = inputIndex > 0;
        BtnInputImageNext.IsEnabled = inputIndex >= 0 && inputIndex < inputCount - 1;

        int splitIndex = ComboBoxSelectSplit.SelectedIndex;
        int splitCount = splitsComboBoxItems.Count;
        BtnPrevAlphaImage.IsEnabled = splitIndex > 0;
        BtnNextAlphaImage.IsEnabled = splitIndex >= 0 && splitIndex < splitCount - 1;
    }

    private static void OpenInFileManager(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start("explorer.exe", $"\"{path}\"");
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", path);
        }
        else if (OperatingSystem.IsLinux())
        {
            Process.Start("xdg-open", path);
        }
    }

    private async Task ShowMessage(string title, string message, Icon icon = MsgBoxIcon.None, string? detail = null)
    {
        var fullMessage = detail is null ? message : $"{message}\n\n{detail}";
        await MessageBoxManager
            .GetMessageBoxStandard(title, fullMessage, ButtonEnum.Ok, icon)
            .ShowWindowDialogAsync(this);
    }
}
