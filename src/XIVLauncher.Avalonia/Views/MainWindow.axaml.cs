using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using XIVLauncher.ViewModels;

namespace XIVLauncher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var viewModel = new MainWindowViewModel();
        DataContext = viewModel;
        Opened += async (sender, args) =>
        {
            _ = viewModel.RefreshNewsCommand.ExecuteAsync(null);
            await viewModel.InitializeAsync();
        };
    }
}
