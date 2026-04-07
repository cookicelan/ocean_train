using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using BRC.Marker.ViewModels;
using BRC.Marker.Views;
using Semi.Avalonia;

namespace BRC.Marker;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        this.DataContext = new ApplicationViewModel();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                // Line below is needed to remove Avalonia data validation.
                // Without this line you will get duplicate validations from both Avalonia and CT
                BindingPlugins.DataValidators.RemoveAt(0);
                desktop.MainWindow = new MainWindow();
                break;
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new MainView();
                break;
        }

        this.RegisterFollowSystemTheme();
        base.OnFrameworkInitializationCompleted();
    }
}