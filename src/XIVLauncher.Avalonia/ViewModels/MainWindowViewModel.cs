using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using XIVLauncher.Account;
using XIVLauncher.Common;
using XIVLauncher.Common.Constant;
using XIVLauncher.Common.Game;
using XIVLauncher.Common.Http.Site;
using XIVLauncher.Common.Mac;
using XIVLauncher.Common.Mac.Compatibility;
using XIVLauncher.Common.Util;
using XIVLauncher.DCTravel;
using XIVLauncher.Dalamud;
using XIVLauncher.GamePatchV3.Repair;
using XIVLauncher.GamePatchV3.Update.Models;
using XIVLauncher.Login.Models;
using XIVLauncher.Login.Workflow;
using XIVLauncher.Services;
using XIVLauncher.Settings;
using XIVLauncher.Views;

namespace XIVLauncher.ViewModels;

/// <summary>
///     主窗口视图模型。
///     <para>macOS 移植：等价于原项目 Windows/ViewModel/Main/MainWindowViewModel.cs，
///     将 WPF 专属的 Window/Dispatcher 依赖替换为平台无关实现。</para>
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private LoginWorkflowService? loginWorkflowService;
    private readonly Launcher             launcher = new();
    private AccountManager? accountManager;
    private readonly GameInstallService   installService = new();
    private readonly GameMaintenanceService maintenanceService = new();
    private readonly LauncherSettingsV3   settings;

    private CompatibilityTools? compatibilityTools;
    private DalamudService?     dalamudService;

    private CancellationTokenSource? loginCts;
    private uint updateCheckRevision;
    private bool isTravelLogin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLaunchMessage))]
    public partial string? LaunchMessage { get; set; }

    [ObservableProperty]
    public partial bool ShowLaunchRecovery { get; set; }

    [ObservableProperty]
    public partial bool NeedsGameUpdate { get; set; }

    public bool ShowLaunchMessage => LaunchMessage != null;

    /// <summary>新闻列表</summary>
    public ObservableCollection<News> News { get; } = [];

    /// <summary>大区列表</summary>
    public ObservableCollection<LoginArea> Areas { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<XIVAccount> Accounts { get; set; } = [];

    [ObservableProperty]
    public partial bool IsInitializing { get; set; } = true;

    [ObservableProperty]
    public partial bool HasGame { get; set; }

    [ObservableProperty]
    public partial bool CanRetryAreas { get; set; }

    [ObservableProperty]
    public partial string NewsStatus { get; set; } = "正在加载新闻…";

    [ObservableProperty]
    public partial bool ShowNewsStatus { get; set; } = true;

    [ObservableProperty]
    public partial int DashboardTabIndex { get; set; }

    /// <summary>当前界面状态文本</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>是否正在登录</summary>
    [ObservableProperty]
    public partial bool IsLoggingIn { get; set; }

    /// <summary>是否正在启动游戏</summary>
    [ObservableProperty]
    public partial bool IsLaunching { get; set; }

    /// <summary>游戏是否在运行</summary>
    [ObservableProperty]
    public partial bool IsGameRunning { get; set; }

    /// <summary>是否正在下载游戏</summary>
    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    /// <summary>下载进度百分比 (0-100)</summary>
    [ObservableProperty]
    public partial double DownloadPercent { get; set; }

    /// <summary>下载当前阶段文本（初始即显示按钮默认文案）</summary>
    [ObservableProperty]
    public partial string DownloadPhase { get; set; } = "下载并安装完整游戏";

    /// <summary>下载详情文本</summary>
    [ObservableProperty]
    public partial string DownloadDetail { get; set; } = string.Empty;

    /// <summary>网络诊断摘要</summary>
    [ObservableProperty]
    public partial string NetworkDiagnosis { get; set; } = "检测登录服务器是否可达。";

    /// <summary>登录二维码图片（扫码登录时显示，null 表示未生成）</summary>
    [ObservableProperty]
    public partial Avalonia.Media.Imaging.Bitmap? QrCodeImage { get; set; }

    /// <summary>是否正在显示二维码</summary>
    [ObservableProperty]
    public partial bool IsQrCodeVisible { get; set; }

    /// <summary>游戏版本</summary>
    [ObservableProperty]
    public partial string GameVersion { get; set; } = "未知";

    /// <summary>Dalamud 版本</summary>
    [ObservableProperty]
    public partial string DalamudVersion { get; set; } = "待检查";

    /// <summary>是否正在执行游戏维护（更新/修复）</summary>
    [ObservableProperty]
    public partial bool IsGameMaintenanceBusy { get; set; }

    /// <summary>游戏维护状态文本</summary>
    [ObservableProperty]
    public partial string MaintenanceStatus { get; set; } = "尚未检查更新";

    /// <summary>是否启用卫月（Dalamud）</summary>
    [ObservableProperty]
    public partial bool IsDalamudEnabled { get; set; } = true;

    partial void OnIsDalamudEnabledChanged(bool value)
    {
        LaunchWithoutDalamudCommand.NotifyCanExecuteChanged();
        if (settings.DalamudEnabled == value)
            return;

        settings.DalamudEnabled = value;
        settings.Save();
    }

    /// <summary>游戏安装路径</summary>
    [ObservableProperty]
    public partial string GamePathText { get; set; } = string.Empty;

    /// <summary>当前选中的大区</summary>
    [ObservableProperty]
    public partial LoginArea? SelectedArea { get; set; }

    /// <summary>当前选中登录类型</summary>
    [ObservableProperty]
    public partial LoginType SelectedLoginType { get; set; } = LoginType.QRCode;

    partial void OnSelectedLoginTypeChanged(LoginType value)
    {
        OnPropertyChanged(nameof(IsStaticSelected));
        OnPropertyChanged(nameof(IsSlideSelected));
        OnPropertyChanged(nameof(IsQrSelected));
        OnPropertyChanged(nameof(ShowUsernameInput));
        OnPropertyChanged(nameof(LoginButtonText));

        if (value != LoginType.QRCode)
            ClearLoginQr();
    }

    public bool IsStaticSelected
    {
        get => SelectedLoginType == LoginType.Static;
        set { if (value) SelectedLoginType = LoginType.Static; }
    }

    public bool IsSlideSelected
    {
        get => SelectedLoginType == LoginType.Slide;
        set { if (value) SelectedLoginType = LoginType.Slide; }
    }

    public bool IsQrSelected
    {
        get => SelectedLoginType == LoginType.QRCode;
        set { if (value) SelectedLoginType = LoginType.QRCode; }
    }

    public bool ShowUsernameInput => SelectedLoginType != LoginType.QRCode;

    public string LoginButtonText => (IsLoggingIn, IsLaunching, IsGameRunning, SelectedLoginType) switch
    {
        (true, _, _, _)              => isTravelLogin ? "取消旅行登录" : "取消登录",
        (_, true, _, _)              => "正在启动…",
        (_, _, true, _)              => "游戏运行中",
        (_, _, _, LoginType.QRCode)  => "扫码登录并启动",
        (_, _, _, LoginType.Slide)   => "一键登录并启动",
        _                            => "登录并启动游戏"
    };

    public bool CanEditLogin => !IsInitializing && !IsLoggingIn && !IsLaunching && !IsGameRunning
                             && !IsGameMaintenanceBusy && !IsDownloading;

    partial void OnIsInitializingChanged(bool value) => NotifyOperationStateChanged();
    partial void OnIsLoggingInChanged(bool value) => NotifyOperationStateChanged();
    partial void OnIsLaunchingChanged(bool value) => NotifyOperationStateChanged();
    partial void OnIsGameRunningChanged(bool value) => NotifyOperationStateChanged();
    partial void OnIsGameMaintenanceBusyChanged(bool value) => NotifyOperationStateChanged();
    partial void OnIsDownloadingChanged(bool value) => NotifyOperationStateChanged();
    partial void OnHasGameChanged(bool value) => NotifyOperationStateChanged();
    partial void OnSelectedAreaChanged(LoginArea? value) => NotifyOperationStateChanged();

    private void NotifyOperationStateChanged()
    {
        OnPropertyChanged(nameof(CanEditLogin));
        OnPropertyChanged(nameof(LoginButtonText));
        PrimaryLoginCommand.NotifyCanExecuteChanged();
        RetryLaunchCommand.NotifyCanExecuteChanged();
        LaunchWithoutDalamudCommand.NotifyCanExecuteChanged();
        OpenAccountsCommand.NotifyCanExecuteChanged();
        OpenTravelCommand.NotifyCanExecuteChanged();
        OpenSettingsCommand.NotifyCanExecuteChanged();
        DownloadGameCommand.NotifyCanExecuteChanged();
        CheckGameUpdateCommand.NotifyCanExecuteChanged();
        UpdateGameCommand.NotifyCanExecuteChanged();
        RepairGameCommand.NotifyCanExecuteChanged();
        UpdateDalamudCommand.NotifyCanExecuteChanged();
    }

    /// <summary>主按钮：未登录时发起登录；登录中则取消当前登录。</summary>
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanUsePrimaryLogin))]
    private async Task PrimaryLoginAsync()
    {
        if (IsLoggingIn)
        {
            Log.Information("[MainWindow] 用户取消登录");
            loginCts?.Cancel();
            StatusText = "正在取消登录…";
            return;
        }

        await LoginAsync(useDalamud: IsDalamudEnabled);
    }

    [RelayCommand(CanExecute = nameof(CanRetryLaunch))]
    private Task RetryLaunchAsync() => LoginAsync(useDalamud: IsDalamudEnabled);

    [RelayCommand(CanExecute = nameof(CanLaunchWithoutDalamud))]
    private Task LaunchWithoutDalamudAsync() => LoginAsync(useDalamud: false);

    private bool CanRetryLaunch() => CanEditLogin && CanUsePrimaryLogin();
    private bool CanLaunchWithoutDalamud() => CanRetryLaunch() && IsDalamudEnabled;

    [RelayCommand]
    private void OpenGameMaintenance() => DashboardTabIndex = 1;

    [RelayCommand]
    private void OpenLaunchLogs() => OpenWithSystem(Path.Combine(Paths.RoamingPath, "logs"));

    [RelayCommand]
    private void OpenDalamudLogs() => OpenWithSystem(XlcoreEnvironment.IsPresent
        ? XlcoreEnvironment.LogDirectory.FullName : Path.Combine(Paths.RoamingPath, "logs"));

    private bool CanUsePrimaryLogin() => IsLoggingIn
                                      || CanEditLogin && HasGame && SelectedArea != null
                                         && compatibilityTools != null && loginWorkflowService != null;

    private bool CanOpenSettings() => CanEditLogin;
    private bool CanOpenAccounts() => CanEditLogin && accountManager != null;
    private bool CanOpenTravel() => CanEditLogin && SelectedArea != null && loginWorkflowService != null;
    private bool CanMaintainGame() => CanEditLogin && HasGame;
    private bool CanUpdateDalamud() => CanEditLogin && dalamudService != null;
    private bool CanDownloadGame() => CanEditLogin && !HasGame;

    [RelayCommand(CanExecute = nameof(CanOpenAccounts))]
    private async Task OpenAccountsAsync()
    {
        if (accountManager is not { } manager ||
            Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return;
        try
        {
            await new AccountWindow(manager, settings, Areas.Select(area => area.AreaName)).ShowDialog(owner);
            RefreshAccounts();
            if (SelectedAccount == null)
                Username = Password = string.Empty;
            else
                ApplyAccountSelection();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MainWindow] 账号管理窗口打开失败");
            StatusText = "无法打开账号管理：" + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenTravel))]
    private Task OpenTravelAsync() => LoginAsync(IsDalamudEnabled, travel: true);

    /// <summary>当前选中的账号（用于快速切换）</summary>
    public XIVAccount? SelectedAccount
    {
        get => accountManager?.CurrentAccount;
        set
        {
            if (value == null || accountManager == null || IsLoggingIn || ReferenceEquals(value, accountManager.CurrentAccount))
                return;

            accountManager.CurrentAccount = value;
            ApplyAccountSelection();
        }
    }

    /// <summary>登录用户名</summary>
    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    /// <summary>登录密码</summary>
    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    public MainWindowViewModel()
    {
        settings = LauncherSettingsV3.Load(Paths.GetConfigPath());
        IsDalamudEnabled = settings.DalamudEnabled;
    }

    public async Task InitializeAsync()
    {
        StatusText = "正在初始化…";
        var areasTask = ReloadAreasCommand.ExecuteAsync(null);

        try
        {
            await Task.Run(InitializeLocalEnvironment);
            await DetectGameAsync();
            await areasTask;
            RefreshAccounts();
            ApplyAccountSelection();

            StatusText = compatibilityTools == null ? "未找到可用的 Wine 环境"
                       : !HasGame                  ? "请在设置中选择游戏目录"
                       : SelectedArea == null      ? "大区列表加载失败，请重试"
                       : "就绪";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainWindow] 初始化失败");
            StatusText = "初始化失败: " + ex.Message;
        }
        finally
        {
            IsInitializing = false;
        }

        if (settings.AutoLoginToLastAccount && CanRetryLaunch() &&
            accountManager is { HasCurrentAccountSelection: true } manager &&
            SelectedAccount is { IsWeGame: false, QuickLoginEnabled: true, SdoQuickLoginSecret: { Length: > 0 } } account &&
            !manager.HasUnavailableSecrets(account))
        {
            await LoginAsync(IsDalamudEnabled, automatic: true);
            return;
        }

        if (HasGame && CanEditLogin)
            await CheckGameUpdateAsync(background: true);
    }

    private void InitializeLocalEnvironment()
    {
        accountManager = new AccountManager(new SettingsStoreAdapter(settings));
        loginWorkflowService = new LoginWorkflowService(accountManager, new XIVLauncher.Login.WeGame.WeGameTokenCaptureCoordinator());

        var useXlcore = XlcoreEnvironment.IsPresent;
        var addonDir = new DirectoryInfo(Path.Combine(Paths.RoamingPath, "addon"));
        var runtimeDir = useXlcore ? XlcoreEnvironment.RuntimeDirectory
                                   : new DirectoryInfo(Path.Combine(Paths.RoamingPath, "runtime"));
        var assetsDir = useXlcore ? XlcoreEnvironment.DalamudAssetsDirectory
                                  : new DirectoryInfo(Path.Combine(Paths.RoamingPath, "dalamudAssets"));
        var configDir = useXlcore ? XlcoreEnvironment.ConfigDirectory
                                  : new DirectoryInfo(Path.Combine(Paths.RoamingPath, "dalamudConfig"));
        var logDir = useXlcore ? XlcoreEnvironment.LogDirectory
                               : new DirectoryInfo(Path.Combine(Paths.RoamingPath, "logs"));

        if (settings.PatchPath == null)
            settings.PatchPath = useXlcore ? XlcoreEnvironment.PatchDirectory
                                           : new DirectoryInfo(Path.Combine(Paths.RoamingPath, "patches"));

        var wineSettings = WineSettings.Detect(Paths.RoamingPath);
        if (wineSettings == null)
            return;

        var compatibility = new CompatibilityTools(wineSettings);
        compatibility.EnsurePrefix();
        if (!compatibility.IsToolReady)
        {
            compatibility.Dispose();
            return;
        }

        compatibilityTools = compatibility;
        Log.Information("[MainWindow] Wine 环境就绪: {Type} @ {Path}", wineSettings.StartupType, wineSettings.Wine64Path);
        dalamudService = new DalamudService
        (
            new DalamudHostPaths(addonDir, runtimeDir, assetsDir, configDir, logDir, new DirectoryInfo(AppContext.BaseDirectory)),
            progressSink: null,
            new GameVersionProvider(),
            new EmptyTroubleshootingProvider(),
            new MacDalamudRunner(compatibility, runtimeDir)
        );
    }

    [RelayCommand]
    private async Task ReloadAreasAsync()
    {
        CanRetryAreas = false;
        try
        {
            var areas = await LoginArea.Get();
            Areas.Clear();
            foreach (var area in areas)
                Areas.Add(area);

            SelectedArea = Areas.FirstOrDefault();
            if (!IsInitializing)
                ApplyAccountSelection();

            CanRetryAreas = SelectedArea == null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MainWindow] 加载大区列表失败");
            CanRetryAreas = true;
        }

        if (!IsInitializing)
            StatusText = CanRetryAreas ? "大区列表加载失败，请检查网络" : "大区列表已加载";
    }

    [RelayCommand]
    private async Task RefreshNewsAsync()
    {
        if (News.Count == 0)
        {
            NewsStatus = "正在加载新闻…";
            ShowNewsStatus = true;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var headlines = await Headlines.GetHeadlinesAsync(launcher, timeout.Token);
            News.Clear();
            foreach (var news in headlines.News.Take(10))
                News.Add(news);

            NewsStatus = "暂无新闻";
            ShowNewsStatus = News.Count == 0;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MainWindow] 加载新闻失败");
            NewsStatus = "新闻加载失败，可重新刷新";
            ShowNewsStatus = true;
        }
    }

    [RelayCommand]
    private void OpenNews(News? news)
    {
        if (news != null)
            OpenWithSystem(news.Url);
    }

    private void ApplyAccountSelection()
    {
        if (SelectedAccount is not { } account)
            return;

        Username = account.SdoLoginAccount ?? string.Empty;
        Password = string.Empty;
        SelectedArea = Areas.FirstOrDefault(area => area.AreaName == account.AreaName) ?? SelectedArea;
        OnPropertyChanged(nameof(SelectedAccount));
    }

    private void RefreshAccounts()
    {
        Accounts = accountManager?.Accounts.Where(account => !account.IsWeGame).ToArray() ?? [];
        OnPropertyChanged(nameof(SelectedAccount));
    }

    private async Task DetectGameAsync()
    {
        var gamePath = settings.GamePath?.FullName ?? await Task.Run(Paths.GetGamePath);
        var version = await Task.Run(() =>
        {
            try
            {
                return GameHelpers.IsValidGamePath(gamePath) ? Repository.Ffxiv.GetVer(new DirectoryInfo(gamePath)) : null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[MainWindow] 读取游戏版本失败");
                return null;
            }
        });

        GamePathText = gamePath;
        GameVersion = version ?? "未检测到游戏";
        HasGame = version != null;
    }

    private async Task LoginAsync(bool useDalamud, bool travel = false, bool automatic = false)
    {
        if (!(travel ? CanOpenTravel() : CanRetryLaunch()) || loginWorkflowService == null || SelectedArea == null)
            return;

        using var travelClient = travel ? new DCTravelClient(string.Empty) : null;
        isTravelLogin = travel;
        ShowLaunchRecovery = false;
        LaunchMessage = travel ? "请登录后管理跨大区旅行。" : null;
        IsLoggingIn = true;
        StatusText = automatic ? "正在尝试用上次账号登录…" : "正在登录…";

        var loginType = automatic ? LoginType.QuickLogin : SelectedLoginType;

        Log.Information
        (
            "[MainWindow] 开始登录: type={Type}, area={Area}",
            loginType,
            SelectedArea.AreaName
        );

        using var cancellation = new CancellationTokenSource();
        loginCts = cancellation;

        try
        {
            var request = new LoginWorkflowRequest
            {
                LoginType                 = loginType,
                Username                  = Username,
                Password                  = Password,
                QuickLoginEnabled         = true,
                ForceWeGameTokenRecapture = false,
                Action                    = LoginAfterAction.Start,
                CurrentArea               = SelectedArea,
                LoginAreas                = Areas.ToArray(),
                LoginCancellationTokenSource = cancellation,
                LoginSessionRefreshSink   = travelClient,
                Interaction               = new MacLoginWorkflowInteraction(this, cancellation.Token),
                RequireDeviceProfileSetupForNewLogin = false
            };

            var result = await loginWorkflowService.ExecuteAsync(request);

            Log.Information("[MainWindow] 登录工作流返回: {HasResult}", result != null);
            cancellation.Token.ThrowIfCancellationRequested();

            if (result == null)
            {
                ReportLaunchFailure(automatic ? "自动登录凭据不可用，请手动登录。" : "登录失败，请重新登录。");
                if (!automatic)
                    await SuggestNetworkFixIfBlockedAsync();
                return;
            }

            ClearLoginQr();
            updateCheckRevision++;
            IsLaunching = true;
            IsLoggingIn = false;
            RefreshAccounts();
            if (result.IsAccountPersisted)
                ApplyAccountSelection();
            Password = string.Empty;
            if (travelClient != null)
            {
                var selection = await ShowTravelAsync(travelClient, result);
                if (selection?.Destination is { Length: > 0 } destination)
                {
                    var area = Areas.FirstOrDefault(item => string.Equals(item.AreaName, destination, StringComparison.Ordinal));
                    if (area == null)
                    {
                        ReportLaunchFailure($"本地大区列表没有「{destination}」，请刷新大区列表后再启动。");
                        return;
                    }
                    SelectedArea = area;
                    result.GameLaunchContext.Area = area;
                }
                LaunchMessage = null;
                if (selection?.StartGame != true)
                {
                    StatusText = selection?.Destination is { Length: > 0 } chosen ? $"已选择 {chosen}" : "已关闭跨大区旅行";
                    return;
                }
            }
            ReportLaunchStatus("已登录，正在检查游戏更新…");
            await LaunchGameAsync(result, useDalamud);
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            LaunchMessage = null;
            StatusText = "已取消登录";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainWindow] 登录异常");
            ReportLaunchFailure(automatic ? "自动登录失败，请手动登录。" : "登录或旅行会话失败：" + ex.Message);
            if (!automatic)
                await SuggestNetworkFixIfBlockedAsync();
        }
        finally
        {
            ClearLoginQr();
            loginCts = null;
            isTravelLogin = false;
            IsLoggingIn = false;
            IsLaunching = false;
            Log.Information("[MainWindow] 登录流程结束");
        }
    }

    private async Task<TravelWindowResult?> ShowTravelAsync(DCTravelClient client, LoginWorkflowResult login)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return null;
        if (client.LoginSessionRefreshContext == null)
            throw new InvalidOperationException("未获取盛趣旅行会话，请重新登录。");

        client.RefreshGameSessionIDByQuickLoginFunc = login.RefreshGameSessionIdByQuickLoginFunc;
        var label = SelectedAccount?.DisplayName ?? "盛趣账号";
        return await new TravelWindow(client, label, HasGame && compatibilityTools != null).ShowDialog<TravelWindowResult?>(owner);
    }

    /// <summary>
    ///     登录失败时检测网络劫持，若被劫持则自动生成诊断信息与修复指引。
    /// </summary>
    private async Task SuggestNetworkFixIfBlockedAsync()
    {
        try
        {
            var report = await NetworkDiagnostics.DiagnoseAsync();
            if (!report.LoginBlocked)
                return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("⚠️ 登录失败可能由网络劫持导致：");
            sb.AppendLine();
            foreach (var result in report.Results.Where(r => r.IsHijacked))
                sb.AppendLine(result.Detail);
            sb.AppendLine();
            sb.AppendLine("可以在此页生成 hosts 修复脚本。");

            NetworkDiagnosis = sb.ToString();
            DashboardTabIndex = 2;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[MainWindow] 登录失败后的网络诊断未执行");
        }
    }

    private void ReportLaunchStatus(string message)
    {
        LaunchMessage = message;
        StatusText = message;
    }

    private void ReportLaunchFailure(string message)
    {
        ReportLaunchStatus(message);
        ShowLaunchRecovery = true;
    }

    private async Task LaunchGameAsync(LoginWorkflowResult result, bool useDalamud)
    {
        if (compatibilityTools is not { } compatibility)
        {
            ReportLaunchFailure("缺少 Wine 环境，无法启动游戏。");
            return;
        }

        try
        {
            var context = result.GameLaunchContext;
            var oauth   = context.LoginResult.OAuthLogin;
            if (oauth == null)
            {
                ReportLaunchFailure("登录结果缺少会话信息，请重新登录。");
                return;
            }

            var gamePath = GamePathText;
            if (await Task.Run(() => FFXIVProcess.GetGameProcessIDs().Count > 0))
            {
                ReportLaunchFailure("已有 FF14 游戏进程在运行，请退出游戏后再试。");
                return;
            }

            // 启动前检查游戏更新（与 Soil 行为一致：有更新时先更新再启动）
            try
            {
                var updateCheck = await maintenanceService.CheckAsync(new DirectoryInfo(gamePath));
                ReportGameUpdate(updateCheck);
                if (updateCheck.NeedsUpdate)
                {
                    ReportLaunchStatus("需要安装游戏更新。");
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[MainWindow] 启动前更新检查失败，继续启动");
            }

            IGameRunner runner = new MacGameRunner(compatibility, useDalamud: false);
            string? dalamudLog = null;

            if (useDalamud && dalamudService is { } service)
            {
                ReportLaunchStatus("正在准备卫月…");

                try
                {
                    var gameDir = new DirectoryInfo(gamePath);

                    var dalamudSession = service.CreateLauncher
                    (
                        gameDir,
                        new DalamudLaunchOptions
                        (
                            settings.DalamudLoadMethod,
                            (int)settings.DalamudInjectionDelayMS,
                            false,
                            false,
                            false
                        )
                    );

                    service.RunUpdater();
                    await service.Updater.WaitForCompletionAsync();
                    dalamudSession.EnsureReady(gameDir);

                    runner = new MacDalamudGameRunner(dalamudSession);
                    DalamudVersion = DalamudUpdater.Version;
                    if (settings.DalamudLoadMethod == DalamudLoadMethod.EntryPoint)
                    {
                        var logs = XlcoreEnvironment.IsPresent ? XlcoreEnvironment.LogDirectory.FullName
                                                               : Path.Combine(Paths.RoamingPath, "logs");
                        dalamudLog = Path.Combine(logs, "dalamud.log");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[MainWindow] 卫月准备失败");
                    ReportLaunchFailure("卫月准备失败：" + ex.Message);
                    return;
                }
            }

            ReportLaunchStatus("正在获取游戏会话…");
            string sessionId;
            try
            {
                sessionId = await LoginWorkflowService.ResolveGameSessionIdAsync(context.LoginResult);
            }
            catch (Exception ex)
            {
                Log.Warning("[MainWindow] 游戏会话获取失败: {ErrorType}", ex.GetType().Name);
                ReportLaunchFailure("获取游戏会话失败，请重新登录。");
                return;
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                Log.Warning("[MainWindow] 未获得游戏会话票据");
                ReportLaunchFailure("未获得游戏会话，请重新登录。");
                return;
            }

            Log.Information("[MainWindow] 已获得游戏会话票据");
            ReportLaunchStatus("已获取会话，正在启动游戏进程…");
            var attemptStarted = DateTimeOffset.UtcNow;

            // xlcore/xom 的国服客户端使用与旧版 macOS 启动器相同的普通启动参数。
            var process = await Task.Run(() => launcher.LaunchGame
            (
                runner,
                sessionId,
                oauth.SndaID,
                0, // DC travel 端口, 未启用时传 0
                context.Area.AreaID,
                context.Area.AreaLobby,
                context.Area.AreaGM,
                context.Area.AreaConfigUpload,
                context.Area.AreaName,
                settings.AdditionalLaunchArgs,
                new DirectoryInfo(gamePath),
                encryptArguments: compatibility.Settings.StartupType != WineStartupType.Xlcore && settings.EncryptArgumentsV2,
                DPIAwareness.Aware
            ));

            if (process == null)
                throw new InvalidOperationException("未获得游戏进程。");

            IsGameRunning = true;
            ReportLaunchStatus("游戏进程已启动，正在等待窗口…");
            _ = MonitorGameAsync(process, dalamudLog, attemptStarted);
        }
        catch (DalamudRunnerException ex)
        {
            Log.Error(ex, "[MainWindow] 卫月启动失败");
            ReportLaunchFailure(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainWindow] 启动游戏失败");
            ReportLaunchFailure("启动游戏失败：" + ex.Message);
        }
    }

    private async Task MonitorGameAsync(FFXIVProcess process, string? dalamudLog, DateTimeOffset attemptStarted)
    {
        var timer = Stopwatch.StartNew();
        var windowSeen = false;
        var dalamudReady = false;
        int? exitCode = null;
        try
        {
            var exited = process.UnderlyingProcess.WaitForExitAsync();
            while (!exited.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(60))
            {
                if (!windowSeen && await Task.Run(() => MacGameWindow.IsVisible(process.ProcessID)))
                {
                    windowSeen = true;
                    ReportLaunchStatus(dalamudLog == null ? "已检测到游戏窗口。"
                                                         : "已检测到游戏窗口，正在确认卫月初始化…");
                }

                if (dalamudLog != null && !dalamudReady)
                    dalamudReady = await DalamudStartupLog.IsReadyAsync(dalamudLog, attemptStarted);

                if (windowSeen && (dalamudLog == null || dalamudReady))
                {
                    Log.Information("[MainWindow] 游戏窗口已出现, 卫月就绪={DalamudReady}, 耗时 {ElapsedMs} ms",
                                    dalamudReady, (DateTimeOffset.UtcNow - attemptStarted).TotalMilliseconds);
                    if (dalamudReady)
                        ReportLaunchStatus("已检测到游戏窗口，卫月界面已初始化。");
                    break;
                }

                await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(1)));
            }

            if (!exited.IsCompleted)
            {
                if (!windowSeen)
                    ReportLaunchFailure("游戏进程仍在运行，暂未检测到窗口。退出游戏后可重新登录。");
                else if (dalamudLog != null && !dalamudReady)
                    ReportLaunchFailure("已检测到游戏窗口，暂未确认卫月初始化。");
            }

            await exited;
            try { exitCode = process.ExitCode; }
            catch (InvalidOperationException) { /* Wine may not be a direct child of the launcher. */ }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MainWindow] 无法跟踪游戏进程");
            ReportLaunchFailure("无法跟踪游戏进程，请检查游戏窗口。");
            return;
        }
        finally
        {
            process.Dispose();
            IsGameRunning = false;
        }

        var code = exitCode is { } value && value != 0 ? $"（退出码 {value}）" : string.Empty;
        if (!windowSeen || timer.Elapsed < TimeSpan.FromSeconds(20) || exitCode is not (null or 0))
        {
            ReportLaunchFailure($"游戏进程在启动后退出{code}，可重新登录或查看日志。");
            return;
        }

        if (dalamudLog != null && !dalamudReady)
        {
            ReportLaunchFailure("游戏已退出，本次未确认卫月初始化。");
            return;
        }

        ShowLaunchRecovery = false;
        LaunchMessage = null;
        StatusText = "游戏已退出";
        if (settings.ExitLauncherWhenGameExit
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    [RelayCommand]
    private void OpenRoamingFolder() => OpenWithSystem(Paths.RoamingPath);

    private void OpenWithSystem(string path, bool reveal = false)
    {
        var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        if (reveal)
            info.ArgumentList.Add("-R");

        info.ArgumentList.Add(path);
        try
        {
            using var process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warning(ex, "[MainWindow] 无法通过系统打开");
            StatusText = "无法打开，请查看日志";
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private async Task OpenSettingsAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return;

        var dialog = new SettingsWindow(settings, GamePathText, compatibilityTools?.Settings);
        if (!await dialog.ShowDialog<bool>(owner))
            return;

        updateCheckRevision++;
        IsInitializing = true;
        try
        {
            IsDalamudEnabled = settings.DalamudEnabled;
            await DetectGameAsync();
            NeedsGameUpdate = false;
            LaunchMessage = null;
            ShowLaunchRecovery = false;
            MaintenanceStatus = "尚未检查更新";
            StatusText = "设置已保存";
        }
        finally
        {
            IsInitializing = false;
        }
    }

    /// <summary>
    ///     下载并安装完整游戏本体（FreshInstall）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownloadGame))]
    private async Task DownloadGameAsync()
    {
        if (!CanDownloadGame())
            return;

        var gamePath = GamePathText;
        if (GameHelpers.IsValidGamePath(gamePath))
        {
            await DetectGameAsync();
            StatusText = "此目录已有游戏，请使用更新或修复";
            return;
        }

        updateCheckRevision++;

        // 磁盘空间预检：完整游戏约 80GB，不足时提前提示避免下载中断
        try
        {
            Directory.CreateDirectory(gamePath);
            var freeBytes = PlatformHelpers.GetDiskFreeSpace(new DirectoryInfo(gamePath));
            const long REQUIRED_BYTES = 80L * 1024 * 1024 * 1024; // 80GB
            if (freeBytes < REQUIRED_BYTES)
            {
                OnUiThread(() =>
                {
                    StatusText = "磁盘空间不足";
                    MaintenanceStatus = $"可用空间不足（{freeBytes / 1024d / 1024 / 1024:0.#} GB）。请在设置中选择空间充足的磁盘。";
                });
                return;
            }
        }
        catch (Exception ex)
        {
            // 磁盘预检失败不阻塞下载（可能是权限/只读卷），仅记录
            Log.Debug(ex, "[MainWindow] 磁盘空间预检失败，跳过");
        }

        OnUiThread(() =>
        {
            IsDownloading = true;
            DownloadPercent = 0;
            DownloadPhase = "正在初始化…";
            DownloadDetail = "准备下载游戏文件（首次安装约 50-80GB，请保持网络畅通）";
            StatusText = "正在下载游戏…";
        });

        var progress = new Progress<InstallProgress>(value =>
        {
            DownloadPercent = value.Percent;
            DownloadPhase = value.Phase;
            DownloadDetail = value.Detail;
        });

        try
        {
            if (await Task.Run(() => installService.FreshInstallAsync(gamePath, progress)))
            {
                settings.GamePath = new DirectoryInfo(gamePath);
                await DetectGameAsync();
                MaintenanceStatus = StatusText = "游戏下载完成";
            }
            else
            {
                MaintenanceStatus = "游戏下载未完成" + (string.IsNullOrWhiteSpace(DownloadDetail) ? "" : $" · {DownloadDetail}");
                StatusText = "游戏下载未完成";
            }
        }
        finally
        {
            // 恢复按钮默认文案
            OnUiThread(() =>
            {
                DownloadPhase = "下载并安装完整游戏";
                DownloadDetail = string.Empty;
                IsDownloading = false;
            });
        }
    }

    /// <summary>
    ///     手动检查并更新卫月。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdateDalamud))]
    private async Task UpdateDalamudAsync()
    {
        if (!CanUpdateDalamud() || dalamudService == null)
            return;

        updateCheckRevision++;
        IsGameMaintenanceBusy = true;
        OnUiThread(() =>
        {
            MaintenanceStatus = "正在检查卫月更新…";
            StatusText        = MaintenanceStatus;
        });

        try
        {
            dalamudService.RunUpdater(refreshVersionInfo: true);
            await dalamudService.Updater.WaitForCompletionAsync();
            if (dalamudService.Updater.State != DalamudUpdater.DownloadState.Done)
                throw new InvalidOperationException("卫月未通过更新校验", dalamudService.Updater.EnsurementException);

            OnUiThread(() =>
            {
                DalamudVersion    = XIVLauncher.Dalamud.DalamudUpdater.Version;
                MaintenanceStatus = $"卫月已就绪: {DalamudVersion}";
                StatusText        = MaintenanceStatus;
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainWindow] 更新卫月失败");
            OnUiThread(() =>
            {
                MaintenanceStatus = "卫月更新失败: " + ex.Message;
                StatusText        = MaintenanceStatus;
            });
        }
        finally
        {
            OnUiThread(() => IsGameMaintenanceBusy = false);
        }
    }

    /// <summary>
    ///     检查游戏更新（V3）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMaintainGame))]
    private Task CheckGameUpdateAsync() => CheckGameUpdateAsync(background: false);

    private async Task CheckGameUpdateAsync(bool background)
    {
        if (!CanMaintainGame())
            return;

        var revision = ++updateCheckRevision;
        if (!background)
        {
            IsGameMaintenanceBusy = true;
            StatusText = "正在检查游戏更新…";
        }

        try
        {
            var result = await maintenanceService.CheckAsync(new DirectoryInfo(GamePathText));
            if (revision == updateCheckRevision)
            {
                ReportGameUpdate(result);
                if (!background)
                    StatusText = MaintenanceStatus;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MainWindow] 检查游戏更新失败");
            if (revision == updateCheckRevision)
            {
                MaintenanceStatus = "检查更新失败，可重试";
                if (!background)
                    StatusText = MaintenanceStatus;
            }
        }
        finally
        {
            if (!background)
                IsGameMaintenanceBusy = false;
        }
    }

    private void ReportGameUpdate(GameUpdateCheckResult result)
    {
        NeedsGameUpdate = result.NeedsUpdate;
        MaintenanceStatus = result.UpdatePlan is { } plan
                            ? $"发现更新 · {plan.CurrentGameVersion} → {plan.TargetGameVersion}"
                            : "游戏已是最新版本";
    }

    /// <summary>
    ///     安装游戏更新（V3 补丁，使用内置 xdelta3 合并差分）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMaintainGame))]
    private async Task UpdateGameAsync()
    {
        if (!CanMaintainGame())
            return;

        var gamePath = GamePathText;
        if (!Directory.Exists(gamePath) || !GameHelpers.IsValidGamePath(gamePath))
        {
            OnUiThread(() => StatusText = "未找到有效游戏目录，请先设置游戏路径");
            return;
        }

        updateCheckRevision++;
        IsGameMaintenanceBusy = true;
        OnUiThread(() =>
        {
            IsDownloading   = true;
            DownloadPercent = 0;
            DownloadPhase   = "准备更新…";
            DownloadDetail  = string.Empty;
            StatusText      = "正在更新游戏…";
        });

        try
        {
            var plan = (await maintenanceService.CheckAsync(new DirectoryInfo(gamePath))).UpdatePlan;

            if (plan == null)
            {
                NeedsGameUpdate = false;
                OnUiThread(() =>
                {
                    MaintenanceStatus = "游戏已是最新版本";
                    StatusText        = MaintenanceStatus;
                });
                return;
            }

            var patchPath = settings.PatchPath ?? new DirectoryInfo(Path.Combine(Paths.RoamingPath, "patches"));

            var progress = new Progress<GamePatchProgress>(p => OnUiThread(() =>
            {
                DownloadPercent = p.Total > 0 ? Math.Min(100, (double)p.Progress / p.Total * 100) : 0;
                DownloadPhase   = p.PhaseText;
                DownloadDetail  = string.IsNullOrEmpty(p.CurrentFile) ? p.StatusText : $"{p.CurrentFile} {p.StatusText}";
            }));

            await maintenanceService.InstallUpdateAsync
            (
                plan,
                new DirectoryInfo(gamePath),
                patchPath,
                false,
                progress
            );

            OnUiThread(() =>
            {
                MaintenanceStatus = "更新完成";
                StatusText        = "游戏更新完成";
                NeedsGameUpdate    = false;
                LaunchMessage      = null;
            });

            await DetectGameAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainWindow] 安装游戏更新失败");
            OnUiThread(() =>
            {
                MaintenanceStatus = "更新失败: " + ex.Message;
                StatusText        = MaintenanceStatus;
            });
        }
        finally
        {
            OnUiThread(() =>
            {
                IsDownloading   = false;
                DownloadPhase   = "下载并安装完整游戏";
                DownloadDetail  = string.Empty;
                IsGameMaintenanceBusy = false;
            });
        }
    }

    /// <summary>
    ///     修复游戏文件：按官方清单校验并只补下损坏文件。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMaintainGame))]
    private async Task RepairGameAsync()
    {
        if (!CanMaintainGame())
            return;

        var gamePath = GamePathText;
        if (!Directory.Exists(gamePath) || !GameHelpers.IsValidGamePath(gamePath))
        {
            OnUiThread(() => StatusText = "未找到有效游戏目录，请先设置游戏路径");
            return;
        }

        updateCheckRevision++;
        IsGameMaintenanceBusy = true;
        OnUiThread(() =>
        {
            IsDownloading   = true;
            DownloadPercent = 0;
            DownloadPhase   = "正在校验游戏文件…";
            DownloadDetail  = string.Empty;
            StatusText      = "正在修复游戏文件…";
        });

        try
        {
            var progress = new Progress<RepairProgress>(p => OnUiThread(() =>
            {
                DownloadPercent = p.Total > 0 ? Math.Min(100, (double)p.Progress / p.Total * 100) : 0;
                DownloadPhase   = p.TaskCount > 0 ? $"修复进度 {p.TaskIndex}/{p.TaskCount}" : "正在修复…";
                DownloadDetail  = string.IsNullOrEmpty(p.CurrentFile) ? $"{p.Progress}/{p.Total}" : p.CurrentFile;
            }));

            var state = await maintenanceService.RepairAsync(gamePath, progress);

            OnUiThread(() =>
            {
                MaintenanceStatus = state switch
                {
                    GameRepairer.RepairState.Done      => "修复完成",
                    GameRepairer.RepairState.Cancelled => "修复已取消",
                    GameRepairer.RepairState.Error     => "修复失败，请查看日志",
                    _                                  => "修复结束"
                };
                StatusText = MaintenanceStatus;
            });

            await DetectGameAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainWindow] 修复游戏失败");
            OnUiThread(() =>
            {
                MaintenanceStatus = "修复失败: " + ex.Message;
                StatusText        = MaintenanceStatus;
            });
        }
        finally
        {
            OnUiThread(() =>
            {
                IsDownloading   = false;
                DownloadPhase   = "下载并安装完整游戏";
                DownloadDetail  = string.Empty;
                IsGameMaintenanceBusy = false;
            });
        }
    }

    /// <summary>
    ///     诊断登录网络（检测 DNS 劫持 / HTTP 567 拦截）。
    /// </summary>
    [RelayCommand]
    private async Task DiagnoseNetworkAsync()
    {
        OnUiThread(() =>
        {
            StatusText = "正在诊断网络…";
            NetworkDiagnosis = "正在检测登录服务器连通性…";
        });

        try
        {
            var report = await NetworkDiagnostics.DiagnoseAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(report.Summary);
            sb.AppendLine();
            foreach (var result in report.Results)
                sb.AppendLine(result.Detail);

            OnUiThread(() =>
            {
                NetworkDiagnosis = sb.ToString();
                StatusText = report.LoginBlocked ? "检测到网络劫持，请执行修复" : "网络正常，可尝试登录";
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainWindow] 网络诊断失败");
            OnUiThread(() =>
            {
                NetworkDiagnosis = "诊断失败: " + ex.Message;
                StatusText = "网络诊断失败";
            });
        }
    }

    /// <summary>
    ///     生成并打开 hosts 修复脚本（需 sudo 执行）。
    /// </summary>
    [RelayCommand]
    private async Task GenerateHostsFixAsync()
    {
        try
        {
            OnUiThread(() => StatusText = "正在生成 hosts 修复脚本…");
            var script = await NetworkDiagnostics.BuildHostsFixScriptAsync();

            var scriptPath = Path.Combine(Paths.RoamingPath, "fix-hosts.sh");
            Directory.CreateDirectory(Paths.RoamingPath);
            File.WriteAllText(scriptPath, script);
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            OpenWithSystem(scriptPath, reveal: true);

            OnUiThread(() =>
            {
                StatusText = "已生成修复脚本，请执行: sudo bash \"" + scriptPath + "\"";
                NetworkDiagnosis = "修复脚本已保存到:\n" + scriptPath + "\n\n请打开终端执行:\nsudo bash \"" + scriptPath + "\"\n执行后重启启动器并重新登录。";
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainWindow] 生成 hosts 修复脚本失败");
            OnUiThread(() => StatusText = "生成修复脚本失败: " + ex.Message);
        }
    }

    private sealed class GameVersionProvider : IDalamudGameVersionProvider
    {
        public string GetVersion(DirectoryInfo gamePath, bool isBck = false)
        {
            try
            {
                return Repository.Ffxiv.GetVer(gamePath, isBck);
            }
            catch
            {
                return "unknown";
            }
        }
    }

    private sealed class EmptyTroubleshootingProvider : IDalamudTroubleshootingProvider
    {
        public string GetTroubleshootingJson(DirectoryInfo? gamePath) => "{}";
    }

    /// <summary>
    ///     macOS 下的设置存储适配（将 LauncherSettingsV3 的 CurrentAccountID 桥接到账户管理器）。
    /// </summary>
    private sealed class SettingsStoreAdapter(LauncherSettingsV3 settings) : IAccountSettingsStore
    {
        public string CurrentAccountID
        {
            get => settings.CurrentAccountID;
            set
            {
                settings.CurrentAccountID = value;
                settings.Save();
            }
        }
    }

    #region UI 线程调度

    /// <summary>
    ///     线程安全地设置状态栏文本（供登录工作流等后台线程调用）。
    /// </summary>
    public void SetStatusText(string message) =>
        OnUiThread(() => StatusText = message);

    public void ShowLoginQr(Avalonia.Media.Imaging.Bitmap bitmap, CancellationToken cancellationToken) =>
        OnUiThread(() =>
        {
            if (cancellationToken.IsCancellationRequested || !IsLoggingIn)
            {
                bitmap.Dispose();
                return;
            }

            var previous = QrCodeImage;
            QrCodeImage = bitmap;
            IsQrCodeVisible = true;
            previous?.Dispose();
            StatusText = "请使用游戏客户端扫码";
        });

    private void ClearLoginQr()
    {
        IsQrCodeVisible = false;
        var previous = QrCodeImage;
        QrCodeImage = null;
        previous?.Dispose();
    }

    /// <summary>
    ///     在 UI 线程上执行操作。若调用方已在 UI 线程则直接执行，
    ///     否则通过 Avalonia Dispatcher 调度（避免后台线程直接操作
    ///     ObservableCollection / 绑定属性导致的线程亲和性异常）。
    ///     <para>调度后的 action 异常被捕获记录，避免成为 UI 线程未处理异常。</para>
    /// </summary>
    private static void OnUiThread(Action action)
    {
        void SafeAction()
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] UI 线程回调异常");
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            SafeAction();
            return;
        }

        Dispatcher.UIThread.Post(SafeAction);
    }

    #endregion
}
