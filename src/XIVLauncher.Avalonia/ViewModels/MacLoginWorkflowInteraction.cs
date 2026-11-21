using Avalonia.Media.Imaging;
using Avalonia.Threading;
using XIVLauncher.Account;
using XIVLauncher.Login.Models;
using XIVLauncher.Login.Workflow;
using XIVLauncher.Views;

namespace XIVLauncher.ViewModels;

/// <summary>
///     macOS 平台的登录交互实现。
///     <para>等价于原项目 WPF 版 MainWindow 中的交互回调，将对话框/提示调度到 Avalonia UI 线程。
///     登录工作流运行在线程池线程，所有 UI 更新统一经 <see cref="MainWindowViewModel.SetStatusText"/>
///     调度到 UI 线程，避免线程亲和性异常。</para>
/// </summary>
public sealed class MacLoginWorkflowInteraction(MainWindowViewModel vm, CancellationToken cancellationToken) : ILoginWorkflowInteraction
{
    public void ShowQrCode(byte[] qrBytes)
    {
        // qrBytes 是服务端返回的二维码图片字节（PNG/GIF），直接解码为 Bitmap 显示
        // （与 WPF 版 ToBitmapImage 等效；原版登录页 QRCodeBitmapImage 绑定显示）
        try
        {
            using var stream = new MemoryStream(qrBytes);
            vm.ShowLoginQr(new Bitmap(stream), cancellationToken);
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "[LoginInteraction] 渲染二维码失败");
            vm.SetStatusText("已生成登录二维码（渲染失败），请在游戏客户端扫码");
        }
    }

    public void ShowVerificationCode(string code) =>
        vm.SetStatusText($"验证码: {code}");

    public void ShowLoginMessage(string message) =>
        vm.SetStatusText(message);

    public string? PromptTextInput(string text, string caption, string initialText)
    {
        // 在 UI 线程弹出输入对话框（登录流程在后台线程，需同步等待用户输入）。
        // 用 InvokeAsync 而非 Post：若调用方已在 UI 线程，InvokeAsync 会同步执行而非排队，
        // 避免「排队 + 阻塞等待」导致的死锁。
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                var dialog = new InputDialog(caption, text, initialText);
                var owner = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }
                                ? main
                                : null;

                if (owner != null)
                    dialog.ShowDialog(owner);
                else
                    dialog.Show();

                dialog.Closed += (_, _) => tcs.TrySetResult(dialog.IsConfirmed ? dialog.ResultText : null);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[LoginInteraction] 输入对话框显示失败");
                tcs.TrySetResult(initialText);
            }
        });

        return tcs.Task.GetAwaiter().GetResult();
    }

    public string? PromptCaptchaInput(LoginCaptchaChallenge challenge)
    {
        // 显示验证码图片与输入框（等效原版 CaptchaInputWindow）。
        // 同 PromptTextInput：InvokeAsync 保证 UI 线程调用时同步执行，避免死锁
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                Bitmap? image = null;
                if (challenge.ImageBytes is { Length: > 0 })
                {
                    try
                    {
                        using var stream = new MemoryStream(challenge.ImageBytes);
                        image = new Bitmap(stream);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Debug(ex, "[LoginInteraction] 验证码图片解码失败");
                    }
                }

                var dialog = new InputDialog(
                    challenge.Title,
                    challenge.Prompt,
                    "请输入验证码",
                    image);

                var owner = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }
                                ? main
                                : null;

                if (owner != null)
                    dialog.ShowDialog(owner);
                else
                    dialog.Show();

                dialog.Closed += (_, _) =>
                {
                    var result = dialog.IsConfirmed && !string.IsNullOrWhiteSpace(dialog.ResultText)
                                     ? dialog.ResultText
                                     : null;
                    tcs.TrySetResult(result);
                };
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[LoginInteraction] 验证码对话框显示失败");
                tcs.TrySetResult(null);
            }
        });

        return tcs.Task.GetAwaiter().GetResult();
    }

    public NewAccountDeviceProfileChoice PromptNewAccountDeviceProfileChoice() =>
        NewAccountDeviceProfileChoice.UseShared;

    public bool ConfigureTemporaryAccountDeviceProfile(XIVAccount account, AccountManager accountManager) =>
        true;

    public void ShowError(string message) =>
        vm.SetStatusText("错误: " + message);

    public string? GetSavedWeGamePath() =>
        null;

    public void SaveWeGamePath(string path)
    {
    }

    public string? PromptWeGameInstallDirectory(string? currentPath) =>
        null;

    public Task<bool> TryElevatedCopyVersionDllAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
