using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace AutoSplit_AutoMask;

public partial class AddPremadeSplitDialog : Window
{
    // Characters forbidden in split names by the AutoSplit file naming spec (same as the editor).
    [GeneratedRegex(@"[#@{}\(\)\[\]\^]")]
    private static partial Regex InvalidNameCharsRegex();

    private readonly List<PremadeSplitsFile> _existing;
    private readonly string _splitsDirectory;

    private string? _maskPath;
    private string? _basePath;
    private Bitmap? _maskPreview;
    private Bitmap? _basePreview;
    private bool _suppressEvents;

    public bool Saved { get; private set; }

    public AddPremadeSplitDialog(List<PremadeSplitsFile> existing, string splitsDirectory)
    {
        InitializeComponent();
        _existing = existing;
        _splitsDirectory = splitsDirectory;

        GameBox.ItemsSource = existing
            .Where(f => !string.IsNullOrWhiteSpace(f.GameName))
            .Select(f => f.GameName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _suppressEvents = true;
        ThresholdBox.Text = "0.95";
        ThresholdSlider.Value = 0.95;
        PauseBox.Text = "3";
        DelayBox.Text = "0";
        _suppressEvents = false;

        UpdateSaveState();
    }

    private void RefreshSectionSuggestions()
    {
        string game = GameBox.Text ?? "";
        SectionBox.ItemsSource = _existing
            .Where(f => f.SectionName != null
                        && string.Equals(f.GameName, game, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.SectionName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void GameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshSectionSuggestions();
        UpdateSaveState();
    }

    private void SectionBox_TextChanged(object? sender, TextChangedEventArgs e) => UpdateSaveState();

    private void NameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        bool invalid = string.IsNullOrWhiteSpace(NameBox.Text) || InvalidNameCharsRegex().IsMatch(NameBox.Text);
        SetInvalid(NameBox, invalid && !string.IsNullOrEmpty(NameBox.Text));
        UpdateSaveState();
    }

    private void NumericToggle_Changed(object? sender, RoutedEventArgs e) => UpdateSaveState();

    private void ThresholdBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (TryParseThreshold(out double value))
        {
            _suppressEvents = true;
            ThresholdSlider.Value = value;
            _suppressEvents = false;
            SetInvalid(ThresholdBox, false);
        }
        else
        {
            SetInvalid(ThresholdBox, true);
        }
        UpdateSaveState();
    }

    private void ThresholdSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _suppressEvents = true;
        ThresholdBox.Text = Math.Round(e.NewValue, 2).ToString("0.##", CultureInfo.InvariantCulture);
        _suppressEvents = false;
        SetInvalid(ThresholdBox, false);
        UpdateSaveState();
    }

    private void PauseBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        SetInvalid(PauseBox, !TryParsePause(out _));
        UpdateSaveState();
    }

    private void DelayBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        SetInvalid(DelayBox, !TryParseDelay(out _));
        UpdateSaveState();
    }

    private bool TryParseThreshold(out double value)
    {
        value = 0;
        if (ThresholdEnabledCheck.IsChecked != true)
        {
            return true;
        }
        return double.TryParse(ThresholdBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && value is >= 0 and <= 1;
    }

    private bool TryParsePause(out double value)
    {
        value = 0;
        if (PauseEnabledCheck.IsChecked != true)
        {
            return true;
        }
        return double.TryParse(PauseBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private bool TryParseDelay(out int value)
    {
        value = 0;
        if (DelayEnabledCheck.IsChecked != true)
        {
            return true;
        }
        return int.TryParse(DelayBox.Text, out value) && value >= 0;
    }

    private static void SetInvalid(TextBox box, bool invalid)
    {
        if (invalid)
        {
            box.Classes.Add("Invalid");
        }
        else
        {
            box.Classes.Remove("Invalid");
        }
    }

    private void UpdateSaveState()
    {
        bool nameOk = !string.IsNullOrWhiteSpace(NameBox.Text) && !InvalidNameCharsRegex().IsMatch(NameBox.Text);
        bool gameOk = !string.IsNullOrWhiteSpace(GameBox.Text);
        bool sectionOk = !string.IsNullOrWhiteSpace(SectionBox.Text);
        bool maskOk = !string.IsNullOrEmpty(_maskPath);
        bool numbersOk = TryParseThreshold(out _) && TryParsePause(out _) && TryParseDelay(out _);
        BtnSave.IsEnabled = nameOk && gameOk && sectionOk && maskOk && numbersOk;
    }

    private async void BtnBrowseMask_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select mask image",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG images") { Patterns = ["*.png"] }],
        });

        if (files.Count == 0)
        {
            return;
        }

        _maskPath = files[0].Path.LocalPath;
        MaskPathBox.Text = _maskPath;
        UpdatePreview(_maskPath, ref _maskPreview, MaskPreviewImage, MaskPreviewError);
        UpdateSaveState();
    }

    private async void BtnBrowseBase_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select base image",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG images") { Patterns = ["*.png"] }],
        });

        if (files.Count == 0)
        {
            return;
        }

        _basePath = files[0].Path.LocalPath;
        BasePathBox.Text = _basePath;
        BtnClearBase.IsEnabled = true;
        UpdatePreview(_basePath, ref _basePreview, BasePreviewImage, null);
    }

    private void BtnClearBase_Click(object? sender, RoutedEventArgs e)
    {
        _basePath = null;
        BasePathBox.Text = "";
        BtnClearBase.IsEnabled = false;
        _basePreview?.Dispose();
        _basePreview = null;
        BasePreviewImage.Source = null;
    }

    private static void UpdatePreview(string path, ref Bitmap? slot, Image target, TextBlock? error)
    {
        slot?.Dispose();
        slot = null;
        target.Source = null;
        if (error != null)
        {
            error.IsVisible = false;
        }

        try
        {
            slot = new Bitmap(path);
            target.Source = slot;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (error != null)
            {
                error.IsVisible = true;
            }
        }
    }

    private async void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        if (!BtnSave.IsEnabled || _maskPath == null)
        {
            return;
        }

        TryParseThreshold(out double threshold);
        TryParsePause(out double pause);
        TryParseDelay(out int delay);

        var input = new NewPremadeSplitInput(
            Name: NameBox.Text!.Trim(),
            Description: DescriptionBox.Text?.Trim() ?? "",
            MaskSourcePath: _maskPath,
            BaseImageSourcePath: _basePath,
            ThresholdEnabled: ThresholdEnabledCheck.IsChecked == true,
            Threshold: threshold,
            PauseEnabled: PauseEnabledCheck.IsChecked == true,
            PauseTime: pause,
            DelayEnabled: DelayEnabledCheck.IsChecked == true,
            Delay: delay,
            Dummy: DummyCheck.IsChecked == true,
            Inverted: InvertedCheck.IsChecked == true);

        try
        {
            await PresetService.AddPremadeSplitAsync(
                _splitsDirectory, _existing, GameBox.Text!.Trim(), SectionBox.Text!.Trim(), input);
            Saved = true;
            Close();
        }
        catch (Exception ex)
        {
            await MessageBox.Show(this, "Save failed",
                $"Could not save the new pre-made split:\n\n{ex.Message}");
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close();
}
