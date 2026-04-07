using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BRC.Marker.Pages;

public partial class data_preprocess : UserControl
{
    private Chooseparameter _choosePage;
    private DataEXshow _dataShowPage;

    public data_preprocess()
    {
        InitializeComponent();

        _choosePage = new Chooseparameter();
        _dataShowPage = new DataEXshow();

        MainContentArea.Content = _choosePage;

        _choosePage.OnParametersConfirmed += (sender, config) =>
        {
            MainContentArea.Content = _dataShowPage;
            _dataShowPage.LoadPreviewData(config);
        };

        _dataShowPage.OnBackRequested += (sender, args) =>
        {
            MainContentArea.Content = _choosePage;
        };
    }

    private void ButtonClick_Chooseparameter(object? sender, RoutedEventArgs e)
    {
        MainContentArea.Content = _choosePage;
    }

    private void ButtonClick_DataEX(object? sender, RoutedEventArgs e)
    {
        MainContentArea.Content = _dataShowPage;
    }
}
