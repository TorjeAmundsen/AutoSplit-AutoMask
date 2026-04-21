using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using AutoSplit_AutoMask.Capture;
using SkiaSharp;

namespace AutoSplit_AutoMask;

public sealed record FeedGroupHeader(string Title);

[SupportedOSPlatform("windows")]
public partial class TestOutputWindow : Window
{
    private enum FeedKind { Window, Region, Webcam }

    private sealed class FeedOption
    {
        public string Label { get; init; } = "";
        public FeedKind Kind { get; init; }
        public WindowCapture.WindowHandle Window { get; init; }
        public CamDeviceInfo? Camera { get; init; }
        public override string ToString() => Label;
    }

    private readonly CaptureController _controller = new();
    private readonly ObservableCollection<object> _feedOptions = [];

    // Reference inputs captured from MainWindow.
    private SplitPreset? _presetFromMain;
    private int _splitIndexFromMain = -1;
    private string? _inputPathFromMain;
    private Dictionary<string, SKBitmap>? _maskCacheFromMain;
    private bool _useCustomReference;

    private bool _loadingFeeds;
    private bool _suppressCropEvents;
    private int _activeSourceW = 320;
    private int _activeSourceH = 240;

    private string _prefsPath = "";
    private CapturePreferences? _loadedPrefs;
    private bool _hasUserChanges;
    private bool _isClosing;

