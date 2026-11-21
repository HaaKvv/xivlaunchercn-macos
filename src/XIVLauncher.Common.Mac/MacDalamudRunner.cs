using System.Diagnostics;
using Newtonsoft.Json;
using Serilog;
using XIVLauncher.Common.Mac.Compatibility;
using XIVLauncher.Dalamud;

namespace XIVLauncher.Common.Mac;

/// <summary>
///     macOS 平台 Dalamud 启动器。
///     <para>Dalamud 注入器本体是 Windows 可执行文件（Dalamud.Injector.exe），
///     在 macOS 上必须通过 Wine 兼容层运行，由注入器在 Wine 进程内拉起游戏并完成注入。</para>
///     <para>移植自官方 GoatCorp 项目的 UnixDalamudRunner，参数协议与 AutoOmen 的
///     <c>DalamudInjectorArgs</c> 保持一致。</para>
/// </summary>
public sealed class MacDalamudRunner : IDalamudRunner
{
    private readonly CompatibilityTools compatibility;
    private readonly DirectoryInfo dotnetRuntime;

    public MacDalamudRunner(CompatibilityTools compatibility, DirectoryInfo dotnetRuntime)
    {
        this.compatibility = compatibility;
        this.dotnetRuntime = dotnetRuntime;
    }

    /// <inheritdoc />
    public Process? Run
    (
        FileInfo                    runner,
        bool                        fakeLogin,
        bool                        noPlugins,
        bool                        noThirdPlugins,
        FileInfo                    gameExe,
        string                      gameArgs,
        IDictionary<string, string> environment,
        DalamudLoadMethod           loadMethod,
        DalamudStartInfo            startInfo
    )
    {
        var gameExePath      = "";
        var dotnetRuntimePath = "";

        Parallel.Invoke
        (
            () => gameExePath       = compatibility.UnixToWinePath(gameExe.FullName) ?? gameExe.FullName,
            () => dotnetRuntimePath = compatibility.UnixToWinePath(dotnetRuntime.FullName) ?? dotnetRuntime.FullName,
            () => startInfo.LoggingPath       = compatibility.UnixToWinePath(startInfo.LoggingPath) ?? startInfo.LoggingPath,
            () => startInfo.WorkingDirectory  = compatibility.UnixToWinePath(startInfo.WorkingDirectory) ?? startInfo.WorkingDirectory,
            () => startInfo.ConfigurationPath = compatibility.UnixToWinePath(startInfo.ConfigurationPath) ?? startInfo.ConfigurationPath,
            () => startInfo.PluginDirectory   = compatibility.UnixToWinePath(startInfo.PluginDirectory) ?? startInfo.PluginDirectory,
            () => startInfo.AssetDirectory    = compatibility.UnixToWinePath(startInfo.AssetDirectory) ?? startInfo.AssetDirectory
        );

        environment["DALAMUD_RUNTIME"] = dotnetRuntimePath;
        environment["DOTNET_ROOT"]     = dotnetRuntimePath;

        var launchArguments = new List<string>
        {
            $"\"{runner.FullName}\"",
            DalamudInjectorArgs.LAUNCH,
            DalamudInjectorArgs.Mode(loadMethod == DalamudLoadMethod.EntryPoint ? "entrypoint" : "inject"),
            DalamudInjectorArgs.Game(gameExePath),
            DalamudInjectorArgs.WorkingDirectory(startInfo.WorkingDirectory),
            DalamudInjectorArgs.ConfigurationPath(startInfo.ConfigurationPath),
            DalamudInjectorArgs.LoggingPath(startInfo.LoggingPath),
            DalamudInjectorArgs.PluginDirectory(startInfo.PluginDirectory),
            DalamudInjectorArgs.AssetDirectory(startInfo.AssetDirectory),
            DalamudInjectorArgs.ClientLanguage(startInfo.ClientLanguage),
            DalamudInjectorArgs.DelayInitialize(startInfo.DelayInitializeMs),
            DalamudInjectorArgs.TsPackB64(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(startInfo.TroubleshootingPackData))),
            DalamudInjectorArgs.LauncherDirectory(startInfo.LauncherDirectory)
        };

        if (loadMethod == DalamudLoadMethod.ACLonly)
            launchArguments.Add(DalamudInjectorArgs.WITHOUT_DALAMUD);

        if (fakeLogin)
            launchArguments.Add(DalamudInjectorArgs.FAKE_ARGUMENTS);

        if (noPlugins)
            launchArguments.Add(DalamudInjectorArgs.NO_PLUGIN);

        if (noThirdPlugins)
            launchArguments.Add(DalamudInjectorArgs.NO_THIRD_PARTY);

        launchArguments.Add("--");
        launchArguments.Add(gameArgs);

        var injector = compatibility.RunInPrefix
        (
            string.Join(" ", launchArguments),
            workingDirectory: gameExe.DirectoryName!,
            environment: environment,
            redirectOutput: true,
            writeLog: true
        );

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var winePid = ReadGamePidAsync(injector.StandardOutput, timeout.Token).GetAwaiter().GetResult();
            if (winePid is null)
            {
                var exitCode = injector.WaitForExit(1000) ? $"（退出码 {injector.ExitCode}）" : string.Empty;
                throw new DalamudRunnerException($"卫月注入器未返回游戏进程{exitCode}。详情请查看 dalamud.injector.log。");
            }

            var unixPid = compatibility.GetUnixProcessId(winePid.Value);
            if (unixPid == 0)
                throw new DalamudRunnerException($"Wine 无法定位游戏进程（Wine PID {winePid}）。请检查游戏窗口及 Wine 日志。");

            Process game;
            try
            {
                game = Process.GetProcessById(unixPid);
            }
            catch (ArgumentException ex)
            {
                throw new DalamudRunnerException("游戏进程在启动时退出。详情请查看 dalamud.log。", ex);
            }

            Log.Information("[MacDalamudRunner] 注入器已启动游戏: Wine PID={WinePid}, Unix PID={UnixPid}", winePid, unixPid);
            _ = DrainOutputAsync(injector);
            return game;
        }
        catch (OperationCanceledException ex)
        {
            _ = DrainOutputAsync(injector);
            throw new DalamudRunnerException("等待卫月启动游戏超时。请检查游戏窗口及 dalamud.injector.log。", ex);
        }
        catch
        {
            injector.Dispose();
            throw;
        }
    }

    private static async Task<int?> ReadGamePidAsync(StreamReader output, CancellationToken cancellationToken)
    {
        while (await output.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var json = line.Trim();
            if (!json.StartsWith('{'))
                continue;

            try
            {
                if (JsonConvert.DeserializeObject<DalamudConsoleOutput>(json) is { Pid: > 0 } result)
                    return result.Pid;
            }
            catch (JsonException)
            {
                // 注入器也可能在标准输出中写普通日志。
            }
        }

        return null;
    }

    private static async Task DrainOutputAsync(Process injector)
    {
        using (injector)
        {
            try
            {
                await injector.StandardOutput.BaseStream.CopyToAsync(Stream.Null).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                Log.Debug(ex, "[MacDalamudRunner] 读取注入器输出结束");
            }
        }
    }
}
