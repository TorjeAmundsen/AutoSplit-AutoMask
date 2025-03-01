using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Avalonia.Controls;
using RoutedEventArgs = Avalonia.Interactivity.RoutedEventArgs;
using Size = System.Windows.Size;
using Window = Avalonia.Controls.Window;

namespace AutoSplit_AutoMask;

using static Utils;

public partial class MainWindow : Window
{
    public ObservableCollection<ComboBoxItem> presetComboBoxItems { get; set; }
    public ObservableCollection<ComboBoxItem> splitsComboBoxItems { get; set; }

    public ObservableCollection<ComboBoxItem> inputImagesComboBoxItems { get; set; }
    public int selectedPresetIndex { get; set; }
    public int selectedSplitIndex { get; set; }
    private List<string>? inputImagePaths;
    private string? selectedInputImagePath;
    private string? outputDirectoryPath;
    private string? alphaImagePath;
    private Bitmap? maskedImage;
    private List<SplitPreset> splitPresets;
    private string createdFilename;
    private string currentPresetsDirectory;


    public MainWindow()
    {
        InitializeComponent();

        DataContext = this;

        Title = "AutoMask v" + AutoMaskSemVer + (string.IsNullOrEmpty(VersionSuffix) ? string.Empty : "-" + VersionSuffix);

        presetComboBoxItems = new ObservableCollection<ComboBoxItem>();
        splitsComboBoxItems = new ObservableCollection<ComboBoxItem>();
        inputImagesComboBoxItems = new ObservableCollection<ComboBoxItem>();

        splitPresets = new List<SplitPreset>();

        createdFilename = "Output preview";

        var rootDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName);

        if (string.IsNullOrEmpty(rootDir))
        {
            MessageBox.Show("Error! Program could not locate root directory. Exiting...");
            throw new NullReferenceException("Could not locate root directory");
        }

        currentPresetsDirectory = rootDir + "\\presets\\";
        
        Directory.CreateDirectory(rootDir + "\\presets\\");
        
