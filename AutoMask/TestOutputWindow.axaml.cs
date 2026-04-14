using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using AutoSplit_AutoMask.Capture;
using SkiaSharp;

namespace AutoSplit_AutoMask;

public partial class TestOutputWindow : Window
{
    private enum ReferenceMode { PresetSplit = 0, CustomPng = 1 }

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
    private readonly ObservableCollection<FeedOption> _feedOptions = [];

    // Reference inputs captured from MainWindow.
    private SplitPreset? _presetFromMain;
    private int _splitIndexFromMain = -1;
    private string? _inputPathFromMain;
    private Dictionary<string, SKBitmap>? _maskCacheFromMain;

    private bool _loadingFeeds;
    private bool _suppressCropEvents;
    private int _activeSourceW = 320;
    private int _activeSourceH = 240;

    public TestOutputWindow()
    {
        InitializeComponent();

        ComboBoxFeedSource.ItemsSource = _feedOptions;

        _controller.FrameReady += OnFrameReady;
        _controller.ErrorReported += OnErrorReported;

        Opened += async (_, _) => await InitializeAsync();
        Closing += async (_, _) => await ShutdownAsync();
    }

    public void InitializeFromMainWindow(
        SplitPreset? preset,
        int selectedSplitIndex,
        string? selectedInputImagePath,
        Dictionary<string, SKBitmap>? maskCache)
    {
        _presetFromMain = preset;
        _splitIndexFromMain = selectedSplitIndex;
        _inputPathFromMain = selectedInputImagePath;
        _maskCacheFromMain = maskCache;
    }

    private async Task InitializeAsync()
    {
        ComboBoxReferenceSource.SelectedIndex = 0;
        await RefreshFeedListAsync(selectAfter: null);
        await RebuildReferenceFromPresetAsync();
        _controller.Start();
    }

    private async Task ShutdownAsync()
    {
        _controller.FrameReady -= OnFrameReady;
        _controller.ErrorReported -= OnErrorReported;
        await _controller.DisposeAsync();
    }

    private async void ComboBoxReferenceSource_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (ComboBoxReferenceSource.SelectedIndex == (int)ReferenceMode.CustomPng)
        {
            await LoadCustomReferenceAsync();
        }
        else
        {
            await RebuildReferenceFromPresetAsync();
        }
    }

    private async void BtnRefreshReference_Click(object? sender, RoutedEventArgs e)
    {
        if (ComboBoxReferenceSource.SelectedIndex == (int)ReferenceMode.CustomPng)
        {
            await LoadCustomReferenceAsync();
        }
        else
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

    private async Task LoadCustomReferenceAsync()
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

        var path = files[0].Path.LocalPath;
        ReferenceStatusLabel.Text = "Loading PNG…";

        try
        {
            var decoded = await Task.Run(() => SKBitmap.Decode(path));
            if (decoded is null)
            {
                SetReferenceMissing("Could not decode PNG.");
                return;
            }

            try
            {
                ApplyReferenceBitmap(decoded, required: double.NaN, label: Path.GetFileName(path));
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
        System.Runtime.InteropServices.Marshal.Copy(scaled.GetPixels(), refPixels, 0, byteCount);

        int pixelCount = CaptureController.CompareWidth * CaptureController.CompareHeight;
        byte[] refMask = new byte[pixelCount];
        // Bgra8888 → alpha is at byte offset 3 of each pixel.
        for (int i = 0, p = 3; i < pixelCount; i++, p += 4)
        {
            refMask[i] = refPixels[p] >= 1 ? (byte)255 : (byte)0;
        }

        ReferenceImageView.Source = ImageProcessor.ToAvaloniaBitmap(scaled);

        _controller.UpdateReference(refPixels, refMask, double.IsNaN(required) ? 0.0 : required);

        int nonZero = 0;
        foreach (var b in refMask)
        {
            if (b != 0)
            {
                nonZero++;
            }
        }

        ReferenceStatusLabel.Text = $"{label} — {source.Width}×{source.Height} native, "
            + $"{nonZero * 100 / pixelCount}% opaque";

        RequiredLabel.Text = double.IsNaN(required) ? "—" : required.ToString("F4");
        HighestLabel.Text = "—";
        CurrentLabel.Text = "—";
        _controller.ResetHighest();
    }

    private void SetReferenceMissing(string reason)
    {
        _controller.UpdateReference(null, null, 0.0);
        ReferenceImageView.Source = null;
        ReferenceStatusLabel.Text = reason;
        CurrentLabel.Text = "—";
        HighestLabel.Text = "—";
        RequiredLabel.Text = "—";
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
                foreach (var cam in cams)
                {
                    _feedOptions.Add(new FeedOption
                    {
                        Label = $"Webcam: {cam.Name}",
                        Kind = FeedKind.Webcam,
                        Camera = cam,
                    });
                }
            }
            catch (Exception ex)
            {
                OnErrorReported($"Webcam enumeration failed: {ex.Message}");
            }

            foreach (var w in WindowCapture.ListWindows())
            {
                _feedOptions.Add(new FeedOption
                {
                    Label = $"Window: {w.Title}",
                    Kind = FeedKind.Window,
                    Window = w,
                });
            }

            _feedOptions.Add(new FeedOption { Label = "Screen region", Kind = FeedKind.Region });
            

            int select = -1;
            if (selectAfter is not null)
            {
                for (int i = 0; i < _feedOptions.Count; i++)
                {
                    if (_feedOptions[i].Label == selectAfter)
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
        if (_loadingFeeds)
        {
            return;
        }

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
        HighestLabel.Text = "—";
    }

    private static readonly IBrush _metGreen = new SolidColorBrush(Color.FromRgb(0x6C, 0xD6, 0x88));
    private static readonly IBrush _metWhite = Brushes.White;

    private void OnFrameReady(Bitmap live, double current, double highest, double required)
    {
        LiveImageView.Source = live;
        CurrentLabel.Text = current.ToString("F4");
        HighestLabel.Text = highest.ToString("F4");
        RequiredLabel.Text = required > 0 ? required.ToString("F4") : "—";
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
}
