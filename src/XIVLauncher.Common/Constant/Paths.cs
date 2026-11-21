using Serilog;
using XIVLauncher.Common.Util;

namespace XIVLauncher.Common.Constant;

public static class Paths
{
    public static string ResourcesPath { get; } =
        Path.Join(AppContext.BaseDirectory, "Resources");

#if XL_MAC
    /// <summary>
    ///     macOS 数据目录：~/Library/Application Support/XIVLauncherCN
    ///     （等效于 Windows 的 %AppData%\XIVLauncherCN）
    /// </summary>
    public static string RoamingPath { get; private set; } =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncherCN");
#else
    public static string RoamingPath { get; private set; } =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncherCN");
#endif

    public static void OverrideRoamingPath(string path) =>
        RoamingPath = ExpandEnvironmentVariables(path);

#if XL_MAC
    /// <summary>
    ///     macOS 上展开环境变量：.NET 的 Environment.ExpandEnvironmentVariables 仅支持
    ///     Windows 的 %VAR% 语法，这里补充 Unix 风格 $VAR / ${VAR}（含 $HOME）。
    /// </summary>
    private static string ExpandEnvironmentVariables(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        return System.Text.RegularExpressions.Regex.Replace(
            path,
            @"\$(?:\{([A-Za-z_][A-Za-z0-9_]*)\}|([A-Za-z_][A-Za-z0-9_]*))",
            match =>
            {
                var name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                return Environment.GetEnvironmentVariable(name) ?? match.Value;
            });
    }
#else
    private static string ExpandEnvironmentVariables(string path) =>
        Environment.ExpandEnvironmentVariables(path);
#endif

    public static string ResolveExecutablePath()
    {
        var currentProcessPath = Environment.ProcessPath;
        if (IsExecutableFile(currentProcessPath))
            return currentProcessPath!;

        var launcherExecutablePath = Path.Combine(AppContext.BaseDirectory, "XIVLauncherCN");
        if (IsExecutableFile(launcherExecutablePath))
            return launcherExecutablePath;

        return currentProcessPath ?? launcherExecutablePath;
    }

    private static bool IsExecutableFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var attributes = File.GetAttributes(filePath);
            if ((attributes & FileAttributes.Directory) != 0)
                return false;

            // 必须是文件且存在
            return File.Exists(filePath)
#if XL_MAC
                   // macOS 上可执行文件无固定扩展名（.app 是目录已被排除，.bin/无扩展名均可）
                   && (Path.GetExtension(filePath) is "" or ".bin");
#else
                   && string.Equals(Path.GetExtension(filePath), ".exe", StringComparison.OrdinalIgnoreCase);
#endif
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static DirectoryInfo ResolvePatchPath(DirectoryInfo? configuredPatchPath, string roamingPath)
    {
        if (configuredPatchPath is { Exists: false })
            configuredPatchPath = null;

        return configuredPatchPath ?? new DirectoryInfo(Path.Combine(roamingPath, "patches"));
    }

    public static string GetConfigPath(string prefix = "launcher") =>
        Path.Join(RoamingPath, $"{prefix}ConfigV3.json");

    /// <summary>
    ///     获取国服游戏安装路径。
    ///     <para>Windows 版从注册表读取；macOS 版无法使用注册表，
    ///     依次检查：用户配置文件中的自定义路径 → 常见安装位置扫描 → 默认路径。</para>
    /// </summary>
    public static string GetGamePath()
    {
        var defaultPath = GetDefaultGamePath();

        // 1. 用户配置文件中的路径优先
        var configPath = GetConfigPath();
        try
        {
            if (File.Exists(configPath))
            {
                using var doc   = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
                if (doc.RootElement.TryGetProperty("GamePath", out var gamePathProp)
                    && gamePathProp.ValueKind == System.Text.Json.JsonValueKind.String
                    && GameHelpers.IsValidGamePath(gamePathProp.GetString()!))
                {
                    return gamePathProp.GetString()!;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Paths] 读取配置文件中的游戏路径失败");
        }

        // 2. 扫描常见安装位置
        var foundPaths = new List<(string Path, SeVersion Version)>();

        foreach (var candidate in GetCandidateGamePaths())
        {
            if (!Directory.Exists(candidate) || !GameHelpers.IsValidGamePath(candidate))
                continue;

            try
            {
                var versionText = Repository.Ffxiv.GetVer(new DirectoryInfo(candidate));
                foundPaths.Add((candidate, SeVersion.Parse(versionText)));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[Paths] 解析游戏版本失败: {Path}", candidate);
            }
        }

        if (foundPaths.Count > 0)
            return foundPaths.OrderByDescending(x => x.Version).Select(x => x.Path).First();

        return defaultPath;
    }

#if XL_MAC
    /// <summary>
    ///     macOS 上常见国服游戏安装位置（用户通过 Wine/CrossOver 安装的目录）。
    /// </summary>
    private static IEnumerable<string> GetCandidateGamePaths()
    {
        var candidates = new List<string>();

        // 用户家目录下的常见位置
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(home, ".xlcore_cn", "ffxiv"));
        candidates.Add(Path.Combine(home, "最终幻想XIV"));
        candidates.Add(Path.Combine(home, "Games", "最终幻想XIV"));
        candidates.Add(Path.Combine(home, "Games", "最终幻想14"));
        candidates.Add(Path.Combine(home, "最终幻想14"));

        // CrossOver / Wine Bottle 中的常见安装位置（prefix 内的 Windows 路径映射到 macOS）
        candidates.Add(Path.Combine(home, "Library", "Application Support", "CrossOver", "Bottles"));
        candidates.Add(Path.Combine(home, "Library", "Application Support", "com.codeweavers.crossover"));

        // 外部卷（移动硬盘等）
        foreach (var volume in Directory.GetDirectories("/Volumes"))
        {
            candidates.Add(Path.Combine(volume, "最终幻想XIV"));
            candidates.Add(Path.Combine(volume, "最终幻想14"));
            candidates.Add(Path.Combine(volume, "Games", "最终幻想XIV"));
        }

        return candidates;
    }

    private static string GetDefaultGamePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "最终幻想XIV");
#else
    private static IEnumerable<string> GetCandidateGamePaths()
    {
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"上海数龙科技有限公司\最终幻想XIV");
        yield return defaultPath;
    }

    private static string GetDefaultGamePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"上海数龙科技有限公司\最终幻想XIV");
#endif
}