        RefreshPresets();
    }

    private void RefreshPresets()
    {
        var presetPaths = Directory.EnumerateDirectories(currentPresetsDirectory).Where(dir => Directory.EnumerateFiles(dir, "preset.json", SearchOption.TopDirectoryOnly).Any());
        
        Console.WriteLine($"Found {presetPaths.Count()} presets:");
        
        foreach (var preset in presetPaths)
        {
            Console.WriteLine(preset + "\\preset.json");
        }

        var foundPresets = new List<SplitPreset>();

        foreach (string presetPath in presetPaths)
        {
            SplitPreset? preset = JsonSerializer.Deserialize<SplitPreset>(File.ReadAllText(presetPath + "\\preset.json"));

            if (preset is not null)
            {
                preset.PresetFolder = presetPath;
                Console.WriteLine($"Adding preset: {preset.PresetFolder} from {presetPath + "\\preset.json"}");
                foundPresets.Add(preset);
            }
        }

        if (splitPresets.Count > 0 && foundPresets.SequenceEqual(splitPresets))
        {
            Console.WriteLine("No changes were detected");
            return;
        }

        splitPresets = foundPresets;
        presetComboBoxItems.Clear();
        selectedPresetIndex = -1;
        selectedSplitIndex = 0;

        var invalidPresetFiles = new List<string>();

        foreach (SplitPreset preset in foundPresets)
        {
            if (preset.Splits == null || preset.Splits.Count == 0)
            {
                MessageBox.Show($"Invalid preset format found: {preset.PresetFolder}");
                invalidPresetFiles.Add(preset.PresetFolder);
                continue;
            }
            
            presetComboBoxItems.Add(new ComboBoxItem { Content = preset.PresetName});
            Console.WriteLine($"Found preset: {preset.PresetName}");
            
            for (int i = 0; i < preset.Splits.Count; ++i)
            {
                var cur = preset.Splits[i];
                Console.WriteLine($"{i + 1}. {cur.Name}, threshold: {cur.Threshold}, filename: {preset.PresetFolder}, enabled: {cur.Enabled}");
            }
        }

        if (invalidPresetFiles.Count > 0)
        {
            MessageBox.Show($"Invalid preset format found in: {string.Join(", ", invalidPresetFiles)}");
        }
    }

    private void BtnRefreshPresets_Click(object? sender, RoutedEventArgs e)
    {
        RefreshPresets();
    }

    private void ComboBoxSelectPreset_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ComboBoxSelectSplit.SelectedIndex = 0;

        splitsComboBoxItems.Clear();

        foreach (var split in splitPresets[selectedPresetIndex].Splits!)
        {
            splitsComboBoxItems.Add(new ComboBoxItem { Content = split.Name });
        }
    }

    private void BtnSelectOutputDirectory_Click(object? sender, RoutedEventArgs e)
    {
        var folderDialog = new OpenFolderDialog
        {
            Title = "Select output directory"
        };

        var foo = folderDialog.ShowAsync(this);
        
        Console.WriteLine(foo);

        if (true)
        {
            outputDirectoryPath = folderDialog.Directory;
            OutputDirectoryTextBox.Text = outputDirectoryPath;
        }
    }

    private void BtnOpenPresetsFolder_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", "\"" + currentPresetsDirectory + "\"");
    }

    private void UpdateOutputPreview()
    {
        ImageSavedLabel.Opacity = 0;
        
        if (String.IsNullOrEmpty(selectedInputImagePath) || String.IsNullOrEmpty(alphaImagePath))
        {
            OutputImageView.Source = null;
            return;
        }

        if (!File.Exists(selectedInputImagePath))
        {
            MessageBox.Show("Specified input image not found!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!File.Exists(alphaImagePath))
        {
            MessageBox.Show("Specified mask image not found!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        maskedImage = ApplyScaledAlphaChannel();

        double factor = 1;

        int previewWidth = Convert.ToInt32(maskedImage.Width * ((320 / factor) / maskedImage.Width));
        int previewHeight = Convert.ToInt32(maskedImage.Height * ((320 / factor) / maskedImage.Width));
        
        var previewImage = new Bitmap(maskedImage, new Size(previewWidth, previewHeight));

        var bmpImage = new BitmapImage();

        var memoryStream = new MemoryStream();

        previewImage.Save(memoryStream, ImageFormat.Png);
        bmpImage.BeginInit();
        bmpImage.StreamSource = memoryStream;
        bmpImage.EndInit();
        bmpImage.Freeze();

        OutputImageView.Source = bmpImage;

        createdFilename = CreateCurrentFilename();

        PreviewImageLabel.Text = createdFilename;
    }

    private void BtnLoadInputImages_Click(object? sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Select Input Image",
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;|All Files|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            inputImagePaths = openFileDialog.FileNames.ToList();
            selectedInputImagePath = inputImagePaths[0];
            ComboBoxSelectInputImage.SelectedIndex = 0;
            InputImageLabel.Text = selectedInputImagePath.Substring(selectedInputImagePath.LastIndexOf('\\') + 1);
            InputImageView.Source = new BitmapImage(new Uri(selectedInputImagePath));

            inputImagesComboBoxItems.Clear();

            foreach (var path in inputImagePaths)
            {
                Console.WriteLine($"Adding ComboBoxItem: {path.Substring(selectedInputImagePath.LastIndexOf('\\') + 1)}");
                inputImagesComboBoxItems.Add(new ComboBoxItem { Content =  path.Substring(selectedInputImagePath.LastIndexOf('\\') + 1)});
                Console.WriteLine(inputImagesComboBoxItems.Count);
            }

            if (inputImagesComboBoxItems.Count > 0)
            {
                ComboBoxSelectInputImage.SelectedIndex = 0;
            }
        }
    }

    private void BtnLoadAlphaImage_Click(object? sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "PNG Files|*.png",
            Title = "Select Mask (Alpha Channel)",
        };

        if (openFileDialog.ShowDialog() == true)
        {
            alphaImagePath = openFileDialog.FileName;

            UpdateOutputPreview();
        }
    }

    private void BtnSaveImageAs_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(selectedInputImagePath) || string.IsNullOrEmpty(alphaImagePath))
        {
            MessageBox.Show("Please load both the input image and the mask image.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            Title = "Save Masked Image",
            Filters = new List<FileDialogFilter>() {}
            /*FileName = createdFilename,
            DefaultExt = ".png",
            Filter = "PNG Files|*.png",*/
        };

        maskedImage = ApplyScaledAlphaChannel();

        bool? result = saveFileDialog.();

        if (result is true)
        {
            string filename = saveFileDialog.;
            maskedImage.Save(filename, ImageFormat.Png);
        }
    }

    private void BtnPrevAlphaImage_Click(object? sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectSplit.SelectedIndex == splitsComboBoxItems.Count - 1)
        {
            return;
        }

        ComboBoxSelectSplit.SelectedIndex += 1;
    }

    private void BtnNextAlphaImage_Click(object? sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectSplit.SelectedIndex == 0)
        {
            return;
        }

        ComboBoxSelectSplit.SelectedIndex -= 1;
    }

    private Bitmap ApplyScaledAlphaChannel()
    {
        using (var inputImage = new Bitmap(selectedInputImagePath))
        using (var alphaImage = new Bitmap(alphaImagePath))
        {
            var scaledAlphaImage = new Bitmap(inputImage.Width, inputImage.Height);
            using (Graphics g = Graphics.FromImage(scaledAlphaImage))
            {
                g.DrawImage(alphaImage, 0, 0, inputImage.Width, inputImage.Height);
            }

            Bitmap outputImage = new Bitmap(inputImage.Width, inputImage.Height);

            for (int x = 0; x < inputImage.Width; x++)
            {
                for (int y = 0; y < inputImage.Height; y++)
                {
                    Color inputColor = inputImage.GetPixel(x, y);
                    Color alphaColor = scaledAlphaImage.GetPixel(x, y);

                    if (alphaColor.A != 255)
                    {
                        outputImage.SetPixel(x, y, Color.Transparent);
                    }
                    else
                    {
                        Color outputColor = Color.FromArgb(alphaColor.A, inputColor.R, inputColor.G, inputColor.B);
                        outputImage.SetPixel(x, y, outputColor);
                    }
                }
            }

            return outputImage;
        }
    }

    private void BtnAutoSave_Click(object? sender, RoutedEventArgs e)
    {
        if (maskedImage == null)
        {
            MessageBox.Show("Error getting masked output image.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (string.IsNullOrEmpty(outputDirectoryPath))
        {
            MessageBox.Show("Please select an output directory.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!Directory.Exists(outputDirectoryPath))
        {
            MessageBox.Show("Specified directory not found!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (File.Exists(outputDirectoryPath + "\\" + createdFilename))
        {
            if (MessageBox.Show("File already exists! Do you want to overwrite it?", "Alert", MessageBoxButton.YesNo) ==
                MessageBoxResult.Yes)
            {
                maskedImage.Save(outputDirectoryPath + "\\" + createdFilename, ImageFormat.Png);
                ImageSavedLabel.Opacity = 1;
            }
        }
        else
        {
            maskedImage.Save(outputDirectoryPath + "\\" + createdFilename, ImageFormat.Png);
            ImageSavedLabel.Opacity = 1;
        }
    }

    private string CreateCurrentFilename()
    {
        if (string.IsNullOrEmpty(selectedInputImagePath) || string.IsNullOrEmpty(alphaImagePath))
        {
            return "";
        }

        if (selectedPresetIndex == -1)
        {
            var path = selectedInputImagePath.Substring(selectedInputImagePath.LastIndexOf('\\') + 1);
            path = path.Remove(path.LastIndexOf('.'));
            Console.WriteLine(path);
            return path + "_masked.png";
        }

        var currentPreset = splitPresets[selectedPresetIndex];

        if (currentPreset.Splits == null)
        {
            MessageBox.Show("Error loading selected split!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return "";
        }

        var currentSplit = currentPreset.Splits[selectedSplitIndex];

        int totalSplits = currentPreset.Splits.Count;

        string prefix;

        switch (currentSplit.Name)
        {
            case "reset":
                prefix = "reset";
                break;
            case "start_auto_splitter":
                prefix = "start_auto_splitter";
                break;
            default:
                prefix = $"{selectedSplitIndex.ToString().PadLeft(totalSplits.ToString().Length, '0')}_{currentSplit.Name}";
                break;
        }

        string output =
            $"{prefix}_({currentSplit.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)})";

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

    private void ComboBoxSelectSplit_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (presetComboBoxItems.Count == 0)
        {
            ComboBoxSelectPreset.SelectedIndex = -1;
            return;
        }

        if (ComboBoxSelectSplit.SelectedIndex == -1)
        {
            alphaImagePath = "";
            UpdateOutputPreview();
            return;
        }

        var currentPreset = splitPresets[selectedPresetIndex];

        if (currentPreset.Splits == null)
        {
            MessageBox.Show("Error loading selected split!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            alphaImagePath = "";
            UpdateOutputPreview();
            return;
        }

        alphaImagePath = currentPreset.PresetFolder + "\\" + currentPreset.Splits[selectedSplitIndex].MaskImagePath;
        Console.WriteLine(alphaImagePath);
        UpdateOutputPreview();
    }

    private void BtnPrevInputImage_Click(object? sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectInputImage.SelectedIndex == splitsComboBoxItems.Count - 1)
        {
            return;
        }

        ComboBoxSelectInputImage.SelectedIndex += 1;
    }

    private void BtnNextInputImage_Click(object? sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectInputImage.SelectedIndex == 0)
        {
            return;
        }

        ComboBoxSelectInputImage.SelectedIndex -= 1;
    }

    private void ComboBoxSelectInputImage_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ComboBoxSelectInputImage.SelectedIndex == -1)
        {
            return;
        }

        if (inputImagePaths == null)
        {
            MessageBox.Show("No input images loaded!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateOutputPreview();
            return;
        }
        selectedInputImagePath = inputImagePaths[ComboBoxSelectInputImage.SelectedIndex];
        InputImageLabel.Text = selectedInputImagePath.Substring(selectedInputImagePath.LastIndexOf('\\') + 1);
        InputImageView.Source = new BitmapImage(new Uri(selectedInputImagePath));
        UpdateOutputPreview();
    }

    private void BtnShowOutput_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(outputDirectoryPath))
        {
            MessageBox.Show("Please select an output directory below.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Process.Start("explorer.exe", outputDirectoryPath);
    }

    private void BtnOpenPresetEditor_Click(object? sender, RoutedEventArgs e)
    {
        PresetEditor editorWindow = new PresetEditor(splitPresets);
        
        editorWindow.Owner = this;
        
        editorWindow.ShowDialog();
    }
}
