using System.Diagnostics;
using Serilog;
using XIVLauncher.Common.Mac.Compatibility;

namespace XIVLauncher.Common.Mac.Compatibility;

/// <summary>
///     macOS 平台 Wine 兼容层。
///     <para>负责 Wine Prefix 的创建与维护、在 Prefix 内执行命令（winepath / reg / winedbg 等），
///     以及将 macOS 原生路径映射为 Wine 内部路径。</para>
///     <para>移植自官方 GoatCorp 项目的 CompatibilityTools（Linux 版），并针对 macOS
///     （CrossOver / Whisky / Homebrew Wine）做了适配。</para>
/// </summary>
public sealed class CompatibilityTools
{
    private readonly StreamWriter logWriter;

    public WineSettings Settings { get; }

    public bool IsToolReady { get; private set; }

    private string Wine64Path => Settings.Wine64Path;
    private string WineServerPath => Settings.WineServerPath;

    public CompatibilityTools(WineSettings settings)
    {
        Settings = settings;

        if (!settings.Prefix.Exists)
            settings.Prefix.Create();

        Directory.CreateDirectory(Path.GetDirectoryName(settings.LogFile.FullName) ?? settings.Prefix.FullName);
        logWriter = new StreamWriter(settings.LogFile.FullName, append: true) { AutoFlush = true };
    }

    public void EnsurePrefix()
    {
        // 初始化 wine prefix（wineboot 首次运行会创建 registry 等）
        // 若 prefix 已存在（registry 已生成），wineboot 可安全跳过
        if (IsPrefixInitialized())
        {
            IsToolReady = true;
            return;
        }

        try
        {
            using var proc = RunInPrefix("wineboot -u", writeLog: true);
            IsToolReady = proc.WaitForExit(60000) && proc.ExitCode == 0 && IsPrefixInitialized();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[CompatibilityTools] wineboot 初始化 prefix 失败");
            IsToolReady = false; // 明确标记初始化失败，由上层决定是否继续
        }
    }

    /// <summary>
    ///     判断 prefix 是否已初始化（存在 system.reg 即认为已 wineboot 过）。
    /// </summary>
    private bool IsPrefixInitialized() =>
        File.Exists(Path.Combine(Settings.Prefix.FullName, "system.reg"));

    /// <summary>
    ///     在 Wine Prefix 中执行命令（自动注入 WINEPREFIX 等环境变量）。
    /// </summary>
    public Process RunInPrefix(string command, string workingDirectory = "", IDictionary<string, string>? environment = null, bool redirectOutput = false, bool writeLog = false)
    {
        var psi = new ProcessStartInfo(Wine64Path)
        {
            Arguments = command,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = writeLog,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true
        };

        return RunInPrefix(psi, environment, redirectOutput, writeLog);
    }

