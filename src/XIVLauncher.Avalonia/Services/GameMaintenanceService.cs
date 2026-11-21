using XIVLauncher.GamePatchV3;
using XIVLauncher.GamePatchV3.Repair;
using XIVLauncher.GamePatchV3.Update;
using XIVLauncher.GamePatchV3.Update.Models;

namespace XIVLauncher.Services;

/// <summary>
///     游戏本体维护：更新检查、V3 补丁安装、文件修复。
/// </summary>
public sealed class GameMaintenanceService
{
    private readonly object checkLock = new();
    private string? checkingPath;
    private Task<GameUpdateCheckResult>? currentCheck;

    /// <summary>查找可用的 xdelta3（优先使用随应用捆绑的二进制）。</summary>
    public static string? FindXdelta3()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Resources", "xdelta3"),
            "/opt/homebrew/bin/xdelta3",
            "/usr/local/bin/xdelta3"
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    public Task<GameUpdateCheckResult> CheckAsync(DirectoryInfo gamePath)
    {
        lock (checkLock)
        {
            if (currentCheck is not { IsCompleted: false } || checkingPath != gamePath.FullName)
            {
                checkingPath = gamePath.FullName;
                currentCheck = Task.Run(() => GameUpdater.Check(gamePath, false));
            }

            return currentCheck;
        }
    }

    public async Task InstallUpdateAsync
    (
        GameUpdatePlan                plan,
        DirectoryInfo                 gamePath,
        DirectoryInfo                 patchPath,
        bool                          keepPatches,
        IProgress<GamePatchProgress>  progress,
        CancellationToken             cancellationToken = default
    )
    {
        var xdelta3 = FindXdelta3() ?? throw new FileNotFoundException("缺少 xdelta3 组件（Resources/xdelta3）");

        using var vcdiff    = VcdiffClient.CreateLocal(xdelta3);
        using var installer = new GamePatchInstaller();

        await installer.InstallAsync
        (
            plan,
            gamePath,
            patchPath,
            vcdiff,
            keepPatches,
            TimeSpan.FromMilliseconds(200),
            progress,
            cancellationToken
        ).ConfigureAwait(false);
    }

    public async Task<GameRepairer.RepairState> RepairAsync
    (
        string                    gamePath,
        IProgress<RepairProgress>? progress,
        CancellationToken         cancellationToken = default
    )
    {
        var repairer = new GameRepairer(gamePath, TimeSpan.FromMilliseconds(200));

        using var pollCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var       pollTask = Task.Run
        (
            async () =>
            {
                while (!pollCts.Token.IsCancellationRequested)
                {
                    progress?.Report
                    (
                        new RepairProgress
                        (
                            repairer.Progress,
                            repairer.Total,
                            repairer.CurrentFile,
                            repairer.TaskIndex,
                            repairer.TaskCount
                        )
                    );

                    try
                    {
                        await Task.Delay(200, pollCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            },
            CancellationToken.None
        );

        try
        {
            await repairer.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            pollCts.Cancel();

            try
            {
                await pollTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        return repairer.State;
    }
}

public sealed record RepairProgress
(
    long   Progress,
    long   Total,
    string CurrentFile,
    int    TaskIndex,
    int    TaskCount
);
