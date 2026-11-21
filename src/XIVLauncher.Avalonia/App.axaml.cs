using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Serilog;
using XIVLauncher.Common.Constant;
using XIVLauncher.Views;

namespace XIVLauncher;

public partial class App : Application
{
    public override void Initialize()
    {
        ConfigureLogging();
        ConfigureGlobalExceptionHandling();
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     配置 Serilog 文件日志（数据目录 logs/launcher.log），便于排查登录/下载问题。
    ///     文件写入经 Async 包装，避免在 UI 线程同步落盘拖慢界面。
    /// </summary>
    private static void ConfigureLogging()
    {
        try
        {
            var logDir = Path.Combine(Paths.RoamingPath, "logs");
            Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                         .MinimumLevel.Debug()
                         .WriteTo.Console()
                         .WriteTo.Async
                         (a => a.File
                         (
                             Path.Combine(logDir, "launcher-.log"),
                             rollingInterval: RollingInterval.Day,
                             retainedFileCountLimit: 14,
                             outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                         ))
                         .CreateLogger();

            Log.Information("[App] 日志已初始化: {LogDir}", logDir);
        }
        catch (Exception ex)
        {
            // 日志初始化失败不应阻止应用启动
            System.Diagnostics.Debug.WriteLine($"日志初始化失败: {ex}");
        }
    }

    /// <summary>
    ///     注册全局未处理异常钩子：后台线程/任务异常不再静默丢失，
    ///     全部落日志，便于排查登录/下载等异步链路问题。
    /// </summary>
    private static void ConfigureGlobalExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "[App] 未处理异常 (AppDomain)");
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "[App] 未观察任务异常");
            e.SetObserved(); // 标记已观察，避免进程级处理
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.Exit += (_, _) =>
            {
                Log.Information("[App] 应用退出");
                Log.CloseAndFlush();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
