using System.Diagnostics;
using Serilog;

namespace XIVLauncher.Common.Game;

public class RestartMonitor
{
    private const string DALAMUD_CRASH_HANDLER_PROCESS_NAME = "DalamudCrashHandler.exe";

    // 托管重启退出码协议, 须与 DalamudCrashHandler.cpp 保持一致
    private const uint MANAGED_EXIT_RESTART_DEFAULT    = 0x12345670; // 沿用启动器原始模式
    private const uint MANAGED_EXIT_RESTART_NORMAL     = 0x12345671; // 正常重启
    private const uint MANAGED_EXIT_RESTART_NO_3P      = 0x12345672; // 禁用第三方插件
    private const uint MANAGED_EXIT_RESTART_NO_PLUGINS = 0x12345673; // 禁用全部插件
    private const uint MANAGED_EXIT_RESTART_NO_DALAMUD = 0x12345674; // 禁用 Dalamud

    private static readonly TimeSpan CrashHandlerDiscoveryTimeout      = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CrashHandlerDiscoveryPollInterval = TimeSpan.FromMilliseconds(200);

    public readonly record struct RestartOptions
    (
        bool ForceNoDalamud,
        bool NoThirdPlugins,
        bool NoPlugins
    )
    {
        public static RestartOptions Normal => default;
    }

    public async Task MonitorAsync
    (
        FFXIVProcess                              gameProcess,
        RestartOptions                            defaultRestartOptions,
        Func<RestartOptions, Task<FFXIVProcess?>> restartProcessAsync,
        CancellationToken                         cancellationToken = default
    )
    {
        using var crashHandler = await TryAcquireCrashHandlerAsync(gameProcess, cancellationToken).ConfigureAwait(false);

        await gameProcess.UnderlyingProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (crashHandler == null)
        {
            Log.Information("游戏进程退出, 未检测到 Dalamud 崩溃处理器, 不接管重启");
            return;
        }

        await crashHandler.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var exitCode = (uint)crashHandler.ExitCode;

        if (MapRestartDecision(exitCode, defaultRestartOptions) is not { } restartOptions)
        {
            Log.Information("Dalamud 崩溃处理器未请求重启, 退出码: 0x{ExitCode:X}", exitCode);
            return;
        }

        Log.Information
        (
            "Dalamud 崩溃处理器请求重启, 退出码: 0x{ExitCode:X}, 模式: 禁用 Dalamud = {ForceNoDalamud}, 禁用第三方插件 = {NoThirdPlugins}, 禁用全部插件 = {NoPlugins}",
            exitCode,
            restartOptions.ForceNoDalamud,
            restartOptions.NoThirdPlugins,
            restartOptions.NoPlugins
        );

        using var restartedProcess = await restartProcessAsync(restartOptions).ConfigureAwait(false);

        if (restartedProcess == null)
            Log.Error("重启游戏失败");
    }

    private static RestartOptions? MapRestartDecision(uint exitCode, RestartOptions defaultRestartOptions) =>
        exitCode switch
        {
            MANAGED_EXIT_RESTART_DEFAULT    => defaultRestartOptions,
            MANAGED_EXIT_RESTART_NORMAL     => RestartOptions.Normal,
            MANAGED_EXIT_RESTART_NO_3P      => new RestartOptions(false, true,  false),
            MANAGED_EXIT_RESTART_NO_PLUGINS => new RestartOptions(false, false, true),
            MANAGED_EXIT_RESTART_NO_DALAMUD => new RestartOptions(true,  false, false),
            _                               => null
        };

    private static async Task<Process?> TryAcquireCrashHandlerAsync(FFXIVProcess gameProcess, CancellationToken cancellationToken)
    {
        var gamePid  = gameProcess.ProcessID;
        var deadline = DateTime.Now + CrashHandlerDiscoveryTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (gameProcess.UnderlyingProcess.HasExited)
                return null;

            if (TryFindCrashHandlerProcess(gamePid) is { } crashHandler)
            {
                Log.Information("已捕获 Dalamud 崩溃处理器进程 {ProcessId}", crashHandler.Id);
                return crashHandler;
            }

            if (DateTime.Now >= deadline)
            {
                Log.Information("等待 Dalamud 崩溃处理器超时, 不接管重启");
                return null;
            }

            await Task.Delay(CrashHandlerDiscoveryPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Process? TryFindCrashHandlerProcess(int gamePid)
    {
#if XL_MAC
        return TryFindCrashHandlerProcessMac(gamePid);
#else
        try
        {
            using var searcher = new ManagementObjectSearcher
            (
                "SELECT ProcessId FROM Win32_Process " + $"WHERE Name = '{DALAMUD_CRASH_HANDLER_PROCESS_NAME}' AND ParentProcessId = {gamePid}"
            );

            foreach (var o in searcher.Get())
            {
                using var managementObject = (ManagementObject)o;

                if (managementObject["ProcessId"] is not uint processId)
                    continue;

                try
                {
                    var crashHandler = Process.GetProcessById((int)processId);

                    _ = crashHandler.SafeHandle;
                    return crashHandler;
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "查询 Dalamud 崩溃处理器进程失败");
        }

        return null;
#endif
    }

#if XL_MAC
    /// <summary>
    ///     macOS 下通过 <c>pgrep -P</c> 查找指定父进程的子进程中名为 DalamudCrashHandler.exe 的进程。
    ///     <para>等效于 Windows 的 WMI Win32_Process 查询。</para>
    /// </summary>
    private static Process? TryFindCrashHandlerProcessMac(int gamePid)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/pgrep")
            {
                Arguments       = $"-P {gamePid} -f {DALAMUD_CRASH_HANDLER_PROCESS_NAME}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow  = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return null;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(line.Trim(), out var pid))
                    continue;

                try
                {
                    var crashHandler = Process.GetProcessById(pid);
                    _ = crashHandler.SafeHandle;
                    return crashHandler;
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "查询 Dalamud 崩溃处理器进程失败");
        }

        return null;
    }
#endif
}
