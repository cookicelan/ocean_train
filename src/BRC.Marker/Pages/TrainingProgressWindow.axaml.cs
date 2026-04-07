using System.Text.Json;
using System.Xml.Linq;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static BRC.Marker.Pages.Trainer;
using System.Text;

namespace BRC.Marker;

public partial class TrainingProgressWindow : Window
{
    private TrainConfig _config;
    private int _lastProgress = 0;

    // 【修改1】将进程声明为类级别的变量，以便跨方法控制
    private Process _trainingProcess;

    public TrainingProgressWindow(TrainConfig config)
    {
        InitializeComponent();
        _config = config;
        this.Loaded += (sender, args) => StartTraining();
    }

    // 【修改2】重写窗口关闭事件，当用户点击右上角X时触发
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        try
        {
            // 检查进程是否正在运行
            if (_trainingProcess != null && !_trainingProcess.HasExited)
            {
                // Kill(true) 会终结整个进程树，包括 exe 衍生出的 Python 多线程子进程
                _trainingProcess.Kill(true);
                _trainingProcess.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"关闭进程时发生异常: {ex.Message}");
        }
    }

    public async void StartTraining()
    {
        StatusText.Text = "正在进行数据预处理...";
        TrainingProgressBar.IsIndeterminate = true;
        _lastProgress = 0;
        LogTextBox.Text = ""; // 清空上次的日志

        // 获取用户设置的训练集比例（假设默认 0.9，你可以从 _config 中传入这个值）
        double userTrainPercent = _config.TrainPercent;

        // 基于 exe 所在的根目录
        string baseDir = System.IO.Path.GetDirectoryName(_config.ExePath);

        try
        {
            // ================= 1. 执行 C# 原生数据预处理 =================
            await PreprocessDatasetAsync(baseDir, userTrainPercent);

            // ================= 2. 预处理完成，启动深度学习训练 =================
            StatusText.Text = "预处理完成！正在启动底层 AI 引擎...";

            await Task.Run(() =>
            {
                string cudaStr = _config.Cuda ? "True" : "False";
                string arguments = $"--init_lr {_config.InitLr} " +
                                   $"--num_workers {_config.NumWorkers} " +
                                   $"--cuda {cudaStr} " +
                                   $"--model_path \"{_config.ModelPath}\" " +
                                   $"--init_epoch {_config.InitEpoch} " +
                                   $"--freeze_epoch {_config.FreezeEpoch} " +
                                   $"--freeze_batch_size {_config.FreezeBatchSize} " +
                                   $"--unfreeze_epoch {_config.UnfreezeEpoch} " +
                                   $"--unfreeze_batch_size {_config.UnfreezeBatchSize}";

                string exeFullPath = _config.ExePath;

                var psi = new ProcessStartInfo
                {
                    FileName = exeFullPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    WorkingDirectory = baseDir // 工作目录定位在 exe 所在文件夹
                };

                // 强制环境变量，杜绝 Python 输出乱码或卡进度
                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                _trainingProcess = new Process { StartInfo = psi };
                _trainingProcess.OutputDataReceived += ParsePythonLog;
                _trainingProcess.ErrorDataReceived += ParsePythonLog;

                _trainingProcess.Start();
                _trainingProcess.BeginOutputReadLine();
                _trainingProcess.BeginErrorReadLine();
                _trainingProcess.WaitForExit();

                // 训练结束更新 UI
                Dispatcher.UIThread.Post(() =>
                {
                    StatusText.Text = "训练完成！";
                    TrainingProgressBar.IsIndeterminate = false;
                    TrainingProgressBar.Value = 100;
                    LogTextBox.Text += "\r\n[系统]: 训练流程已全部结束。";
                });
            });
        }
        catch (Exception ex)
        {
            // 捕获预处理或启动训练中的任何报错
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = $"流程异常终止";
                TrainingProgressBar.IsIndeterminate = false;
                LogTextBox.Text += $"\r\n[致命错误]: {ex.Message}";
            });
        }
    }

    private void ParsePythonLog(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Data)) return;

        string line = args.Data.Trim();

        Dispatcher.UIThread.Post(() =>
        {
            // 【修改4】将接收到的每一行日志实时追加到 TextBox 中，并自动滚动到底部
            LogTextBox.Text += line + Environment.NewLine;
            LogTextBox.CaretIndex = LogTextBox.Text.Length;

            // --- 原本的解析逻辑保持不变 ---
            var epochProgressMatch = Regex.Match(line, @"Epoch\s*(\d+)/(\d+):\s*(\d+)%");

            if (epochProgressMatch.Success)
            {
                if (int.TryParse(epochProgressMatch.Groups[1].Value, out int currentEpoch) &&
                    int.TryParse(epochProgressMatch.Groups[2].Value, out int totalEpochs) &&
                    int.TryParse(epochProgressMatch.Groups[3].Value, out int batchPercentage))
                {
                    double baseProgress = (currentEpoch - 1) * 100.0 / totalEpochs;
                    double currentEpochProgress = (batchPercentage / 100.0) * (100.0 / totalEpochs);
                    int totalProgress = (int)(baseProgress + currentEpochProgress);

                    if (totalProgress > _lastProgress)
                    {
                        _lastProgress = totalProgress;
                    }

                    TrainingProgressBar.IsIndeterminate = false;
                    TrainingProgressBar.Value = _lastProgress;
                    StatusText.Text = $"正在训练: Epoch {currentEpoch}/{totalEpochs} ({batchPercentage}%)";
                }
            }
            else if (line.Contains("Epoch:") && !line.Contains("%"))
            {
                var epochMatch = Regex.Match(line, @"Epoch:(\d+)/(\d+)");
                if (epochMatch.Success)
                {
                    StatusText.Text = $"Epoch {epochMatch.Groups[1].Value}/{epochMatch.Groups[2].Value} 结算中...";
                }
            }
            else if (line.Contains("Total Loss") || line.Contains("mAP =") || line.Contains("val_loss"))
            {
                StatusText.Text = line;
            }
            else if (line.Contains("Start Validation"))
            {
                StatusText.Text = "正在进行验证集评估...";
            }
            else if (line.Contains("Calculate Map"))
            {
                StatusText.Text = "正在计算 mAP...";
            }
        });
    }

    // 数据集预处理：JSON转XML -> 划分训练集验证集 -> 生成对应的TXT文件
    private async Task PreprocessDatasetAsync(string baseDir, double trainPercent)
    {
        string jpegDir = Path.Combine(baseDir, @"VOCdevkit\VOC2007\JPEGImages");
        string xmlDir = Path.Combine(baseDir, @"VOCdevkit\VOC2007\Annotations");
        string classesPath = Path.Combine(baseDir, @"model_data\classes_binary.txt");

        if (!Directory.Exists(jpegDir))
        {
            throw new Exception($"找不到图片数据集目录: {jpegDir}\r\n请确保图片和JSON文件已放入该文件夹。");
        }

        // 确保 XML 保存目录存在
        if (!Directory.Exists(xmlDir)) Directory.CreateDirectory(xmlDir);

        Dispatcher.UIThread.Post(() => LogTextBox.Text += "开始解析 JSON 并生成 XML...\r\n");

        // 1. 转换 JSON 为 XML
        var jsonFiles = Directory.GetFiles(jpegDir, "*.json");
        List<string> validImageIds = new List<string>();

        foreach (var jsonFile in jsonFiles)
        {
            string jsonContent = await File.ReadAllTextAsync(jsonFile);
            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // 获取图片基本信息
            string imagePath = root.GetProperty("imagePath").GetString() ?? "";
            string imageName = Path.GetFileName(imagePath);
            int width = root.GetProperty("imageWidth").GetInt32();
            int height = root.GetProperty("imageHeight").GetInt32();

            // 构建 XML 树
            XElement xmlRoot = new XElement("annotation",
                new XElement("folder", "images"),
                new XElement("filename", imageName),
                new XElement("source", "LabelMe"),
                new XElement("size",
                    new XElement("width", width),
                    new XElement("height", height),
                    new XElement("depth", 3)),
                new XElement("segmented", 0)
            );

            // 解析多边形并转换为 Bounding Box
            if (root.TryGetProperty("shapes", out JsonElement shapes))
            {
                foreach (var shape in shapes.EnumerateArray())
                {
                    string label = shape.GetProperty("label").GetString() ?? "";
                    var points = shape.GetProperty("points");

                    double xmin = double.MaxValue, ymin = double.MaxValue;
                    double xmax = double.MinValue, ymax = double.MinValue;

                    foreach (var point in points.EnumerateArray())
                    {
                        double x = point[0].GetDouble();
                        double y = point[1].GetDouble();
                        if (x < xmin) xmin = x;
                        if (y < ymin) ymin = y;
                        if (x > xmax) xmax = x;
                        if (y > ymax) ymax = y;
                    }

                    XElement obj = new XElement("object",
                        new XElement("name", label),
                        new XElement("pose", "Unspecified"),
                        new XElement("truncated", 0),
                        new XElement("difficult", 0),
                        new XElement("bndbox",
                            new XElement("xmin", (int)xmin),
                            new XElement("ymin", (int)ymin),
                            new XElement("xmax", (int)xmax),
                            new XElement("ymax", (int)ymax)
                        )
                    );
                    xmlRoot.Add(obj);
                }
            }

            // 保存 XML
            string baseName = Path.GetFileNameWithoutExtension(jsonFile);
            string xmlFile = Path.Combine(xmlDir, $"{baseName}.xml");
            xmlRoot.Save(xmlFile);

            validImageIds.Add(baseName); // 记录成功解析的文件名(无后缀)
        }

        Dispatcher.UIThread.Post(() => LogTextBox.Text += $"JSON 解析完毕，共生成 {validImageIds.Count} 个 XML 文件。\r\n");

        // 2. 读取模型分类标签 (classes_binary.txt)
        if (!File.Exists(classesPath))
        {
            throw new Exception($"找不到类别配置文件: {classesPath}");
        }
        var classes = (await File.ReadAllLinesAsync(classesPath))
                        .Select(c => c.Trim())
                        .Where(c => !string.IsNullOrEmpty(c))
                        .ToList();

        // 3. 打乱顺序并划分训练集与验证集
        Random rng = new Random();
        validImageIds = validImageIds.OrderBy(x => rng.Next()).ToList();

        int trainCount = (int)(validImageIds.Count * trainPercent);
        var trainIds = validImageIds.Take(trainCount).ToList();
        var valIds = validImageIds.Skip(trainCount).ToList();

        Dispatcher.UIThread.Post(() => LogTextBox.Text += $"数据集划分完毕，训练集: {trainIds.Count} 张，验证集: {valIds.Count} 张。\r\n");

        // 4. 生成 2007_train.txt 和 2007_val.txt
        await GenerateDatasetTxtAsync(trainIds, Path.Combine(baseDir, "2007_train.txt"), jpegDir, xmlDir, classes);
        await GenerateDatasetTxtAsync(valIds, Path.Combine(baseDir, "2007_val.txt"), jpegDir, xmlDir, classes);
    }

    // 读取对应的 XML，生成供 Python 训练器读取的 txt 格式行（生成TXT）
    private async Task GenerateDatasetTxtAsync(List<string> imageIds, string txtPath, string jpegDir, string xmlDir, List<string> classes)
    {
        // 注册提供程序以支持 GBK 编码（.NET Core / .NET 5+ 必须加这一句）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // 使用 GBK 编码写入 TXT 文件
        using StreamWriter writer = new StreamWriter(txtPath, false, Encoding.GetEncoding("GBK"));

        foreach (var id in imageIds)
        {
            // 匹配图片真实后缀 (优先匹配 png，其次 jpg)
            string imgPath = Path.Combine(jpegDir, $"{id}.png");
            if (!File.Exists(imgPath)) imgPath = Path.Combine(jpegDir, $"{id}.jpg");

            // Python底层通常接受正斜杠路径
            string absImgPath = imgPath.Replace("\\", "/");
            string line = absImgPath;

            // 加载刚才生成的 XML 获取框选坐标
            string xmlFile = Path.Combine(xmlDir, $"{id}.xml");
            if (File.Exists(xmlFile))
            {
                XDocument xmlDoc = XDocument.Load(xmlFile);
                foreach (var obj in xmlDoc.Root.Elements("object"))
                {
                    string clsName = obj.Element("name")?.Value ?? "";
                    if (!classes.Contains(clsName)) continue; // 过滤掉未登记的标签

                    int clsId = classes.IndexOf(clsName);
                    var bndbox = obj.Element("bndbox");
                    string xmin = bndbox.Element("xmin").Value;
                    string ymin = bndbox.Element("ymin").Value;
                    string xmax = bndbox.Element("xmax").Value;
                    string ymax = bndbox.Element("ymax").Value;

                    // 按照 Python 代码格式拼接: path xmin,ymin,xmax,ymax,class_id
                    line += $" {xmin},{ymin},{xmax},{ymax},{clsId}";
                }
            }
            await writer.WriteLineAsync(line);
        }
    }
}