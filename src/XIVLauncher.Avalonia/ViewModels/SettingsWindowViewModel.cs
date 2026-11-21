using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using XIVLauncher.Common.Mac.Compatibility;
using XIVLauncher.Dalamud;
using XIVLauncher.Settings;

namespace XIVLauncher.ViewModels;

public partial class SettingsWindowViewModel : ObservableObject
{
    private readonly LauncherSettingsV3 settings;

    public SettingsWindowViewModel(LauncherSettingsV3 settings, string gamePath, WineSettings? wine)
    {
        this.settings = settings;

        GamePath             = settings.GamePath?.FullName ?? gamePath;
        PatchPath            = settings.PatchPath?.FullName ?? string.Empty;
        AdditionalLaunchArgs = settings.AdditionalLaunchArgs;
        CanEncryptLaunchArguments = wine?.StartupType != WineStartupType.Xlcore;
        EncryptArgumentsV2 = CanEncryptLaunchArguments && settings.EncryptArgumentsV2;
        ExitLauncherWhenGameExit = settings.ExitLauncherWhenGameExit;
        DalamudEnabled       = settings.DalamudEnabled;
        DalamudDelayMs       = settings.DalamudInjectionDelayMS;

        WineInfo = wine == null
                       ? "未检测到 Wine / CrossOver / Whisky。请先安装其中之一。"
                       : $"{wine.StartupType} · {wine.Wine64Path}\nPrefix: {wine.Prefix.FullName}";
    }

    public bool CanEncryptLaunchArguments { get; }

    public ObservableCollection<LoadMethodOption> LoadMethods { get; } =
    [
        new(DalamudLoadMethod.EntryPoint, "EntryPoint（推荐，启动时注入）"),
        new(DalamudLoadMethod.ACLonly, "ACL only（不注入，仅修复 ACL）")
    ];

    [ObservableProperty]
    public partial string GamePath { get; set; }

    [ObservableProperty]
    public partial string PatchPath { get; set; }

    [ObservableProperty]
    public partial string AdditionalLaunchArgs { get; set; }

    [ObservableProperty]
    public partial bool EncryptArgumentsV2 { get; set; }

    [ObservableProperty]
    public partial bool ExitLauncherWhenGameExit { get; set; }

    [ObservableProperty]
    public partial bool DalamudEnabled { get; set; }

    [ObservableProperty]
    public partial decimal DalamudDelayMs { get; set; }

    [ObservableProperty]
    public partial LoadMethodOption? SelectedLoadMethod { get; set; }

    [ObservableProperty]
    public partial string WineInfo { get; set; }

    public void InitializeSelection() =>
        SelectedLoadMethod = LoadMethods.FirstOrDefault(x => x.Value == settings.DalamudLoadMethod) ?? LoadMethods[0];

    public void Save()
    {
        var gamePath = ResolveDirectory(GamePath, "游戏目录");
        var patchPath = ResolveDirectory(PatchPath, "补丁缓存");
        settings.GamePath = gamePath;
        settings.PatchPath = patchPath;

        settings.AdditionalLaunchArgs      = AdditionalLaunchArgs;
        if (CanEncryptLaunchArguments)
            settings.EncryptArgumentsV2 = EncryptArgumentsV2;
        settings.ExitLauncherWhenGameExit  = ExitLauncherWhenGameExit;
        settings.DalamudEnabled            = DalamudEnabled;
        settings.DalamudInjectionDelayMS   = Math.Clamp(DalamudDelayMs, 0, 10000);

        if (SelectedLoadMethod != null)
            settings.DalamudLoadMethod = SelectedLoadMethod.Value;

        settings.Save();
    }

    private static DirectoryInfo? ResolveDirectory(string input, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var path = input.Trim();
        if (!Path.IsPathFullyQualified(path) || File.Exists(path))
            throw new ArgumentException($"{fieldName}需要使用文件夹的完整路径。");

        return new DirectoryInfo(path);
    }
}

public sealed record LoadMethodOption(DalamudLoadMethod Value, string Label);
