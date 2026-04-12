using Avalonia.Controls;
using Avalonia.Media;

namespace AutoSplit_AutoMask;

public partial class ImportSplitsDialog : Window
{
    private readonly List<(PremadeSplitsFile File, Split Split, CheckBox CheckBox)> _splitEntries = [];

    public List<(PremadeSplitsFile File, Split Split)> SelectedSplits { get; } = [];

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
                Margin = new Avalonia.Thickness(0, 6, 0, 2),
            };
            SplitsPanel.Children.Add(header);

            foreach (var split in splitsFile.Splits)
            {
                var checkBox = new CheckBox
                {
                    Content = split.Name,
                    Margin = new Avalonia.Thickness(8, 0, 0, 0),
                };
                checkBox.IsCheckedChanged += (_, _) => UpdateImportButtonState();

                _splitEntries.Add((splitsFile, split, checkBox));
                SplitsPanel.Children.Add(checkBox);
            }
        }
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
