using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AutoSplit_AutoMask;

public partial class MainWindow : Window
{
        private string? inputImagePath;
        private string? alphaImagePath;
        private Bitmap? maskedImage;
        private List<SplitPreset>? splitPresets;

        public MainWindow()
        {
            InitializeComponent();
            var test = new FooBar();
            test.BarFoo();
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
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                inputImagePath = openFileDialog.FileName;
                InputImageView.Source = new BitmapImage(new Uri(inputImagePath));
            }
        }

        private void BtnLoadAlphaImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*"
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
                FileName = "masked_image",
                DefaultExt = ".png",
                Filter = "PNG Files|*.png",
            };

            UpdateOutputPreview();

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
