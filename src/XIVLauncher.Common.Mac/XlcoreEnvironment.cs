namespace XIVLauncher.Common.Mac;

/// <summary>
///     既有 XIVLauncherCN.Core（xlcore）数据目录的约定路径。
///     当本机已装过 xlcore 时，新启动器直接复用其 Wine Prefix、游戏目录与卫月数据，
///     避免重新安装 100GB+ 的游戏。
/// </summary>
public static class XlcoreEnvironment
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".xlcore_cn");

    /// <summary>本机是否存在可复用的 xlcore 安装</summary>
    public static bool IsPresent =>
        File.Exists(Path.Combine(Root, "launcher.ini")) || Directory.Exists(GameDirectory.FullName);

    public static DirectoryInfo GameDirectory       => new(Path.Combine(Root, "ffxiv"));
    public static DirectoryInfo PrefixDirectory     => new(Path.Combine(Root, "wineprefix"));
    public static DirectoryInfo DalamudAddonDirectory => new(Path.Combine(Root, "dalamud"));
    public static DirectoryInfo DalamudAssetsDirectory => new(Path.Combine(Root, "dalamudAssets"));
    public static DirectoryInfo RuntimeDirectory    => new(Path.Combine(Root, "runtime"));
    public static DirectoryInfo ConfigDirectory     => new(Root);
    public static DirectoryInfo LogDirectory        => new(Path.Combine(Root, "logs"));
    public static DirectoryInfo PatchDirectory      => new(Path.Combine(Root, "patch"));
}
