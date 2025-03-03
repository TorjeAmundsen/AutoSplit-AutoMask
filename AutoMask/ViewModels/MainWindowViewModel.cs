using System.Collections.Generic;
using AutoMask.Models;
using Avalonia.Media.Imaging;

namespace AutoMask.ViewModels;

public class MainWindowViewModel
{
    public List<string> PresetComboBoxItems { get; set; }
    public List<string> SplitsComboBoxItems { get; set; }
    public List<string> InputImageComboBoxItems { get; set; }
    public int SelectedPresetIndex { get; set; }
    public int SelectedSplitIndex { get; set; }
    public List<string> InputImagePaths { get; set; }
    public string SelectedInputImagePath { get; set; }
    public string OutputDirectoryPath { get; set; }
    public string AlphaImagePath { get; set; }
    public Bitmap OutputImage { get; set; }
    public List<SplitPreset> SplitPresets { get; set; }

    public MainWindowViewModel()
    {
        PresetComboBoxItems = new List<string>();
        SplitsComboBoxItems = new List<string>();
        InputImageComboBoxItems = new List<string>();
    }
}