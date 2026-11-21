using Serilog;
using XIVLauncher.GamePatchV3.Integrity;
using XIVLauncher.GamePatchV3.Integrity.Models;

namespace XIVLauncher.GamePatchV3.Install;

public sealed class GameInstaller
{
    public long                                Speed                   => speedEstimator.Speed;
    public int                                 TaskIndex               { get; private set; }
    public long                                Progress                { get; private set; }
    public long                                Total                   { get; private set; }
    public int                                 TaskCount               { get; private set; }
    public string                              CurrentFile             { get; private set; } = string.Empty;
    public GameFileDownloader.InstallTaskState CurrentMetaInstallState { get; private set; } = GameFileDownloader.InstallTaskState.NotStarted;
    public InstallState                        State                   { get; private set; } = InstallState.NotStarted;

    private readonly string                  gamePath;
    private readonly TimeSpan                progressUpdateInterval;
    private readonly TransferSpeedEstimator  speedEstimator = new();
    private          CancellationTokenSource cts            = new();

    public GameInstaller
    (
        string   gamePath,
        TimeSpan progressUpdateInterval
    )
    {
        this.gamePath               = gamePath;
        this.progressUpdateInterval = progressUpdateInterval;
    }

    public async Task RunAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cts.Token;

        try
        {
            State = InstallState.DownloadMeta;
            var remoteIntegrity = await GameIntegrityChecker.DownloadIntegrityCheckForVersion(token).ConfigureAwait(false);
            var installTargets  = IntegrityPathEntry.BuildEntries(remoteIntegrity);
            var targetRelativePaths = installTargets
                                      .Select(x => x.LocalRelativePath)
                                      .ToList();

            using var downloader = new GameFileDownloader();
            downloader.ProgressReportInterval = progressUpdateInterval.TotalMilliseconds > 0 ?
                                                    (int)progressUpdateInterval.TotalMilliseconds :
                                                    250;
            var installProgressTaskIndex = 0;

            void UpdateInstallProgress
            (
                int                                 sourceIndex,
                long                                fileProgress,
                long                                fileTotal,
                long                                totalProgress,
                long                                total,
                GameFileDownloader.InstallTaskState state
            )
            {
                if (targetRelativePaths.Count <= 0)
                    return;

                CurrentFile = targetRelativePaths[Math.Min(sourceIndex, targetRelativePaths.Count - 1)];
                if (state == GameFileDownloader.InstallTaskState.Complete)
                    TaskIndex = Interlocked.Increment(ref installProgressTaskIndex);
                Progress = Math.Min(totalProgress, total);
                Total    = total;
                CurrentMetaInstallState = state switch
                {
                    GameFileDownloader.InstallTaskState.Connecting  => GameFileDownloader.InstallTaskState.Connecting,
                    GameFileDownloader.InstallTaskState.Downloading => GameFileDownloader.InstallTaskState.Downloading,
                    GameFileDownloader.InstallTaskState.Complete    => GameFileDownloader.InstallTaskState.Complete,
                    _                                               => GameFileDownloader.InstallTaskState.NotStarted
                };
                speedEstimator.Update(totalProgress);
            }

            downloader.OnInstallProgress += UpdateInstallProgress;

            try
            {
                downloader.Construct(installTargets, remoteIntegrity.BaseUrl, remoteIntegrity.DataVersion);

                TaskCount               = targetRelativePaths.Count;
                State                   = InstallState.Installing;
                CurrentMetaInstallState = GameFileDownloader.InstallTaskState.Connecting;

                for (var fileIndex = 0; fileIndex < targetRelativePaths.Count; fileIndex++)
                    downloader.QueueInstall(fileIndex, installTargets[fileIndex].DownloadPath);

                await downloader.Install(gamePath, Math.Clamp(Environment.ProcessorCount, 8, 16), token).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(remoteIntegrity.GameVersion))
                {
                    var gameDir = Path.Combine(gamePath, "game");
                    Directory.CreateDirectory(gameDir);
                    await File.WriteAllTextAsync(Path.Combine(gameDir, "ffxivgame.ver"), remoteIntegrity.GameVersion, token).ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(gameDir, "ffxivgame.bck"), remoteIntegrity.GameVersion, token).ConfigureAwait(false);
                    Log.Information("[GameInstaller] 已写入游戏版本文件, 版本 {GameVersion}", remoteIntegrity.GameVersion);
                }

                State = InstallState.Done;
            }
            finally
            {
                downloader.OnInstallProgress -= UpdateInstallProgress;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
        {
            State = InstallState.Cancelled;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GameInstaller 发生未预期错误");
            State = InstallState.Error;
        }

        // 失败/取消后清理未完成的临时文件（.tmp），避免残留占用磁盘
        if (State is InstallState.Error or InstallState.Cancelled)
            CleanUpTempFiles(gamePath);
    }

    /// <summary>
    ///     清理下载目录中的临时文件（GameFileDownloader 使用 {目标}.tmp 后缀写入，
    ///     失败/取消后可能残留）。仅删除 *.tmp，不影响已完成文件。
    /// </summary>
    private static void CleanUpTempFiles(string gamePath)
    {
        try
        {
            if (!Directory.Exists(gamePath))
                return;

            var deleted = 0;
            foreach (var tmpFile in Directory.EnumerateFiles(gamePath, "*.tmp", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(tmpFile);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Debug(ex, "[GameInstaller] 删除临时文件失败: {Path}", tmpFile);
                }
            }

            if (deleted > 0)
                Log.Information("[GameInstaller] 已清理 {Count} 个临时文件", deleted);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[GameInstaller] 清理临时文件失败: {GamePath}", gamePath);
        }
    }

    public void Cancel() =>
        cts.Cancel();

    public enum InstallState
    {
        NotStarted,
        DownloadMeta,
        Installing,
        Done,
        Cancelled,
        Error
    }
}
