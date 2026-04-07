using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BRC.Marker.Pages;

public partial class Trainer : UserControl
{
    private WindowNotificationManager? _manager;

    private string _selectedModelPath = "";

    private string _selectedDataFolderPath = "";

    private string _selectedExePath = "";
    private ObservableCollection<string> _trainList = new ObservableCollection<string>();
    private ObservableCollection<string> _testList = new ObservableCollection<string>();
    public class TrainConfig
    {
        public double TrainPercent { get; set; }
        public string ExePath { get; set; } = string.Empty;
        public string ModelPath { get; set; } = string.Empty;
        public string InitLr { get; set; } = "0.0000333";
        public int NumWorkers { get; set; } = 4;
        public bool Cuda { get; set; } = true;
        public int InitEpoch { get; set; } = 0;
        public int FreezeEpoch { get; set; } = 50;
        public int FreezeBatchSize { get; set; } = 8;
        public int UnfreezeEpoch { get; set; } = 200;
        public int UnfreezeBatchSize { get; set; } = 8;
    }

    public Trainer()
    {
        InitializeComponent();

        TrainDataListBox.ItemsSource = _trainList;
        TestDataListBox.ItemsSource = _testList;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        _manager = new WindowNotificationManager(topLevel) { MaxItems = 3 };
    }

