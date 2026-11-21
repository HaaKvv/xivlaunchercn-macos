using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XIVLauncher.Common.Constant;
using XIVLauncher.Common.Mac.Compatibility;
using XIVLauncher.Settings;
using XIVLauncher.ViewModels;

namespace XIVLauncher.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel viewModel;

    public SettingsWindow() : this(LauncherSettingsV3.Load(Paths.GetConfigPath()), string.Empty, null)
    {
    }

    public SettingsWindow(LauncherSettingsV3 settings, string gamePath, WineSettings? wine)
    {
        viewModel = new SettingsWindowViewModel(settings, gamePath, wine);
        DataContext = viewModel;

        InitializeComponent();
        viewModel.InitializeSelection();
    }

    private async void OnBrowseGamePath(object? sender, RoutedEventArgs e) =>
        await BrowseFolderAsync("选择游戏目录（包含 game/ffxiv_dx11.exe）", viewModel.GamePath, path => viewModel.GamePath = path);

    private async void OnBrowsePatchPath(object? sender, RoutedEventArgs e) =>
        await BrowseFolderAsync("选择补丁缓存目录", viewModel.PatchPath, path => viewModel.PatchPath = path);

    private async Task BrowseFolderAsync(string title, string currentPath, Action<string> apply)
    {
        var suggested = Directory.Exists(currentPath) ? await StorageProvider.TryGetFolderFromPathAsync(currentPath) : null;
        var folders = await StorageProvider.OpenFolderPickerAsync
        (
            new FolderPickerOpenOptions
            {
                Title         = title,
                AllowMultiple = false,
                SuggestedStartLocation = suggested
            }
        );

        if (folders.Count > 0)
            apply(folders[0].Path.LocalPath);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            viewModel.Save();
            Close(true);
        }
        catch (ArgumentException ex)
        {
            SaveError.Text = ex.Message;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) =>
        Close(false);
}
