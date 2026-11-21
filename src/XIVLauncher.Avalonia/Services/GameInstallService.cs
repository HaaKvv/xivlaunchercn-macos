using Serilog;
using XIVLauncher.GamePatchV3;
using XIVLauncher.GamePatchV3.Install;

namespace XIVLauncher.Services;

/// <summary>
///     macOS 平台游戏下载/安装服务。
///     <para>移植自原项目 WPF 版 GameClientFileTaskService 的 FreshInstall 流程，
///     去掉 WPF 窗口依赖，通过 <see cref="IProgress{InstallProgress}"/> 报告进度。</para>
/// </summary>
public sealed class GameInstallService
{
    private int installingFlag;

    public async Task<bool> FreshInstallAsync(string gamePath, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // 原子抢占：Interlocked 保证并发调用只有一个进入下载
        if (Interlocked.CompareExchange(ref installingFlag, 1, 0) != 0)
        {
            progress?.Report(new InstallProgress(0, 0, "已有下载任务进行中", "请等待当前任务完成"));
            return false;
        }

        try
        {
            var path = new DirectoryInfo(gamePath);
            if (!path.Exists)
                path.Create();

            path.CreateSubdirectory("game");
            path.CreateSubdirectory("boot");

            progress?.Report(new InstallProgress(0, 1, "正在初始化", "准备下载游戏文件"));

            var installer = new GameInstaller(path.FullName, TimeSpan.FromMilliseconds(100));

            // GameInstaller 使用轮询模式公开进度属性，启动并行轮询任务
            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var pollingTask = PollProgressAsync(installer, progress, pollCts.Token);

            try
            {
                await installer.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                pollCts.Cancel();
                try
                {
                    await pollingTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            return installer.State switch
            {
                GameInstaller.InstallState.Done => true,
                GameInstaller.InstallState.Error => false,
                GameInstaller.InstallState.Cancelled => false,
                _ => false
            };
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new InstallProgress(0, 1, "已取消", "下载已取消"));
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[GameInstallService] 安装游戏失败");
            progress?.Report(new InstallProgress(0, 1, "安装失败", ex.Message));
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref installingFlag, 0);
        }
    }

    private static async Task PollProgressAsync(GameInstaller installer, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        if (progress == null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            progress.Report(new InstallProgress(installer.Progress, installer.Total, StateText(installer.CurrentMetaInstallState), installer.CurrentFile));
            try
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static string StateText(GameFileDownloader.InstallTaskState state) =>
        state switch
        {
            GameFileDownloader.InstallTaskState.Connecting => "正在连接",
            GameFileDownloader.InstallTaskState.Downloading => "正在下载",
            GameFileDownloader.InstallTaskState.Complete => "完成",
            _ => "准备中"
        };

}

/// <summary>安装进度报告</summary>
public sealed record InstallProgress
(
    long   Progress,
    long   Total,
    string Phase,
    string Detail
)
{
    public double Percent => Total > 0 ? Math.Min(100, (double)Progress / Total * 100) : 0;
}
