using System;
using System.Collections.Generic;
using AutoMask.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AutoMask.Views;

public partial class MainWindow : Window
{
    private readonly string AutoMaskSemVer = "0.2.0";
    private readonly string? VersionSuffix = "alpha";
    
    public List<string> PresetComboBoxItems { get; set; }
    public List<string> SplitsComboBoxItems { get; set; }
    public List<string> InputImageComboBoxItems { get; set; }
    public MainWindow()
    {
        InitializeComponent();

        DataContext = this;

        Title = "AutoMask v" + AutoMaskSemVer + (string.IsNullOrEmpty(VersionSuffix) ? string.Empty : "-" + VersionSuffix);
    }
    
    public string TestString { get; set; } = "AutoMask";

    private void BtnLoadInputImage_OnClick(object? sender, RoutedEventArgs e)
    {
        BtnLoadInputImage.Content = "Foo bar";
    }

    private void BtnOpenOutputFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        BtnLoadInputImage.Content = "https://github.com/AutoMask/AutoMask";
        Console.WriteLine(TestString);
    }

    private void BtnOpenPresetsFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void ComboBoxSelectInputImage_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        throw new System.NotImplementedException();
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
        throw new System.NotImplementedException();
    }

    private void BtnPrevInputImage_OnClick_OnClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
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