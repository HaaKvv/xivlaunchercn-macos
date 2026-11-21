using Avalonia.Controls;
using Avalonia.Interactivity;

namespace XIVLauncher.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() => InitializeComponent();

    public static Task<bool> AskAsync(Window owner, string title, string message, string accept)
    {
        var dialog = new ConfirmDialog { Title = title };
        dialog.Message.Text = message;
        dialog.Accept.Content = accept;
        return dialog.ShowDialog<bool>(owner);
    }

    private void OnCancel(object? sender, RoutedEventArgs args) => Close(false);
    private void OnAccept(object? sender, RoutedEventArgs args) => Close(true);
}
