using System.Diagnostics;
using Serilog;
using XIVLauncher.Common.Game;
using XIVLauncher.Common.Mac.Compatibility;

namespace XIVLauncher.Common.Mac;

/// <summary>
///     macOS 平台游戏启动器。
///     <para>国服 FF14 没有 macOS 原生客户端，游戏可执行文件（ffxiv_dx11.exe）必须在
///     Wine / CrossOver / Whisky 兼容层中运行。</para>
///     <para>移植自官方 GoatCorp 项目的 UnixGameRunner，并针对 macOS 的 Wine 环境做了适配。</para>
/// </summary>
public sealed class MacGameRunner : IGameRunner
{
    private readonly CompatibilityTools compatibility;
    private readonly bool useDalamud;

    public MacGameRunner(CompatibilityTools compatibility, bool useDalamud)
    {
        this.compatibility = compatibility;
        this.useDalamud = useDalamud;
    }

    /// <inheritdoc />
    public Process? Start(string path, string workingDirectory, string arguments, IDictionary<string, string> environment, DPIAwareness dpiAwareness)
    {
        Log.Information("[MacGameRunner] 启动游戏: {Path} (Dalamud={UseDalamud})", path, useDalamud);

        // Dalamud 注入模式：游戏由注入器（MacDalamudRunner → Dalamud.Injector.exe）在
        // Wine 内拉起并注入，本 runner 不应直接启动游戏，返回 null 表示"由上层注入链路接管"。
        // （与官方 GoatCorp Unix 实现语义一致。）
        if (useDalamud)
        {
            Log.Information("[MacGameRunner] Dalamud 注入模式，游戏由注入器启动，runner 不直接拉起");
            return null;
        }

        // 无 Dalamud 模式：直接通过 Wine 运行游戏
        var psi = BuildWineProcessStartInfo(path, workingDirectory, arguments, environment);
        return compatibility.RunInPrefix(psi, environment, redirectOutput: false, writeLog: true);
    }

    private ProcessStartInfo BuildWineProcessStartInfo(string gamePath, string workingDirectory, string arguments, IDictionary<string, string> environment)
    {
        // Wine 内的 exe 路径：macOS 路径 → 需要映射为 Wine 内部路径，或直接使用 Z: 盘映射
        var wineExePath = PathStartsWithDrive(gamePath) ? gamePath : MapUnixPathToWine(gamePath);

        var psi = new ProcessStartInfo(compatibility.Settings.Wine64Path)
        {
            Arguments = $"\"{wineExePath}\" {arguments}",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        foreach (var kv in environment)
            psi.Environment[kv.Key] = kv.Value;

        return psi;
    }

    private static bool PathStartsWithDrive(string path) =>
        path.Length >= 2 && path[1] == ':';

    private string MapUnixPathToWine(string unixPath)
    {
        var mapped = compatibility.UnixToWinePath(unixPath);
        if (string.IsNullOrEmpty(mapped))
        {
            Log.Warning("[MacGameRunner] winepath 映射失败，回退到 Z: 盘路径: {Path}", unixPath);
            return "Z:" + unixPath.Replace('/', '\\');
        }

        return mapped;
    }
}
