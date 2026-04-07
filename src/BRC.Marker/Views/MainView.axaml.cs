using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Semi.Avalonia;

namespace BRC.Marker.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        this.DataContext = new MainViewModel();
    }
}

public partial class MainViewModel : ObservableObject
{
    public IReadOnlyList<MenuItemViewModel> MenuItems { get; }

    public MainViewModel()
    {
        MenuItems =
        [
            new MenuItemViewModel
            {
                Header = "主题",
                Items =
                [
                    new MenuItemViewModel
                    {
                        Header = "跟随系统",
                        Command = FollowSystemThemeCommand
                    },
                    new MenuItemViewModel
                    {
                        Header = "Aquatic",
                        Command = SelectThemeCommand,
                        CommandParameter = SemiTheme.Aquatic
                    },
                    new MenuItemViewModel
                    {
                        Header = "Desert",
                        Command = SelectThemeCommand,
                        CommandParameter = SemiTheme.Desert
                    },
                    new MenuItemViewModel
                    {
                        Header = "Dusk",
                        Command = SelectThemeCommand,
                        CommandParameter = SemiTheme.Dusk
                    },
                    new MenuItemViewModel
                    {
                        Header = "NightSky",
                        Command = SelectThemeCommand,
                        CommandParameter = SemiTheme.NightSky
                    },
                ]
            }
        ];
    }

    [RelayCommand]
    private void FollowSystemTheme()
    {
        Application.Current?.RegisterFollowSystemTheme();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var app = Application.Current;
        if (app is null) return;
        var theme = app.ActualThemeVariant;
        app.RequestedThemeVariant = theme == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        app.UnregisterFollowSystemTheme();
    }

    [RelayCommand]
    private void SelectTheme(object? obj)
    {
        var app = Application.Current;
        if (app is null) return;
        app.RequestedThemeVariant = obj as ThemeVariant;
        app.UnregisterFollowSystemTheme();
    }
}

public class MenuItemViewModel
{
    public string? Header { get; set; }
    public ICommand? Command { get; set; }
    public object? CommandParameter { get; set; }
    public IList<MenuItemViewModel>? Items { get; set; }
}
