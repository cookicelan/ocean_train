using System;
using System.Text;
using Avalonia;
using Avalonia.Dialogs;
using Avalonia.Media;

namespace BRC.Marker.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        BuildAvaloniaApp()
        .With(new FontManagerOptions
        {
            FontFallbacks =
            [
                new FontFallback
                {
                    FontFamily = new FontFamily("Microsoft YaHei")
                }
            ]
        })
        .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseManagedSystemDialogs()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions())
            .LogToTrace();
}