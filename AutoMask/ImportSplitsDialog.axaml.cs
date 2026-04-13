using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AutoSplit_AutoMask;

public partial class ImportSplitsDialog : Window
{
    private static readonly SolidColorBrush DimTextBrush = new(Color.Parse("#888888"));
    private static readonly SolidColorBrush CardBorderBrush = new(Color.Parse("#444444"));

    private readonly List<(PremadeSplitsFile File, PremadeSplit Split, CheckBox CheckBox)> _splitEntries = [];

    public List<(PremadeSplitsFile File, PremadeSplit Split)> SelectedSplits { get; } = [];

    public ImportSplitsDialog(List<PremadeSplitsFile> premadeSplits)
    {
        InitializeComponent();
        PopulateSplitsList(premadeSplits);
    }

    private void PopulateSplitsList(List<PremadeSplitsFile> premadeSplits)
    {
        foreach (var splitsFile in premadeSplits)
        {
            if (splitsFile.Splits == null || splitsFile.Splits.Count == 0)
            {
                continue;
            }

            var header = new TextBlock
            {
                Text = splitsFile.GameName ?? "Unknown Game",
                FontWeight = FontWeight.SemiBold,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#AAAAAA")),
                Margin = new Thickness(0, 6, 0, 4),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            SplitsPanel.Children.Add(header);

            foreach (var split in splitsFile.Splits)
            {
                SplitsPanel.Children.Add(BuildSplitCard(splitsFile, split));
            }
        }
    }

    private Border BuildSplitCard(PremadeSplitsFile splitsFile, PremadeSplit split)
    {
        var checkBox = new CheckBox
        {
            Content = new TextBlock { Text = split.Name, FontWeight = FontWeight.SemiBold },
            VerticalAlignment = VerticalAlignment.Top,
        };
        checkBox.IsCheckedChanged += (_, _) => UpdateImportButtonState();
        _splitEntries.Add((splitsFile, split, checkBox));

        var infoStack = new StackPanel { Spacing = 2 };
        infoStack.Children.Add(checkBox);

        if (!string.IsNullOrWhiteSpace(split.Description))
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = split.Description,
                FontSize = 10,
                Foreground = DimTextBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(26, 0, 0, 0),
            });
        }

        var propertiesText = BuildPropertiesString(split);
        if (propertiesText.Length > 0)
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = propertiesText,
                FontSize = 10,
                Foreground = DimTextBrush,
                Margin = new Thickness(26, 2, 0, 0),
            });
        }

        var cardContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        cardContent.Children.Add(infoStack);

        if (!string.IsNullOrEmpty(split.BaseImage) && splitsFile.FolderPath != null)
        {
            string baseImagePath = Path.Combine(splitsFile.FolderPath, split.BaseImage);
            if (File.Exists(baseImagePath))
            {
                try
                {
                    var thumbnail = new Image
                    {
                        Source = new Bitmap(baseImagePath),
                        Width = 64,
                        Height = 48,
                        Stretch = Stretch.Uniform,
                    };
                    var imageBorder = new Border
                    {
                        BorderBrush = CardBorderBrush,
                        BorderThickness = new Thickness(1),
                        Child = thumbnail,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 0, 0),
                    };
                    Grid.SetColumn(imageBorder, 1);
                    cardContent.Children.Add(imageBorder);
                }
                catch
                {
                    // Image failed to load — skip thumbnail
                }
            }
        }

        return new Border
        {
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.Parse("#2A2A2A")),
            Padding = new Thickness(6),
            Margin = new Thickness(8, 2, 0, 2),
            Child = cardContent,
        };
    }

    private static string BuildPropertiesString(PremadeSplit split)
    {
        var parts = new List<string>();

        if (Math.Abs(split.Threshold - 0.95f) > 0.001f)
        {
            parts.Add($"Threshold: {split.Threshold}");
        }

        if (Math.Abs(split.PauseTime - 3.0f) > 0.001f)
        {
            parts.Add($"Pause: {split.PauseTime}s");
        }

        if (split.Delay > 0)
        {
            parts.Add($"Delay: {split.Delay}ms");
        }

        if (split.Dummy)
        {
            parts.Add("Dummy");
        }

        if (split.Inverted)
        {
            parts.Add("Inverted");
        }

        return string.Join("  ·  ", parts);
    }

    private void UpdateImportButtonState()
    {
        BtnImportSelected.IsEnabled = _splitEntries.Any(entry => entry.CheckBox.IsChecked == true);
    }

    private void BtnImportSelected_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        foreach (var (file, split, checkBox) in _splitEntries)
        {
            if (checkBox.IsChecked == true)
            {
                SelectedSplits.Add((file, split));
            }
        }

        Close();
    }

    private void BtnCancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
