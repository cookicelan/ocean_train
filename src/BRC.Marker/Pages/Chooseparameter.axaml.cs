using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BRC.Marker.Pages;

public partial class Chooseparameter : UserControl
{
    private string? _selectedPath;
    private bool _isDirectory = false;
    private string? _okDataPath;

    public event EventHandler<SpectrogramConfig>? OnParametersConfirmed;
    private WindowNotificationManager? _manager;

    public Chooseparameter()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        _manager = new WindowNotificationManager(topLevel) { MaxItems = 3 };
    }

    private int GetComboBoxIntValue(ComboBox? comboBox, int defaultValue)
    {
        if (comboBox?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out int value))
        {
            return value;
        }
        return defaultValue;
    }

    private int GetSampleRateValue()
    {
        if (ChooseRate?.SelectedItem is ComboBoxItem item)
        {
            string content = item.Content?.ToString() ?? "";
            content = content.Replace("hz", "", StringComparison.OrdinalIgnoreCase);
            if (int.TryParse(content, out int rate)) return rate;
        }
        return 25600;
    }

    private string GetCurrentFilePattern()
    {
        if (WavRadioButton.IsChecked == true) return "*.wav";
        if (CsvRadioButton.IsChecked == true) return "*.csv";
        if (TdmsRadioButton.IsChecked == true) return "*.tdms";
        return "*.*";
    }

    private void SetPath(string path)
    {
        _selectedPath = path;

        if (Directory.Exists(path))
        {
            _isDirectory = true;
            _manager?.Show(new Notification("提示", "已加载文件夹，将遍历其内部文件", NotificationType.Information));
        }
        else if (File.Exists(path))
        {
            _isDirectory = false;
            _manager?.Show(new Notification("提示", "已加载单个文件", NotificationType.Information));
        }

        if (ButtonClick_Choosefile != null)
            Choosefile.Content = _isDirectory ? "已选文件夹" : "已选文件";
    }

    private async void ButtonClick_Choosefile(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var fileTypes = new List<FilePickerFileType>();
        if (WavRadioButton.IsChecked == true)
            fileTypes.Add(new FilePickerFileType("Wav Data") { Patterns = new[] { "*.wav" } });
        else if (CsvRadioButton.IsChecked == true)
            fileTypes.Add(new FilePickerFileType("CSV Data") { Patterns = new[] { "*.csv" } });
        else
            fileTypes.Add(FilePickerFileTypes.All);

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择数据文件",
            AllowMultiple = false,
            FileTypeFilter = fileTypes
        });

        if (files.Count >= 1)
        {
            SetPath(files[0].Path.LocalPath);
            if (sender is Button btn) btn.Content = "文件已就绪";
        }
    }

    private async void ButtonClick_ChooseFolder(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择包含数据的文件夹",
            AllowMultiple = false
        });

        if (folders.Count >= 1)
        {
            SetPath(folders[0].Path.LocalPath);
            if (sender is Button btn) btn.Content = "文件夹已就绪";
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFileNames();
        if (files == null || !files.Any()) return;

        var path = files.First();

        SetPath(path);
        Debug.WriteLine("AAAAAAAAAAA");
    }

    private void Algorithm_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (PanelOkData != null && OKRadioButton != null)
        {
            PanelOkData.IsVisible = OKRadioButton.IsChecked == true;
        }
    }

    private async void BtnBrowseOkData_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择含有OK正常数据的文件夹",
            AllowMultiple = false
        });

        if (folders.Count >= 1)
        {
            _okDataPath = folders[0].Path.LocalPath;
            TxtOkDataPath.Text = _okDataPath;
        }
    }

    private void ButtonClick_Start(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedPath))
        {
            _manager?.Show(new Notification("提示", "请先选择需要处理的文件或者文件夹", NotificationType.Warning));
            return;
        }

        if (OKRadioButton.IsChecked == true && string.IsNullOrEmpty(_okDataPath))
        {
            _manager?.Show(new Notification("提示", "请您额外再选择OK正常数据目录", NotificationType.Warning));
            return;
        }

        var config = new SpectrogramConfig
        {
            InputPath = _selectedPath,
            IsDirectory = _isDirectory,
            WindowSize = GetComboBoxIntValue(windowsize, 256),
            HopSize = GetComboBoxIntValue(stepsize, 64),
            SampleRate = GetSampleRateValue(),
            IsSelfNorm = SelfRadioButton.IsChecked == true,
            FilePattern = GetCurrentFilePattern(),

            UseCombinedOkAlgorithm = OKRadioButton.IsChecked == true,
            OkDataFolderPath = _okDataPath ?? ""
        };

        OnParametersConfirmed?.Invoke(this, config);

        Console.WriteLine("参数已传递，准备跳转预览...");
    }
}
