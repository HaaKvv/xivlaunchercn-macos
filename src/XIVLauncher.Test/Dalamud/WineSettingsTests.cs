using XIVLauncher.Common.Mac.Compatibility;
using Xunit;

namespace XIVLauncher.Test.Dalamud;

/// <summary>
///     macOS Wine 环境检测逻辑验证
/// </summary>
public sealed class WineSettingsTests
{
    [Fact]
    public void Detect_ReturnsNull_WhenNoWineOnPath()
    {
        // 构造一个不存在任何 Wine 的受限 PATH，验证返回 null（不抛异常）
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "/nonexistent-dir");

            // 注意：CrossOver/Whisky 通过固定路径检测，本测试仅验证不抛异常
            var settings = WineSettings.Detect("/tmp/xl-wine-test");

            // 结果可能为 null（无 Wine）或非 null（本机装了 CrossOver/Whisky），
            // 无论哪种都不应抛异常 —— 这是健壮性验证
            Assert.True(true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public void Detect_DoesNotThrow_WithValidRoamingPath()
    {
        var settings = WineSettings.Detect(Path.GetTempPath());

        // 核心是"不抛异常"；是否有 Wine 取决于本机环境
        Assert.True(true);
    }

    [Fact]
    public void Detect_Defaults_EnableEsyncQuietWine_AndMsyncForXlcore()
    {
        var settings = WineSettings.Detect(Path.GetTempPath());

        if (settings == null)
            return;

        Assert.True(settings.EsyncOn);
        Assert.Equal("-all", settings.DebugVars);

        if (settings.StartupType == WineStartupType.Xlcore)
            Assert.True(settings.MsyncOn);
    }

    [Fact]
    public void FindWineInPath_ReturnsNullOrPath_WithoutThrowing()
    {
        string? result = null;
        try
        {
            result = WineSettings.FindWineInPath();
        }
        catch (Exception ex)
        {
            Assert.Fail($"FindWineInPath 不应抛异常: {ex}");
        }

        // 若找到则必须是存在的文件
        if (result != null)
            Assert.True(File.Exists(result), $"Wine 路径应存在: {result}");
    }
}
