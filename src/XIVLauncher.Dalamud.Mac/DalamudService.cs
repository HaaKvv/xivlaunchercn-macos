namespace XIVLauncher.Dalamud;

public sealed class DalamudService : IDalamudService
{
    private readonly DalamudHostPaths                hostPaths;
    private readonly IDalamudGameVersionProvider     gameVersionProvider;
    private readonly IDalamudTroubleshootingProvider troubleshootingProvider;
    private readonly IDalamudCompatibilityCheck      compatibilityCheck;
    private readonly IDalamudRunner                  injector;

    public DalamudUpdater Updater { get; }

    public event Action<DalamudStatusSnapshot>? StatusChanged;

    public DalamudService
    (
        DalamudHostPaths                hostPaths,
        IDalamudProgressSink?           progressSink,
        IDalamudGameVersionProvider     gameVersionProvider,
        IDalamudTroubleshootingProvider troubleshootingProvider,
        IDalamudRunner                  injector,
        IDalamudCompatibilityCheck?     compatibilityCheck = null
    )
    {
        this.hostPaths               = hostPaths;
        this.gameVersionProvider     = gameVersionProvider;
        this.troubleshootingProvider = troubleshootingProvider;
        this.injector                = injector;
        this.compatibilityCheck      = compatibilityCheck ?? new DalamudCompatibilityCheck();

        Updater = new DalamudUpdater(hostPaths.AddonDirectory, hostPaths.RuntimeDirectory, hostPaths.AssetDirectory)
        {
            ProgressSink = progressSink
        };
        Updater.StatusChanged += HandleUpdaterStatusChanged;
    }

    public void RunUpdater(bool refreshVersionInfo = false) =>
        Updater.Run(refreshVersionInfo);

    public void EnsureCompatibility() =>
        compatibilityCheck.EnsureCompatibility();

    public DalamudSession CreateLauncher(DirectoryInfo gamePath, DalamudLaunchOptions options) =>
        new
        (
            injector,
            Updater,
            options.LoadMethod,
            gamePath,
            hostPaths.ConfigDirectory,
            hostPaths.LogDirectory,
            options.DelayInitializeMs,
            options.FakeLogin,
            options.NoPlugins,
            options.NoThirdPlugins,
            troubleshootingProvider.GetTroubleshootingJson(gamePath),
            gameVersionProvider
        );

    public DalamudStatusSnapshot GetStatusSnapshot() =>
        new
        (
            Updater.State,
            Updater.LoadingDetail,
            Updater.LoadingTotal,
            Updater.LoadingDownloaded,
            Updater.LoadingProgress,
            DalamudUpdater.Version,
            DalamudUpdater.OnlineHash
        );

    private void HandleUpdaterStatusChanged(DalamudUpdater updater) =>
        StatusChanged?.Invoke(GetStatusSnapshot());
}
