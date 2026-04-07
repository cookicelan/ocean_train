using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;
using BRC.Marker.Pages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BRC.Marker.Pages;

public class PreviewImageItem
{
    public Bitmap? Image { get; set; }
    public string Name { get; set; } = "";
}

public class ChannelGroup
{
    public string ChannelName { get; set; } = "";
    public List<PreviewImageItem> Images { get; set; } = new();
}

public partial class DataEXshow : UserControl
{
    public event EventHandler? OnBackRequested;
    private SpectrogramConfig? _config;

    [DllImport("BrcAudioCore.dll", EntryPoint = "GenerateSpectrogramEx", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GenerateSpectrogramEx(
        byte[] inputPath, byte[] outputFolder,
        int windowLength, int hopLength,
        int useOkAlgorithm, byte[] okDataFolder);

    public DataEXshow()
    {
        InitializeComponent();
    }

    private byte[] GetUtf8Bytes(string str)
    {
        if (string.IsNullOrEmpty(str)) return new byte[] { 0 };
        return Encoding.UTF8.GetBytes(str + "\0");
    }

    public async void LoadPreviewData(SpectrogramConfig config)
    {
        _config = config;
        ChannelTabs.ItemsSource = null;
        ProgressPanel.IsVisible = true;

        string? firstFile = config.IsDirectory
            ? Directory.GetFiles(config.InputPath, config.FilePattern).FirstOrDefault()
            : config.InputPath;

        if (string.IsNullOrEmpty(firstFile))
        {
            CurrentFileText.Text = "路径下未找到文件";
            ProgressPanel.IsVisible = false;
            return;
        }

        CurrentFileText.Text = $"预览源文件: {Path.GetFileName(firstFile)}";

        try
        {
            string previewOutDir = Path.Combine(Path.GetDirectoryName(firstFile)!, "SpectroOutput_Preview");
            Directory.CreateDirectory(previewOutDir);

            foreach (var oldFile in Directory.GetFiles(previewOutDir, "*.png"))
            {
                File.Delete(oldFile);
            }

            byte[] inBytes = GetUtf8Bytes(firstFile);
            byte[] outBytes = GetUtf8Bytes(previewOutDir);

            int useOk = _config.UseCombinedOkAlgorithm ? 1 : 0;
            string okFolder = _config.UseCombinedOkAlgorithm ? (_config.OkDataFolderPath ?? "") : "";
            byte[] okBytes = GetUtf8Bytes(okFolder);

            ProgressText.Text = _config.UseCombinedOkAlgorithm ? "正在调用核函计算引擎生成预览..." : "正在生成自适应预览...";

            int status = await Task.Run(() =>
                GenerateSpectrogramEx(inBytes, outBytes, config.WindowSize, config.HopSize, useOk, okBytes)
            );

            if (status != 1)
            {
                CurrentFileText.Text = "预览失败: 底层算法执行异常";
                ProgressPanel.IsVisible = false;
                return;
            }

            var imgPaths = Directory.GetFiles(previewOutDir, "*.png");
            var groupedChannels = new Dictionary<string, List<PreviewImageItem>>();

            foreach (var path in imgPaths)
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var imageItem = new PreviewImageItem
                {
                    Image = new Bitmap(fs),
                    Name = Path.GetFileName(path)
                };

                var match = Regex.Match(imageItem.Name, @"_CH_(\d+)");
                string channelName = match.Success ? $"通道 {match.Groups[1].Value}" : "无通道";

                if (!groupedChannels.ContainsKey(channelName))
                    groupedChannels[channelName] = new List<PreviewImageItem>();

                groupedChannels[channelName].Add(imageItem);
            }

            ChannelTabs.ItemsSource = groupedChannels.Select(kv => new ChannelGroup
            {
                ChannelName = kv.Key,
                Images = kv.Value
            }).OrderBy(c => c.ChannelName).ToList();
        }
        catch (Exception ex)
        {
            CurrentFileText.Text = $"处理过程出现错误: {ex.Message}";
        }
        finally
        {
            ProgressPanel.IsVisible = false;
        }
    }

    private async void ButtonClick_ExecuteAll(object? sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        var btn = sender as Button;
        if (btn != null) btn.IsEnabled = false;

        ProgressPanel.IsVisible = true;

        try
        {
            List<string> files = _config.IsDirectory
                ? Directory.GetFiles(_config.InputPath, _config.FilePattern).ToList()
                : new List<string> { _config.InputPath };

            MainProgressBar.Maximum = files.Count;
            MainProgressBar.Value = 0;

            int count = 0;

            int useOk = _config.UseCombinedOkAlgorithm ? 1 : 0;
            string okFolder = _config.UseCombinedOkAlgorithm ? (_config.OkDataFolderPath ?? "") : "";
            byte[] okBytes = GetUtf8Bytes(okFolder);

            foreach (var file in files)
            {
                ProgressText.Text = $"正在处理 ({count + 1}/{files.Count}): {Path.GetFileName(file)}";

                string folderSuffix = _config.UseCombinedOkAlgorithm ? "SpectroOutput_OK增强" : "SpectroOutput_自适应";
                string outDir = Path.Combine(Path.GetDirectoryName(file)!, folderSuffix);
                Directory.CreateDirectory(outDir);

                byte[] inBytes = GetUtf8Bytes(file);
                byte[] outBytes = GetUtf8Bytes(outDir);

                int status = await Task.Run(() =>
                    GenerateSpectrogramEx(inBytes, outBytes, _config.WindowSize, _config.HopSize, useOk, okBytes)
                );

                if (status != 1)
                {
                    Console.WriteLine($"文件 {Path.GetFileName(file)} 处理失败");
                }

                count++;
                MainProgressBar.Value = count;
            }

            ProgressText.Text = "全部文件处理完成！";
        }
        catch (Exception ex)
        {
            ProgressText.Text = $"处理过程出现错误: {ex.Message}";
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    private void Button_Back(object? sender, RoutedEventArgs e)
    {
        OnBackRequested?.Invoke(this, EventArgs.Empty);
    }
}
