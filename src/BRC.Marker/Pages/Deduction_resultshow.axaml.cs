using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BRC.Marker.Pages;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BRC.Marker;

public partial class Deduction_resultshow : Window
{
    private DeductionConfig _config;
    private Process _predictProcess;

    public Deduction_resultshow(DeductionConfig config)
    {
        InitializeComponent();
        _config = config;

        this.Loaded += (sender, args) => StartPredictAsync();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        try
        {
            if (_predictProcess != null)
            {
                try
                {
                    _ = _predictProcess.Id;

                    if (!_predictProcess.HasExited)
                    {
                        _predictProcess.Kill(true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    _predictProcess.Dispose();
                }
            }
        }
        catch { }
    }

    public async void StartPredictAsync()
    {
        DeductionedCountText.Text = "正在启动推理程序...";

        await Task.Run(() =>
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                if (!Directory.Exists(_config.OutputDir))
                {
                    Directory.CreateDirectory(_config.OutputDir);
                }

                string exePath = _config.ExePath;
                string workingDir = Path.GetDirectoryName(exePath);

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding("GBK"),
                    StandardErrorEncoding = Encoding.GetEncoding("GBK"),
                    WorkingDirectory = workingDir
                };

                psi.ArgumentList.Add("--model_path");
                psi.ArgumentList.Add(_config.ModelPath);

                psi.ArgumentList.Add("--confidence");
                psi.ArgumentList.Add(_config.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture));

                psi.ArgumentList.Add("--input_dir");
                psi.ArgumentList.Add(_config.InputDir);

                psi.ArgumentList.Add("--output_dir");
                psi.ArgumentList.Add(_config.OutputDir);

                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

                _predictProcess = new Process { StartInfo = psi };
                _predictProcess.OutputDataReceived += ParsePredictLog;
                _predictProcess.ErrorDataReceived += ParsePredictLog;

                bool isStarted = _predictProcess.Start();
                if (!isStarted)
                {
                    throw new Exception("Process.Start() 失败，程序未能成功启动");
                }

                _predictProcess.BeginOutputReadLine();
                _predictProcess.BeginErrorReadLine();
                _predictProcess.WaitForExit();

                Dispatcher.UIThread.Post(() =>
                {
                    if (TrainProgressBar.Maximum > 0)
                    {
                        UpdateProgress((int)TrainProgressBar.Maximum, (int)TrainProgressBar.Maximum);
                    }
                    DeductionedCountText.Text = "推理进度全部完成！";
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n========== C# ==================\n{ex.ToString()}\n=========================================\n");

                Dispatcher.UIThread.Post(() =>
                {
                    DeductionedCountText.Text = $"推理失败: {ex.Message}";
                });
            }
        });
    }

    private void ParsePredictLog(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Data)) return;

        string line = args.Data.Trim();

        Dispatcher.UIThread.Post(() =>
        {
            var match = Regex.Match(line, @"(\d+)/(\d+)");

            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int current) &&
                    int.TryParse(match.Groups[2].Value, out int total))
                {
                    if (total > 0 && current <= total)
                    {
                        UpdateProgress(current, total);
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Python底层输出]: {line}");

                if (line.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Traceback", StringComparison.OrdinalIgnoreCase))
                {
                    DeductionedCountText.Text = $"Python报错: {line}";
                }
            }
        });
    }

    public void UpdateProgress(int current, int total)
    {
        Dispatcher.UIThread.Post(() =>
        {
            TrainProgressBar.Maximum = total;
            TrainProgressBar.Value = current;
            DeductionedCountText.Text = $"已推理图片数量: {current} / {total}";

            if (current == total)
            {
                DeductionedCountText.Text = $"推理完成，共 {total} 张";
            }
        });
    }
}