    private async void ButtonClick_ChooseModel(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "选择模型文件",
            AllowMultiple = false,
            Filters = new List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "PyTorch Model", Extensions = { "pth" } }
            }
        };

        var result = await dialog.ShowAsync(window);
        if (result != null && result.Length > 0)
        {
            SetModel(result[0]);
        }
    }

    private async void ButtonClick_ChooseData(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;

        var dialog = new OpenFolderDialog
        {
            Title = "选择包含数据的文件夹"
        };

        var path = await dialog.ShowAsync(window);
        if (!string.IsNullOrEmpty(path))
        {
            ProcessDataFolder(path);
        }
    }

    private async void ButtonClick_ChooseExe(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "选择训练程序 (exe)",
            AllowMultiple = false,
            Filters = new List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "Executable Files", Extensions = { "exe" } }
            }
        };

        var result = await dialog.ShowAsync(window);
        if (result != null && result.Length > 0)
        {
            _selectedExePath = result[0];
            _manager?.Show(new Notification("成功", $"已选择程序: {System.IO.Path.GetFileName(_selectedExePath)}", NotificationType.Success));

            ChooseExeName.Text = $"当前程序: {System.IO.Path.GetFileName(_selectedExePath)}";
        }
    }

    private void ProcessDataFolder(string folderPath)
    {
        _selectedDataFolderPath = folderPath;
        var dir = new DirectoryInfo(folderPath);
        if (!dir.Exists) return;

        var files = dir.GetFiles().Where(f =>
            f.Extension.EndsWith("jpg", StringComparison.OrdinalIgnoreCase) ||
            f.Extension.EndsWith("png", StringComparison.OrdinalIgnoreCase) ||
            f.Extension.EndsWith("bmp", StringComparison.OrdinalIgnoreCase)).Select(f => f.Name).ToList();

        if (files.Count == 0)
        {
            _manager?.Show(new Notification("提示", "该文件夹下没有图片文件", NotificationType.Warning));
            return;
        }

        if (!int.TryParse(RatioTrainBox.Text, out int trainRatio) || !int.TryParse(RatioTestBox.Text, out int testRatio))
        {
            trainRatio = 7;
            testRatio = 3;
        }

        var rng = new Random();
        var shuffledFiles = files.OrderBy(x => rng.Next()).ToList();

        double totalRatio = trainRatio + testRatio;
        int trainCount = (int)(shuffledFiles.Count * (trainRatio / totalRatio));

        _trainList.Clear();
        _testList.Clear();

        for (int i = 0; i < shuffledFiles.Count; i++)
        {
            if (i < trainCount)
                _trainList.Add(shuffledFiles[i]);
            else
                _testList.Add(shuffledFiles[i]);
        }

        _manager?.Show(new Notification("成功", $"数据已加载: {_trainList.Count} 训练 / {_testList.Count} 测试", NotificationType.Success));
    }

    private void ButtonClick_StartTrain(object? sender, RoutedEventArgs e)
    {
        if (!double.TryParse(RatioTrainBox.Text, out double trainRatio)) trainRatio = 7;
        if (!double.TryParse(RatioTestBox.Text, out double testRatio)) testRatio = 3;

        double calculatedTrainPercent = trainRatio / (trainRatio + testRatio);

        var parentWindow = TopLevel.GetTopLevel(this) as Window;

        if (parentWindow == null)
        {
            _manager?.Show(new Notification("错误", "无法获取主窗口", NotificationType.Error));
            return;
        }

        if (string.IsNullOrEmpty(_selectedModelPath))
        {
            _manager?.Show(new Notification("错误", "请先选择模型", NotificationType.Error));
            return;
        }

        if (string.IsNullOrEmpty(_selectedExePath))
        {
            _manager?.Show(new Notification("错误", "请先选择训练程序(exe)位置", NotificationType.Error));
            return;
        }

        var config = new TrainConfig
        {
            TrainPercent = calculatedTrainPercent,
            ExePath = _selectedExePath,
            ModelPath = _selectedModelPath,
            InitLr = InputInitLr.Text ?? "0.0000333",
            NumWorkers = (int)(InputNumWorkers.Value ?? 4),
            Cuda = InputCuda.IsChecked ?? true,
            InitEpoch = (int)(InputInitEpoch.Value ?? 0),
            FreezeEpoch = (int)(InputFreezeEpoch.Value ?? 50),
            FreezeBatchSize = (int)(InputFreezeBatchSize.Value ?? 8),
            UnfreezeEpoch = (int)(InputUnfreezeEpoch.Value ?? 200),
            UnfreezeBatchSize = (int)(InputUnfreezeBatchSize.Value ?? 8)
        };

        var progressWindow = new TrainingProgressWindow(config);
        progressWindow.ShowDialog(parentWindow);
    }

    private void SetModel(string path)
    {
        if (System.IO.Path.GetExtension(path).ToLower() != ".pth")
        {
            _manager?.Show(new Notification("错误", "仅支持 .pth 格式的模型", NotificationType.Error));
            return;
        }
        _selectedModelPath = path;
        TrainModelName.Text = $"当前模型: {System.IO.Path.GetFileName(path)}";
        _manager?.Show(new Notification("模型加载", "模型路径已保存", NotificationType.Success));
    }

    private void SetExe(string path)
    {
        if (System.IO.Path.GetExtension(path).ToLower() != ".exe")
        {
            _manager?.Show(new Notification("错误", "仅支持 .exe 格式的文件", NotificationType.Error));
            return;
        }
        _selectedExePath = path;
        ChooseExeName.Text = $"当前程序: {System.IO.Path.GetFileName(path)}";
        _manager?.Show(new Notification("程序加载", $"已选择程序: {System.IO.Path.GetFileName(path)}", NotificationType.Success));
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFileNames();
        if (files == null || !files.Any()) return;
        var path = files.First();

        if (File.Exists(path))
        {
            string extension = System.IO.Path.GetExtension(path).ToLower();

            if (extension == ".exe")
            {
                SetExe(path);
            }
            else if (extension == ".pth")
            {
                SetModel(path);
            }
            else
            {
                _manager?.Show(new Notification("错误", "不支持的文件格式，请拖入 .pth 或 .exe 文件", NotificationType.Error));
            }
        }
        else if (Directory.Exists(path))
        {
            ProcessDataFolder(path);
        }
        else
        {
            _manager?.Show(new Notification("错误", "无法识别该路径类型", NotificationType.Error));
        }
    }
}
