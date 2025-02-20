using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace AutoSplit_AutoMask;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
    {
        private string? inputImagePath;
        private string? alphaImagePath;
        private string? outputImagePath;

        public MainWindow()
        {
            InitializeComponent();
        }

        // Load Input Image
        private void BtnLoadInputImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                inputImagePath = openFileDialog.FileName;
                InputImageView.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(inputImagePath));
            }
        }

        // Load Alpha Channel Image
        private void BtnLoadAlphaImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                alphaImagePath = openFileDialog.FileName;
            }
        }

        // Apply Alpha Channel Masking
        private void BtnApplyAlpha_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(inputImagePath) || string.IsNullOrEmpty(alphaImagePath))
            {
                MessageBox.Show("Please load both the input image and the alpha channel image.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Define output image path
            outputImagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "output_image.png");

            ApplyScaledAlphaChannel(inputImagePath, alphaImagePath, outputImagePath);

            // Display the output image
            OutputImageView.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(outputImagePath));
        }

        // Function to apply the scaled alpha channel
        private void ApplyScaledAlphaChannel(string? inputImagePath, string? alphaImagePath, string? outputImagePath)
        {
            using (Bitmap inputImage = new Bitmap(inputImagePath))
            using (Bitmap alphaImage = new Bitmap(alphaImagePath))
            {
                // Scale the alpha image to match the dimensions of the input image
                Bitmap scaledAlphaImage = new Bitmap(inputImage.Width, inputImage.Height);
                using (Graphics g = Graphics.FromImage(scaledAlphaImage))
                {
                    g.DrawImage(alphaImage, 0, 0, inputImage.Width, inputImage.Height);
                }

                // Create the output image with proper alpha mask
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

                outputImage.Save(outputImagePath, ImageFormat.Png);
            }
        }
    }
