using System.Diagnostics;
using Serilog;

namespace XIVLauncher.Common.Mac.Compatibility;

public enum WineStartupType
{
    /// <summary>
    ///     XIVLauncherCN.Core（xlcore / xom wine）自带的兼容层。
    ///     复用其 Wine 二进制与既有 Prefix，可直接使用已安装的游戏与卫月。
    /// </summary>
    Xlcore,

    /// <summary>
    ///     用户系统安装的 Wine（Homebrew / 手动安装）
    /// </summary>
    System,

    /// <summary>
    ///     CrossOver（CodeWeavers）
    /// </summary>
    CrossOver,

    /// <summary>
    ///     Whisky（基于 Apple Game Porting Toolkit 的 Wine 封装）
    /// </summary>
    Whisky
}

/// <summary>
///     macOS 平台 Wine 环境配置。
///     <para>国服 FF14 没有 macOS 原生客户端，游戏与 Dalamud 注入器均需在 Wine 兼容层中运行。</para>
/// </summary>
public sealed class WineSettings
{
    public WineStartupType StartupType { get; }

    /// <summary>Wine 可执行文件路径（系统 Wine / CrossOver / Whisky / xlcore 中的 wine64）</summary>
    public string WineBinPath { get; }

    /// <summary>Wine Prefix 目录。检测到 xlcore 时复用 ~/.xlcore_cn/wineprefix。</summary>
    public DirectoryInfo Prefix { get; }

    /// <summary>Wine 日志文件</summary>
    public FileInfo LogFile { get; }

    public bool EsyncOn { get; }
    public bool FsyncOn { get; }

    /// <summary>使用 MSync（xom/xlcore 的 Wine 构建支持；其他构建会忽略）</summary>
    public bool MsyncOn { get; }

    public string? DebugVars { get; }

    /// <summary>使用 DXMT（D3D11 → Metal）而非 Wine 自带 D3D 实现</summary>
    public bool UseDxmt { get; }

    /// <summary>MetalFX 空间放大</summary>
    public bool MetalFxEnabled { get; }

    /// <summary>MetalFX 放大倍率（1 / 2 / 4）</summary>
    public decimal MetalFxFactor { get; }

    /// <summary>帧率上限（0 = 不限制）</summary>
    public int FrameLimit { get; }

    /// <summary>额外注入的 Wine 环境变量（分号分隔的 KEY=VALUE）</summary>
    public string? ExtraEnv { get; }

    public string Wine64Path => Path.Combine(WineBinPath, "wine64");
    public string WineServerPath => Path.Combine(WineBinPath, "wineserver");

    private WineSettings
    (
        WineStartupType startupType,
        string          wineBinPath,
        DirectoryInfo   prefix,
        FileInfo        logFile,
        bool            esync,
        bool            fsync,
        string?         debugVars,
        bool            useDxmt        = true,
        bool            metalFxEnabled = true,
        decimal         metalFxFactor  = 2,
        int             frameLimit     = 0,
        string?         extraEnv       = null
    )
    {
        StartupType    = startupType;
        WineBinPath    = wineBinPath;
        Prefix         = prefix;
        LogFile        = logFile;
        EsyncOn        = esync;
        FsyncOn        = fsync;
        MsyncOn        = startupType == WineStartupType.Xlcore;
        DebugVars      = debugVars;
        UseDxmt        = useDxmt;
        MetalFxEnabled = metalFxEnabled;
        MetalFxFactor  = metalFxFactor;
        FrameLimit     = frameLimit;
        ExtraEnv       = extraEnv;
    }

