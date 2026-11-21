using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;

namespace XIVLauncher.Views;

/// <summary>
///     通用文本/验证码输入对话框（macOS 移植新增）。
///     <para>等效原项目 WPF 版的 CaptchaInputWindow / ShowTextInput 对话框，
///     用于静态密码登录遇到验证码、或登录流程需要用户输入时。</para>
/// </summary>
public partial class InputDialog : Window
{
    public string DialogTitle { get; }
    public string Prompt { get; }
    public string Watermark { get; }
    public Bitmap? Image { get; }
    public bool HasImage => Image != null;

    public string ResultText => InputBox.Text ?? string.Empty;

    /// <summary>用户是否点击了「确定」（公开属性，等效 Window.Result）</summary>
    public bool IsConfirmed { get; private set; }

    public InputDialog(string title, string prompt, string watermark = "", Bitmap? image = null)
    {
        DialogTitle = title;
        Prompt = prompt;
        Watermark = watermark;
        Image = image;

        Title = title;
        InitializeComponent();
        DataContext = this;
        InputBox.Text = string.Empty;
    }

    private void InitializeComponent() =>
        AvaloniaXamlLoader.Load(this);

    private void OnOkClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IsConfirmed = true;
        Close();
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }
}
