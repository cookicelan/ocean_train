using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BRC.Marker.Pages
{
    public class DeductionImageItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public Bitmap? Thumbnail { get; set; }
    }

    public class DeductionConfig
    {
        public string ExePath { get; set; } = string.Empty;
        public string ModelPath { get; set; } = string.Empty;
        public string InputDir { get; set; } = string.Empty;
        public string OutputDir { get; set; } = string.Empty;
        public double Confidence { get; set; } = 0.7;
    }

    public partial class Deduction : UserControl
    {
        private WindowNotificationManager? _manager;
        private string _selectedModelPath = "";
        private string _selectedDataFolderPath = "";
        private string _selectedExePath = "";
        private string _selectedOutputPath = "";

        private ObservableCollection<DeductionImageItem> _imageList = new ObservableCollection<DeductionImageItem>();

        private string[] _classNames = new string[] { "object" };

        public Deduction()
        {
            InitializeComponent();
            TrainDataListBox.ItemsSource = _imageList;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            var topLevel = TopLevel.GetTopLevel(this);
            _manager = new WindowNotificationManager(topLevel) { MaxItems = 3 };
        }

        private async void ButtonClick_output(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择保存推理图片输出的文件夹"
            });

            if (folders != null && folders.Count > 0)
            {
                _selectedOutputPath = folders[0].Path.LocalPath;
                OutputPathText.Text = $"保存路径: {_selectedOutputPath}";
                _manager?.Show(new Notification("成功", "已选择保存路径", NotificationType.Success));
            }
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
                    new FileDialogFilter { Name = "Model Files", Extensions = { "pth" } }
                }
            };

            var result = await dialog.ShowAsync(window);
            if (result != null && result.Length > 0)
            {
                SetModel(result[0]);
            }
        }

        private async void ButtonClick_ChooseFile(object? sender, RoutedEventArgs e)
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
                Title = "选择推理程序",
                AllowMultiple = false,
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "Executable Files", Extensions = { "exe" } }
                }
            };
            var result = await dialog.ShowAsync(window);
            if (result != null && result.Length > 0)
            {
                SetExe(result[0]);
            }
        }

        private void ButtonClick_StartDeduction(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedExePath) ||
                string.IsNullOrEmpty(_selectedModelPath) ||
                string.IsNullOrEmpty(_selectedDataFolderPath) ||
                string.IsNullOrEmpty(_selectedOutputPath))
            {
                _manager?.Show(new Notification("错误", "请先选择模型、数据、程序以及保存路径！", NotificationType.Error));
                return;
            }

            var config = new DeductionConfig
            {
                ExePath = _selectedExePath,
                ModelPath = _selectedModelPath,
                InputDir = _selectedDataFolderPath,
                OutputDir = _selectedOutputPath,
                Confidence = (double)(InputConfidence.Value ?? 0.7m)
            };

            var window = new Deduction_resultshow(config);
            window.Show();
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

        private void SetModel(string path)
        {
            var ext = System.IO.Path.GetExtension(path).ToLower();
            if (ext != ".pth")
            {
                _manager?.Show(new Notification("错误", "仅支持 .pth 格式", NotificationType.Error));
                return;
            }

            _selectedModelPath = path;
            DeductionModelName.Text = $"当前模型: {System.IO.Path.GetFileName(path)}";
            _manager?.Show(new Notification("成功", "模型路径已保存", NotificationType.Success));
        }

        private void ProcessDataFolder(string folderPath)
        {
            _selectedDataFolderPath = folderPath;
            var dir = new DirectoryInfo(folderPath);
            if (!dir.Exists) return;

            var files = dir.GetFiles().Where(f =>
                f.Extension.EndsWith("jpg", StringComparison.OrdinalIgnoreCase) ||
                f.Extension.EndsWith("png", StringComparison.OrdinalIgnoreCase) ||
                f.Extension.EndsWith("bmp", StringComparison.OrdinalIgnoreCase)).ToList();

            if (files.Count == 0)
            {
                _manager?.Show(new Notification("提示", "该文件夹下没有图片文件", NotificationType.Warning));
                return;
            }

            _imageList.Clear();

            foreach (var file in files)
            {
                try
                {
                    using var stream = file.OpenRead();
                    var bitmap = Bitmap.DecodeToWidth(stream, 100);

                    _imageList.Add(new DeductionImageItem
                    {
                        FileName = file.Name,
                        FullPath = file.FullName,
                        Thumbnail = bitmap
                    });
                }
                catch
                {
                }
            }

            _manager?.Show(new Notification("成功", $"数据已加载: {_imageList.Count} 张图片", NotificationType.Success));
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
}