    /// <summary>
    ///     自动探测系统上可用的 Wine 环境，按优先级：
    ///     xlcore/xom → CrossOver → Whisky → Homebrew/系统 Wine。
    ///     默认启用 ESync（xlcore 下同时启用 MSync）并静默 Wine 日志（-all）。
    /// </summary>
    public static WineSettings? Detect(string roamingPath, bool esync = true, bool fsync = false, string? debugVars = "-all")
    {
        var logFile = new FileInfo(Path.Combine(roamingPath, "wine.log"));
        var home    = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 1. XIVLauncherCN.Core（xlcore）使用的 xom wine：复用其 Prefix，避免重新安装游戏
        var xlcoreRoot = Path.Combine(home, ".xlcore_cn");
        var xlcoreBins = new[]
        {
            Path.Combine(xlcoreRoot, "compatibilitytool", "beta", "wine", "bin"),
            Path.Combine(xlcoreRoot, "compatibilitytool", "wine", "bin")
        };
        foreach (var bin in xlcoreBins)
        {
            if (!File.Exists(Path.Combine(bin, "wine64")))
                continue;

            var xlcorePrefix = new DirectoryInfo(Path.Combine(xlcoreRoot, "wineprefix"));
            var prefix       = xlcorePrefix.Exists ? xlcorePrefix : new DirectoryInfo(Path.Combine(roamingPath, "wineprefix"));
            Log.Information("[WineSettings] 检测到 xlcore/xom Wine: {Bin} (Prefix: {Prefix})", bin, prefix.FullName);
            return new WineSettings(WineStartupType.Xlcore, bin, prefix, logFile, esync, fsync, debugVars);
        }

        var prefixDefault = new DirectoryInfo(Path.Combine(roamingPath, "wineprefix"));

        // 2. CrossOver
        var crossoverCandidates = new[]
        {
            "/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin",
            Path.Combine(home, "Applications", "CrossOver.app", "Contents", "SharedSupport", "CrossOver", "bin")
        };
        foreach (var bin in crossoverCandidates)
        {
            if (File.Exists(Path.Combine(bin, "wine64")))
            {
                Log.Information("[WineSettings] 检测到 CrossOver: {Bin}", bin);
                return new WineSettings(WineStartupType.CrossOver, bin, prefixDefault, logFile, esync, fsync, debugVars);
            }
        }

        // 3. Whisky
        var whiskyBottles = new DirectoryInfo(Path.Combine(home, "Library", "Application Support", "com.isaacmarovitz.Whisky", "Bottles"));
        if (whiskyBottles.Exists)
        {
            var whiskyBin = Path.Combine(home, "Library", "Application Support", "com.isaacmarovitz.Whisky", "Library", "wine");
            if (File.Exists(Path.Combine(whiskyBin, "wine64")))
            {
                Log.Information("[WineSettings] 检测到 Whisky: {Bin}", whiskyBin);
                return new WineSettings(WineStartupType.Whisky, whiskyBin, prefixDefault, logFile, esync, fsync, debugVars);
            }
        }

        // 4. Homebrew / 系统 Wine
        var systemCandidates = new[]
        {
            "/opt/homebrew/bin",   // Apple Silicon
            "/usr/local/bin",      // Intel
            "/usr/bin"
        };
        foreach (var bin in systemCandidates)
        {
            if (File.Exists(Path.Combine(bin, "wine64")) || File.Exists(Path.Combine(bin, "wine")))
            {
                Log.Information("[WineSettings] 检测到系统 Wine: {Bin}", bin);
                return new WineSettings(WineStartupType.System, bin, prefixDefault, logFile, esync, fsync, debugVars);
            }
        }

        Log.Warning("[WineSettings] 未检测到可用的 Wine 环境");
        return null;
    }

    /// <summary>
    ///     在 PATH 中查找 wine/wine64。
    /// </summary>
    public static string? FindWineInPath()
    {
        foreach (var name in new[] { "wine64", "wine" })
        {
            var psi = new ProcessStartInfo("/usr/bin/which")
            {
                Arguments = name,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            try
            {
                using var proc = Process.Start(psi);
                var path = proc?.StandardOutput.ReadToEnd().Trim();
                proc?.WaitForExit(2000);

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[WineSettings] which 查找 {Name} 失败", name);
            }
        }

        return null;
    }
}