    public TestOutputWindow()
    {
        InitializeComponent();

        ComboBoxFeedSource.ItemsSource = _feedOptions;
        ComboBoxFeedSource.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ComboBoxItem item)
            {
                bool isHeader = e.Index < _feedOptions.Count && _feedOptions[e.Index] is FeedGroupHeader;
                item.IsHitTestVisible = !isHeader;
                item.Focusable = !isHeader;
            }
        };

        _controller.FrameReady += OnFrameReady;
        _controller.ErrorReported += OnErrorReported;

        Opened += async (_, _) => await InitializeAsync();
        Closing += async (_, e) =>
        {
            if (_isClosing)
            {
                return;
            }

            // Always cancel and run shutdown on a fresh turn - the async lambda is not
            // awaited by Avalonia's close pipeline, so without Cancel the window tears
            // down while ShutdownAsync is still stopping the capture thread / webcam /
            // GDI handles.
            e.Cancel = true;

            if (_hasUserChanges)
            {
                await PromptSaveAndClose();
            }
            else
            {
                _isClosing = true;
                await ShutdownAsync();
                Close();
            }
        };
    }

    public void InitializeFromMainWindow(
        SplitPreset? preset,
        int selectedSplitIndex,
        string? selectedInputImagePath,
        Dictionary<string, SKBitmap>? maskCache,
        string prefsPath)
    {
        _presetFromMain = preset;
        _splitIndexFromMain = selectedSplitIndex;
        _inputPathFromMain = selectedInputImagePath;
        _maskCacheFromMain = maskCache;
        _prefsPath = prefsPath;
    }

    private async Task InitializeAsync()
    {
        _loadedPrefs = LoadPrefs();

        await RefreshFeedListAsync(selectAfter: _loadedPrefs?.FeedName);

        // RefreshFeedListAsync sets the index while _loadingFeeds is true,
        // so SelectionChanged won't fire. Activate the source manually.
        if (ComboBoxFeedSource.SelectedIndex >= 0)
        {
            await ActivateSelectedFeedAsync();

            if (_loadedPrefs is not null)
            {
                ApplyCropFromPrefs(_loadedPrefs);
            }
        }

        await RebuildReferenceFromPresetAsync();
        _controller.Start();
        _hasUserChanges = false;
    }

    private async Task ShutdownAsync()
    {
        _controller.FrameReady -= OnFrameReady;
        _controller.ErrorReported -= OnErrorReported;
        await _controller.DisposeAsync();
        LiveImageView.Source = null;
        ReferenceImageView.Source = null;
        _liveBitmap?.Dispose();
        _liveBitmap = null;
        _referenceBitmap?.Dispose();
        _referenceBitmap = null;
    }

    public async void UpdateFromMainWindow(
        SplitPreset? preset,
        int selectedSplitIndex,
        string? selectedInputImagePath,
        Dictionary<string, SKBitmap>? maskCache)
    {
        _presetFromMain = preset;
        _splitIndexFromMain = selectedSplitIndex;
        _inputPathFromMain = selectedInputImagePath;
        _maskCacheFromMain = maskCache;

        if (IsLoaded && !_useCustomReference)
        {
            await RebuildReferenceFromPresetAsync();
        }
    }

    private async Task RebuildReferenceFromPresetAsync()
    {
        if (_presetFromMain is null || _presetFromMain.Splits is null
            || _splitIndexFromMain < 0 || _splitIndexFromMain >= _presetFromMain.Splits.Count
            || string.IsNullOrEmpty(_presetFromMain.PresetFolder))
        {
            SetReferenceMissing("No preset + split selected in the main window.");
            return;
        }

        if (string.IsNullOrEmpty(_inputPathFromMain) || !File.Exists(_inputPathFromMain))
        {
            SetReferenceMissing("No input image loaded in the main window.");
            return;
        }

        var split = _presetFromMain.Splits[_splitIndexFromMain];
        var maskPath = Path.Combine(_presetFromMain.PresetFolder, split.Mask);

        if (!File.Exists(maskPath))
        {
            SetReferenceMissing($"Mask file missing: {split.Mask}");
            return;
        }

        ReferenceStatusLabel.Text = "Building reference…";

        var inputPath = _inputPathFromMain;
        var cache = _maskCacheFromMain ?? new Dictionary<string, SKBitmap>();

        try
        {
            var masked = await Task.Run(() =>
                ImageProcessor.ApplyScaledAlphaChannel(inputPath, maskPath, cache));

            try
            {
                ApplyReferenceBitmap(masked, split.Threshold, split.Name);
            }
            finally
            {
                masked.Dispose();
            }
        }
        catch (Exception ex)
        {
            SetReferenceMissing($"Failed to build reference: {ex.Message}");
        }
    }

    private async void BtnLoadCustomReference_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a masked PNG reference",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG Files") { Patterns = ["*.png"] }],
        });

        if (files.Count == 0)
        {
            return;
        }

        _useCustomReference = true;

        var path = files[0].Path.LocalPath;
        ReferenceStatusLabel.Text = "Loading PNG...";

        try
        {
            var decoded = await Task.Run(() => SKBitmap.Decode(path));
            if (decoded is null)
            {
                SetReferenceMissing("Could not decode PNG.");
                return;
            }

            var fileName = Path.GetFileName(path);
            double required = double.NaN;
            var match = ThresholdRegex().Match(fileName);
            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                required = parsed;
            }

            try
            {
                ApplyReferenceBitmap(decoded, required, label: fileName);
            }
            finally
            {
                decoded.Dispose();
            }
        }
        catch (Exception ex)
        {
            SetReferenceMissing($"Failed to load PNG: {ex.Message}");
        }
    }

    private void ApplyReferenceBitmap(SKBitmap source, double required, string label)
    {
        using var scaled = source.Resize(
            new SKImageInfo(CaptureController.CompareWidth, CaptureController.CompareHeight,
                SKColorType.Bgra8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));

        if (scaled is null)
        {
            SetReferenceMissing("Could not scale reference to 320×240.");
            return;
        }

        int byteCount = scaled.ByteCount;
        byte[] refPixels = new byte[byteCount];
        Marshal.Copy(scaled.GetPixels(), refPixels, 0, byteCount);

        int pixelCount = CaptureController.CompareWidth * CaptureController.CompareHeight;
        byte[] refMask = new byte[pixelCount];
        // Bgra8888 → alpha is at byte offset 3 of each pixel.
        for (int i = 0, p = 3; i < pixelCount; i++, p += 4)
        {
            refMask[i] = refPixels[p] >= 1 ? (byte)255 : (byte)0;
        }

        _referenceBitmap = ImageProcessor.ToAvaloniaBitmap(scaled);
        ReferenceImageView.Source = _referenceBitmap;

        _controller.UpdateReference(refPixels, refMask, double.IsNaN(required) ? 0.0 : required);

        int nonZero = 0;
        foreach (var b in refMask)
        {
            if (b != 0)
            {
                nonZero++;
            }
        }

        ReferenceStatusLabel.Text = $"{label} - {source.Width}×{source.Height} native, "
            + $"{nonZero * 100.0 / pixelCount:0.0}% opaque";

        RequiredLabel.Text = double.IsNaN(required) ? "-" : required.ToString("F4");
        HighestLabel.Text = "-";
        CurrentLabel.Text = "-";
        _controller.ResetHighest();
    }

    private void SetReferenceMissing(string reason)
    {
        _controller.UpdateReference(null, null, 0.0);
        ReferenceImageView.Source = null;
        _referenceBitmap = null;
        ReferenceStatusLabel.Text = reason;
        CurrentLabel.Text = "-";
        HighestLabel.Text = "-";
        RequiredLabel.Text = "-";
    }

    private async void BtnRefreshFeed_Click(object? sender, RoutedEventArgs e)
    {
        var current = ComboBoxFeedSource.SelectedItem as FeedOption;
        await RefreshFeedListAsync(selectAfter: current?.Label);
    }

    private async Task RefreshFeedListAsync(string? selectAfter)
    {
        _loadingFeeds = true;
        try
        {
            _feedOptions.Clear();

            try
            {
                var cams = await WebcamCapture.EnumerateDevicesAsync();
                if (cams.Count > 0)
                {
                    _feedOptions.Add(new FeedGroupHeader("Webcams"));
                    foreach (var cam in cams)
                    {
                        _feedOptions.Add(new FeedOption
                        {
                            Label = cam.Name,
                            Kind = FeedKind.Webcam,
                            Camera = cam,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorReported($"Webcam enumeration failed: {ex.Message}");
            }

            var windows = WindowCapture.ListWindows();
            if (windows.Count > 0)
            {
                _feedOptions.Add(new FeedGroupHeader("Windows"));
                foreach (var w in windows)
                {
                    _feedOptions.Add(new FeedOption
                    {
                        Label = w.Title,
                        Kind = FeedKind.Window,
                        Window = w,
                    });
                }
            }

            _feedOptions.Add(new FeedGroupHeader("Screen"));
            _feedOptions.Add(new FeedOption { Label = "Screen region", Kind = FeedKind.Region });

            int select = -1;
            if (selectAfter is not null)
            {
                for (int i = 0; i < _feedOptions.Count; i++)
                {
                    if (_feedOptions[i] is FeedOption opt && opt.Label == selectAfter)
                    {
                        select = i;
                        break;
                    }
                }
            }

            ComboBoxFeedSource.SelectedIndex = select;
        }
        finally
        {
            _loadingFeeds = false;
        }
    }

    private async void ComboBoxFeedSource_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingFeeds || _isClosing)
        {
            return;
        }

        await ActivateSelectedFeedAsync();
        _hasUserChanges = true;
    }

    private async Task ActivateSelectedFeedAsync()
    {
        if (ComboBoxFeedSource.SelectedItem is not FeedOption opt)
        {
            await _controller.SetSourceAsync(null, CancellationToken.None);
            return;
        }

        ICaptureSource? source = null;
        try
        {
            source = opt.Kind switch
            {
                FeedKind.Window => WindowCapture.Create(opt.Window),
                FeedKind.Region => CreateDefaultRegionCapture(),
                FeedKind.Webcam => CreateWebcamCapture(opt.Camera!),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            OnErrorReported($"Could not create source: {ex.Message}");
            return;
        }

        if (source is null)
        {
            return;
        }

        try
        {
            await _controller.SetSourceAsync(source, CancellationToken.None);
        }
        catch (Exception ex)
        {
            OnErrorReported($"Failed to start source: {ex.Message}");
            return;
        }

        _activeSourceW = source.SourceWidth;
        _activeSourceH = source.SourceHeight;
        ResetCropToFull();
    }

    private static RegionCapture CreateDefaultRegionCapture()
    {
        var (vx, vy, vw, vh) = RegionCapture.GetVirtualScreenRect();
        return new RegionCapture(vx, vy, vw, vh);
    }

    private static WebcamCapture CreateWebcamCapture(CamDeviceInfo device)
    {
        return new WebcamCapture(device);
    }

    private void CropValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressCropEvents)
        {
            return;
        }

        int x = (int)(CropX.Value ?? 0);
        int y = (int)(CropY.Value ?? 0);
        int w = (int)(CropW.Value ?? 1);
        int h = (int)(CropH.Value ?? 1);

        _controller.UpdateCrop(new CaptureController.CropRect(x, y, w, h));
        _hasUserChanges = true;
    }

    private void BtnCropIncrement_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && this.FindControl<NumericUpDown>(name) is { } nud)
        {
            nud.Value = (nud.Value ?? 0) + nud.Increment;
        }
    }

    private void BtnCropDecrement_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && this.FindControl<NumericUpDown>(name) is { } nud)
        {
            nud.Value = (nud.Value ?? 0) - nud.Increment;
        }
    }

    private void BtnResetCrop_Click(object? sender, RoutedEventArgs e)
    {
        ResetCropToFull();
    }

    private void ResetCropToFull()
    {
        int w = Math.Max(1, _activeSourceW);
        int h = Math.Max(1, _activeSourceH);

        _suppressCropEvents = true;
        try
        {
            if (ComboBoxFeedSource.SelectedItem is FeedOption { Kind: FeedKind.Region })
            {
                var (vx, vy, vw, vh) = RegionCapture.GetVirtualScreenRect();
                CropX.Value = vx;
                CropY.Value = vy;
                CropW.Value = vw;
                CropH.Value = vh;
                _controller.UpdateCrop(new CaptureController.CropRect(vx, vy, vw, vh));
            }
            else
            {
                CropX.Value = 0;
                CropY.Value = 0;
                CropW.Value = w;
                CropH.Value = h;
                _controller.UpdateCrop(new CaptureController.CropRect(0, 0, w, h));
            }
        }
        finally
        {
            _suppressCropEvents = false;
        }
    }

    private void BtnResetHighest_Click(object? sender, RoutedEventArgs e)
    {
        _controller.ResetHighest();
        HighestLabel.Text = "-";
    }

    private static readonly IBrush _metGreen = new SolidColorBrush(Color.FromRgb(0x6C, 0xD6, 0x88));
    private static readonly IBrush _metWhite = Brushes.White;

    private WriteableBitmap? _liveBitmap;
    private Bitmap? _referenceBitmap;

    private void OnFrameReady(byte[] bgraPixels, double current, double highest, double required)
    {
        // Frames may arrive on the dispatcher after shutdown began; ignore them so we
        // don't recreate _liveBitmap or touch controls after disposal.
        if (_isClosing)
        {
            return;
        }

        if (_liveBitmap is null)
        {
            _liveBitmap = new WriteableBitmap(
                new PixelSize(CaptureController.CompareWidth, CaptureController.CompareHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
            LiveImageView.Source = _liveBitmap;
        }

        using (var locked = _liveBitmap.Lock())
        {
            int srcStride = CaptureController.CompareWidth * 4;
            if (locked.RowBytes == srcStride)
            {
                Marshal.Copy(bgraPixels, 0, locked.Address, bgraPixels.Length);
            }
            else
            {
                for (int y = 0; y < CaptureController.CompareHeight; y++)
                {
                    Marshal.Copy(bgraPixels, y * srcStride,
                        locked.Address + y * locked.RowBytes, srcStride);
                }
            }
        }

        LiveImageView.InvalidateVisual();

        bool hasReference = ReferenceImageView.Source is not null;
        CurrentLabel.Text = hasReference ? current.ToString("F4") : "-";
        HighestLabel.Text = hasReference ? highest.ToString("F4") : "-";
        RequiredLabel.Text = required > 0 ? required.ToString("F4") : "-";
        CurrentLabel.Foreground = required > 0 && current >= required ? _metGreen : _metWhite;

        // If the source just reported a different size on first frame, update our crop bounds
        // so a subsequent reset uses the new size.
        if (ComboBoxFeedSource.SelectedItem is FeedOption opt
            && opt.Kind != FeedKind.Region)
        {
            // Live bitmap here is 320x240 (post-scale); source size lives on the ICaptureSource.
            // We only track the source dims we saw at creation time; good enough for defaults.
        }
    }

    private void OnErrorReported(string message)
    {
        ReferenceStatusLabel.Text = message;
    }

    private CapturePreferences BuildCurrentPrefs()
    {
        var feedOpt = ComboBoxFeedSource.SelectedItem as FeedOption;
        return new CapturePreferences
        {
            FeedKind = feedOpt?.Kind.ToString(),
            FeedName = feedOpt?.Label,
            CropX = (int)(CropX.Value ?? 0),
            CropY = (int)(CropY.Value ?? 0),
            CropW = (int)(CropW.Value ?? 1),
            CropH = (int)(CropH.Value ?? 1),
        };
    }

    private void ApplyCropFromPrefs(CapturePreferences prefs)
    {
        _suppressCropEvents = true;
        try
        {
            CropX.Value = prefs.CropX;
            CropY.Value = prefs.CropY;
            CropW.Value = prefs.CropW;
            CropH.Value = prefs.CropH;
            _controller.UpdateCrop(new CaptureController.CropRect(prefs.CropX, prefs.CropY, prefs.CropW, prefs.CropH));
        }
        finally
        {
            _suppressCropEvents = false;
        }
    }

    private CapturePreferences? LoadPrefs()
    {
        if (string.IsNullOrEmpty(_prefsPath) || !File.Exists(_prefsPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_prefsPath);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.CapturePreferences);
        }
        catch
        {
            return null;
        }
    }

    private void SavePrefs(CapturePreferences prefs)
    {
        if (string.IsNullOrEmpty(_prefsPath))
        {
            return;
        }

        var json = JsonSerializer.Serialize(prefs, AppJsonContext.Default.CapturePreferences);
        File.WriteAllText(_prefsPath, json);
    }

    private async Task PromptSaveAndClose()
    {
        var result = await MessageBox.Show(this, "Save capture preferences",
            "Save your capture source and crop settings for next time?", MessageBoxButton.YesNo);

        if (result == MessageBoxResult.Yes)
        {
            SavePrefs(BuildCurrentPrefs());
        }

        _isClosing = true;
        _hasUserChanges = false;
        await ShutdownAsync();
        Close();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\(([0-9]*\.?[0-9]+)\)")]
    private static partial System.Text.RegularExpressions.Regex ThresholdRegex();
}