    public Process RunInPrefix(string[] args, string workingDirectory = "", IDictionary<string, string>? environment = null, bool redirectOutput = false, bool writeLog = false)
    {
        var psi = new ProcessStartInfo(Wine64Path)
        {
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = writeLog,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        Log.Verbose("[CompatibilityTools] 在 Prefix 中执行: {File} {Args}", Wine64Path, string.Join(' ', args));
        return RunInPrefix(psi, environment, redirectOutput, writeLog);
    }

    /// <summary>
    ///     使用给定 ProcessStartInfo 在 Wine Prefix 中运行命令
    ///     （自动注入 WINEPREFIX 等环境变量，供需要精确控制启动参数的调用方使用）。
    /// </summary>
    public Process RunInPrefix(ProcessStartInfo psi, IDictionary<string, string>? environment = null, bool redirectOutput = false, bool writeLog = false)
    {
        psi.RedirectStandardOutput = redirectOutput;
        psi.RedirectStandardError  = writeLog;
        psi.UseShellExecute        = false;
        psi.CreateNoWindow         = true;

        return RunInPrefixInternal(psi, environment, writeLog);
    }

    private Process RunInPrefixInternal(ProcessStartInfo psi, IDictionary<string, string>? environment, bool writeLog = false)
    {
        // 与 ottercorp/XIVLauncher.Core 的 macOS 兼容层保持一致（DXMT / MetalFX / MoltenVK）。
        var wineEnv = new Dictionary<string, string>
        {
            ["LANG"] = "en_US",
            ["MVK_ALLOW_METAL_FENCES"] = "1",
            ["MVK_CONFIG_FULL_IMAGE_VIEW_SWIZZLE"] = "1",
            ["MVK_CONFIG_RESUME_LOST_DEVICE"] = "1",
            ["DOTNET_EnableWriteXorExecute"] = "0",
            ["DXMT_CONFIG"] = $"d3d11.metalSpatialUpscaleFactor={Settings.MetalFxFactor};d3d11.preferredMaxFrameRate={Settings.FrameLimit};",
            ["DXMT_METALFX_SPATIAL_SWAPCHAIN"] = Settings.MetalFxEnabled ? "1" : "0",
            ["WINEPREFIX"] = Settings.Prefix.FullName,
            ["WINEDLLOVERRIDES"] = $"msquic=,mscoree=n,b;d3d11={(Settings.UseDxmt ? "n" : "b")};dxgi=n,b",
            ["XL_WINEONLINUX"] = "true",
            ["XL_WINEONMAC"] = "true",
            ["XL_WINEONMACOS"] = "true"
        };

        if (!string.IsNullOrEmpty(Settings.DebugVars))
            wineEnv["WINEDEBUG"] = Settings.DebugVars;

        wineEnv["WINEESYNC"] = Settings.EsyncOn ? "1" : "0";
        wineEnv["WINEFSYNC"] = Settings.FsyncOn ? "1" : "0";

        // 仅 xom/xlcore 使用 MSync；其他 Wine 构建保持其自身默认，不显式关闭
        if (Settings.MsyncOn)
            wineEnv["WINEMSYNC"] = "1";

        if (!string.IsNullOrEmpty(Settings.ExtraEnv))
        {
            foreach (var pair in Settings.ExtraEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2)
                    wineEnv[kv[0]] = kv[1];
            }
        }

        foreach (var kv in wineEnv)
            psi.Environment[kv.Key] = kv.Value;

        if (environment != null)
        {
            foreach (var kv in environment)
                psi.Environment[kv.Key] = kv.Value;
        }

        var process = new Process { StartInfo = psi };
        process.ErrorDataReceived += (_, errLine) =>
        {
            if (string.IsNullOrEmpty(errLine.Data))
                return;

            try
            {
                logWriter.WriteLine(errLine.Data);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException or IndexOutOfRangeException)
            {
                // wine 的极长日志行可能被截断，忽略写入错误
            }
        };

        process.Start();
        if (writeLog)
            process.BeginErrorReadLine();

        return process;
    }

    /// <summary>
    ///     将 macOS 路径转换为 Wine Prefix 内的 Windows 路径（C:\...）。
    /// </summary>
    public string? UnixToWinePath(string unixPath)
    {
        using var winePath = RunInPrefix(new[] { "winepath", "--windows", unixPath }, redirectOutput: true);
        var output = ReadUtilityOutput(winePath);
        return output?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
    }

    /// <summary>
    ///     将 Wine 内部 PID 映射为 macOS 进程 PID（依赖 wine 的 info procmap 输出）。
    ///     与 ottercorp/XIVLauncher.Core 的 GetUnixProcessId 语义一致。
    /// </summary>
    public int GetUnixProcessId(int winePid)
    {
        try
        {
            using var wineDbg = RunInPrefix(new[] { "winedbg", "--command", "info procmap" }, redirectOutput: true);
            var output = ReadUtilityOutput(wineDbg);
            if (output is null)
                return 0;

            return ParseUnixProcessId(output, winePid);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[CompatibilityTools] 映射 Wine PID {Pid} 失败", winePid);
            return 0;
        }
    }

    internal static int ParseUnixProcessId(string output, int winePid)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = line.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length >= 3 && TryParsePid(columns[0], out var pid) && pid == winePid
                                    && TryParsePid(columns[1], out var unixPid) && unixPid > 0)
                return unixPid;
        }

        return 0;
    }

    private static bool TryParsePid(string value, out int pid) =>
        int.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out pid);

    private static string? ReadUtilityOutput(Process process)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token).GetAwaiter().GetResult();
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output : null;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill();

            return null;
        }
    }

    public void AddRegistryKey(string key, string value, string data)
    {
        var args = new[] { "reg", "add", key, "/v", value, "/d", data, "/f" };
        var wineProcess = RunInPrefix(args);
        wineProcess.WaitForExit();
    }

    public void Kill()
    {
        try
        {
            var psi = new ProcessStartInfo(WineServerPath)
            {
                Arguments = "-k",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.Environment["WINEPREFIX"] = Settings.Prefix.FullName;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[CompatibilityTools] 终止 wineserver 失败");
        }
    }

    public void Dispose() =>
        logWriter.Dispose();
}
