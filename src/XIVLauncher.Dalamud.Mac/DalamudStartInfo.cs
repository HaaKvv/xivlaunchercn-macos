namespace XIVLauncher.Dalamud;

[Serializable]
public sealed class DalamudStartInfo
{
    public string WorkingDirectory  = string.Empty;
    public string ConfigurationPath = string.Empty;
    public string LoggingPath       = string.Empty;

    public string PluginDirectory = string.Empty;
    public string AssetDirectory  = string.Empty;
    public int    DelayInitializeMs;

    public string GameVersion             = string.Empty;
    public string TroubleshootingPackData = string.Empty;
    public string LauncherDirectory       = Environment.CurrentDirectory;

    /// <summary>客户端语言（Dalamud ClientLanguage 枚举：4 = 简体中文）</summary>
    public int ClientLanguage = 4;
}
