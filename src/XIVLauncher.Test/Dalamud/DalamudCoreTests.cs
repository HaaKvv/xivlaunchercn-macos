using XIVLauncher.Dalamud;
using Xunit;

namespace XIVLauncher.Test.Dalamud;

/// <summary>
///     Dalamud 核心类型与参数构建验证（纯逻辑，无 Wine 依赖）
/// </summary>
public sealed class DalamudCoreTests
{
    [Fact]
    public void InjectorArgs_Mode_Formats()
    {
        Assert.Equal("--mode=inject", DalamudInjectorArgs.Mode("inject"));
        Assert.Equal("--mode=entrypoint", DalamudInjectorArgs.Mode("entrypoint"));
    }

    [Fact]
    public void InjectorArgs_Game_PreservesQuotedPath()
    {
        Assert.Equal("--game=\"C:\\game\\ffxiv_dx11.exe\"", DalamudInjectorArgs.Game("C:\\game\\ffxiv_dx11.exe"));
    }

    [Fact]
    public void InjectorArgs_Constants_AreStable()
    {
        Assert.Equal("launch", DalamudInjectorArgs.LAUNCH);
        Assert.Equal("--without-dalamud", DalamudInjectorArgs.WITHOUT_DALAMUD);
        Assert.Equal("--fake-arguments", DalamudInjectorArgs.FAKE_ARGUMENTS);
        Assert.Equal("--no-plugin", DalamudInjectorArgs.NO_PLUGIN);
        Assert.Equal("--no-3rd-plugin", DalamudInjectorArgs.NO_THIRD_PARTY);
    }

    [Fact]
    public void LoadMethod_Enum_IsDefined()
    {
        Assert.True(Enum.IsDefined(typeof(DalamudLoadMethod), DalamudLoadMethod.EntryPoint));
        Assert.True(Enum.IsDefined(typeof(DalamudLoadMethod), DalamudLoadMethod.DllInject));
        Assert.True(Enum.IsDefined(typeof(DalamudLoadMethod), DalamudLoadMethod.ACLonly));
    }

    [Fact]
    public void ConsoleOutput_Deserializes_FromJson()
    {
        const string JSON = """{"pid":1234,"handle":5678}""";

        var output = Newtonsoft.Json.JsonConvert.DeserializeObject<DalamudConsoleOutput>(JSON);

        Assert.NotNull(output);
        Assert.Equal(1234, output!.Pid);
        Assert.Equal(5678, output.Handle);
    }

    [Fact]
    public void HostPaths_Constructs()
    {
        var paths = new DalamudHostPaths(
            new DirectoryInfo("/tmp/addon"),
            new DirectoryInfo("/tmp/runtime"),
            new DirectoryInfo("/tmp/assets"),
            new DirectoryInfo("/tmp/config"),
            new DirectoryInfo("/tmp/logs"),
            new DirectoryInfo("/tmp/launcher"));

        Assert.Equal("/tmp/addon", paths.AddonDirectory.FullName);
        Assert.Equal("/tmp/logs", paths.LogDirectory.FullName);
    }

    [Fact]
    public void LaunchOptions_ClampsDelay()
    {
        var options = new DalamudLaunchOptions(DalamudLoadMethod.EntryPoint, 999999, false, false, false);

        Assert.Equal(DalamudLaunchOptions.MAX_DELAY_INITIALIZE_MS, options.DelayInitializeMs);
    }

    [Fact]
    public void LaunchOptions_NegativeDelay_ClampsToZero()
    {
        var options = new DalamudLaunchOptions(DalamudLoadMethod.EntryPoint, -5, false, false, false);

        Assert.Equal(0, options.DelayInitializeMs);
    }

    [Fact]
    public void StartInfo_Defaults_AreSet()
    {
        var info = new DalamudStartInfo();

        Assert.Equal(string.Empty, info.GameVersion);
        Assert.NotNull(info.LauncherDirectory);
        Assert.NotEqual(string.Empty, info.LauncherDirectory);
    }

    [Fact]
    public void CompatibilityCheck_Arm64IsSupported()
    {
        // macOS 上 Arm64 不应被拒绝（Rosetta 2 提供 x64 仿真）
        var check = new DalamudCompatibilityCheck();

        // 在 arm64 机器上 EnsureCompatibility 不应抛出架构异常；
        // 若本机是 x64 也同样通过。仅验证"不抛架构不支持"异常。
        try
        {
            check.EnsureCompatibility();
        }
        catch (IDalamudCompatibilityCheck.ArchitectureNotSupportedException ex)
        {
            Assert.Fail($"macOS 上不应出现架构不支持: {ex.Message}");
        }
    }
}
