using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AutoSplit_AutoMask;

public partial class MainWindow : Window
{
        public ObservableCollection<ComboBoxItem> presetComboBoxItems { get; set; }
        public ComboBoxItem selectedPreset { get; set; }
        public int selectedPresetIndex { get; set; }
        private string? inputImagePath;
        private string? alphaImagePath;
        private Bitmap? maskedImage;
        private List<SplitPreset> splitPresets;

        public MainWindow()
        {
            InitializeComponent();
            
            DataContext = this;
            
            presetComboBoxItems = new ObservableCollection<ComboBoxItem>();
            
            var cbItem = new ComboBoxItem { Content = "Select preset..." };
            selectedPreset = cbItem;

            string currentPath = AppContext.BaseDirectory;

            if (string.IsNullOrEmpty(currentPath))
            {
                throw new Exception("Could not find executable path");
            }
            
            Directory.CreateDirectory(currentPath + "\\presets\\");

            string[] presetPaths = Directory.GetFiles(currentPath + "\\presets\\", "*json");
            
            Console.WriteLine($"Found {presetPaths.Length} presets");
            
            splitPresets = new List<SplitPreset>();

            foreach (string presetPath in presetPaths)
            {
                SplitPreset? preset = JsonSerializer.Deserialize<SplitPreset>(File.ReadAllText(presetPath));
                
                if (preset is not null)
                {
                    preset.PresetFileName = presetPath.Replace(".json", "");
                    Console.WriteLine($"Adding preset: {presetPath}");
                    splitPresets.Add(preset);
                    presetComboBoxItems.Add(new ComboBoxItem { Content = preset.PresetName});
                }
            }

            foreach (SplitPreset preset in splitPresets)
            {
                Console.WriteLine($"Found preset: {preset.PresetName}");
                for (int i = 0; i < preset.Splits.Count; ++i)
                {
                    var cur = preset.Splits[i];
                    Console.WriteLine($"{i + 1}. {cur.Name}, threshold: {cur.Threshold}, filename: {preset.PresetFileName}, enabled: {cur.Enabled}");
                }
            }
            
        }

        private void OnSelected(object sender, RoutedEventArgs e)
        {
            
        }

        private void BtnOpenPresetsFolder_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("\"" + AppContext.BaseDirectory + "presets\\" + "\"");
            Process.Start("explorer.exe", "\"" + AppContext.BaseDirectory + "presets\\" + "\"");
        }

        private void UpdateOutputPreview()
        {
            if (String.IsNullOrEmpty(inputImagePath) || String.IsNullOrEmpty(alphaImagePath))
            {
                return;
            }

            var maskedImage = ApplyScaledAlphaChannel();

            var bmpImage = new BitmapImage();

            var memoryStream = new MemoryStream();

            maskedImage.Save(memoryStream, ImageFormat.Png);
            bmpImage.BeginInit();
            bmpImage.StreamSource = memoryStream;
            bmpImage.EndInit();
            bmpImage.Freeze();

            OutputImageView.Source = bmpImage;
        }

        private void BtnLoadInputImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Input Image",
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;|All Files|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                inputImagePath = openFileDialog.FileName;
                InputImageView.Source = new BitmapImage(new Uri(inputImagePath));
                UpdateOutputPreview();
            }
        }

        private void BtnLoadAlphaImage_Click(object sender, RoutedEventArgs e)
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

        private void BtnSaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(inputImagePath) || string.IsNullOrEmpty(alphaImagePath))
            {
                MessageBox.Show("Please load both the input image and the mask image.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Title = "Save Masked Image",
                FileName = "masked_image",
                DefaultExt = ".png",
                Filter = "PNG Files|*.png",
            };

            maskedImage = ApplyScaledAlphaChannel();

            bool? result = saveFileDialog.ShowDialog();

            if (result is true)
            {
                string filename = saveFileDialog.FileName;
                maskedImage.Save(filename, ImageFormat.Png);
            }
        }

        private void BtnNextAlphaImage_Click(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void BtnPrevAlphaImage_Click(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void BtnAutoSaveImage_Click(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private Bitmap ApplyScaledAlphaChannel()
        {
            using (var inputImage = new Bitmap(inputImagePath))
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

                        Color outputColor = Color.FromArgb(alphaColor.A, inputColor.R, inputColor.G, inputColor.B);
                        outputImage.SetPixel(x, y, outputColor);
                    }
                }

                return outputImage;
            }
        }

        private void BtnAutoSave_Click(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }
    }
