using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using AutoMask.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AutoMask.Views;

public partial class MainWindow : Window
{
    private readonly string AutoMaskSemVer = "0.2.0";
    private readonly string? VersionSuffix = "alpha";
    
    private List<string> LoadedInputImagePaths { get; set; } = new();
    private List<string> LoadedAlphaImagePaths { get; set; } = new();

    private string? selectedInputImagePath => LoadedInputImagePaths.Count > 0 && ComboBoxSelectInputImage.SelectedIndex < LoadedInputImagePaths.Count ? LoadedInputImagePaths[ComboBoxSelectInputImage.SelectedIndex] : null;
    private string? selectedAlphaImagePath => LoadedAlphaImagePaths.Count > 0 ? LoadedAlphaImagePaths[ComboBoxSelectSplit.SelectedIndex] : null;

    public MainWindow()
    {
        InitializeComponent();

        ComboBoxSelectInputImage.Items.Add("No input images loaded");

        Title = "AutoMask v" + AutoMaskSemVer + (string.IsNullOrEmpty(VersionSuffix) ? string.Empty : "-" + VersionSuffix);
    }

    private async void BtnLoadInputImage_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select input image(s) to load",
            AllowMultiple = true,
        });

        if (files.Count >= 1)
        {
            ComboBoxSelectInputImage.SelectedIndex = 0;
            ComboBoxSelectInputImage.Items.Clear();
            LoadedInputImagePaths.Clear();
            
            foreach (var file in files)
            {
                ComboBoxSelectInputImage.Items.Add(file.Name);
                LoadedInputImagePaths.Add(file.Path.LocalPath);
            }
            
            ComboBoxSelectInputImage.IsEnabled = true;

            if (ComboBoxSelectInputImage.Items.Count > 0)
            {
                ComboBoxSelectInputImage.SelectedIndex = 0;
            }
        }
    }

    private void BtnOpenOutputFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void BtnOpenPresetsFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void ComboBoxSelectInputImage_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        Console.WriteLine(selectedInputImagePath);
    }

    private void BtnNextAlphaImage_OnClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void BtnPrevAlphaImage_OnClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void BtnSelectOutputDirectory_OnClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void BtnNextInputImage_OnClick_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectInputImage.SelectedIndex == ComboBoxSelectInputImage.Items.Count - 1)
        {
            return;
        }
        
        ComboBoxSelectInputImage.SelectedIndex += 1;
    }

    private void BtnPrevInputImage_OnClick_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ComboBoxSelectInputImage.SelectedIndex == 0)
        {
            return;
        }
        
        ComboBoxSelectInputImage.SelectedIndex -= 1;
    }

    private void ComboBoxSelectSplit_OnSelectionChanged_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void BtnLoadCustomAlphaImage_OnClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void BtnSave_OnClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void BtnSaveAs_OnClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }
}