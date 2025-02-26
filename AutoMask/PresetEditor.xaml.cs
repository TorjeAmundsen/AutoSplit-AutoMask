using System.Windows;
using System.Windows.Controls;

namespace AutoSplit_AutoMask;

public partial class PresetEditor
{
    public List<SplitPreset> Presets { get; set; }
    public PresetEditor(List<SplitPreset> presets)
    {
        InitializeComponent();
        
        Presets = presets;
        CreatePresetsList();
    }

    private void CreatePresetsList()
    {
        Grid table = new();

        int rows = Presets.Count;
        
        int cols = 3;

        for (int i = 0; i < rows; ++i)
        {
            table.RowDefinitions.Add(new RowDefinition());
        }

        for (int i = 0; i < cols; ++i)
        {
            table.ColumnDefinitions.Add(new ColumnDefinition());
        }
        
        TextBlock headerGameName = new TextBlock
        {
            Text = "Game"
        };
        
        TextBlock headerPresetName = new TextBlock
        {
            Text = "Preset"
        };

        TextBlock headerSplitsCount = new TextBlock
        {
            Text = "Splits"
        };
        
        Grid.SetRow(headerGameName, 0);
        Grid.SetRow(headerPresetName, 0);
        Grid.SetRow(headerSplitsCount, 0);
        Grid.SetColumn(headerGameName, 0);
        Grid.SetColumn(headerPresetName, 1);
        Grid.SetColumn(headerSplitsCount, 2);
        
        table.Children.Add(headerGameName);
        table.Children.Add(headerPresetName);
        table.Children.Add(headerSplitsCount);

        for (int i = 1; i < rows; ++i)
        {
            for (int j = 0; j < cols; ++j)
            {
                TextBlock textBlock = new TextBlock
                {
                    Text = Presets[i - 1].GameName,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5),
                };
                
                Grid.SetRow(textBlock, i);
                Grid.SetColumn(textBlock, j);
                
                table.Children.Add(textBlock);
            }
        }

        for (int i = 0; i < rows; ++i)
        {
            for (int j = 0; j < cols; ++j)
            {
                Border border = new Border
                {
                    BorderBrush = SystemColors.ControlDarkBrush,
                    BorderThickness = new Thickness(1),
                };
                
                Grid.SetRow(border, i);
                Grid.SetColumn(border, j);
                
                table.Children.Add(border);
            }
        }
        
        MainGrid.Children.Add(table);
    }

    private void CreateSplitsList(SplitPreset splitPreset)
    {
        if (splitPreset.Splits == null || splitPreset.Splits.Count == 0)
        {
            MessageBox.Show("Selected split preset has no splits!");
            return;
        }
        Grid table = new();

        int cols = 8;
        int rows = splitPreset.Splits.Count;
    }
}