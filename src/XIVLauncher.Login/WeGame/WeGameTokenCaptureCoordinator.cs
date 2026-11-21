using XIVLauncher.Login.Workflow;

namespace XIVLauncher.Login.WeGame;

/// <summary>
///     macOS 平台 WeGame 令牌抓取协调器。
///     <para>WeGame 登录链路依赖 Windows 专属机制（version.dll DLL 劫持、NamedPipe、
///     wegame:// URI 协议、进程内存读取），在 macOS 上不可用，因此始终返回
///     <c>null</c>（表示不支持），由上层回退到其他登录方式。</para>
/// </summary>
public sealed class WeGameTokenCaptureCoordinator : IWeGameTokenCaptureCoordinator
{
    public Task<WeGameCaptureResult?> CaptureAsync
    (
        ILoginWorkflowInteraction interaction,
        CancellationTokenSource   loginCancellationTokenSource
    )
    {
        interaction.ShowError("macOS 平台暂不支持 WeGame 登录渠道，请使用扫码、一键或静态密码登录。");
        return Task.FromResult<WeGameCaptureResult?>(null);
    }
}
