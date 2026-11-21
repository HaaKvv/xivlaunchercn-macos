using XIVLauncher.Settings;
using Xunit;

namespace XIVLauncher.Test.Account;

/// <summary>
///     性能优化回归测试：LauncherSettingsV3 防抖写盘与紧凑 JSON 序列化。
/// </summary>
public sealed class LauncherSettingsV3DebounceTests
{
    [Fact]
    public void Save_ImmediatelyFlushes_WhenDebounced()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"xivlauncher-test-{Guid.NewGuid():N}.json");
        try
        {
            var settings = LauncherSettingsV3.Load(tempPath);

            // 设置一个字段（触发防抖调度）
            settings.CurrentAccountID = "test-account-123";

            // 显式 Save() 应立即同步落盘（flush 语义不变）
            settings.Save();

            Assert.True(File.Exists(tempPath), "显式 Save 应立即写盘");

            // 重新加载验证值已持久化
            var reloaded = LauncherSettingsV3.Load(tempPath);
            Assert.Equal("test-account-123", reloaded.CurrentAccountID);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }

    [Fact]
    public void Debounce_EventuallyPersists_WithoutExplicitSave()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"xivlauncher-test-{Guid.NewGuid():N}.json");
        try
        {
            var settings = LauncherSettingsV3.Load(tempPath);

            // 连续设置多个字段（防抖合并）
            settings.CurrentAccountID = "account-a";
            settings.CurrentAccountID = "account-b";
            settings.CurrentAccountID = "account-c";

            // 等待防抖定时器触发；CI 上线程池可能延迟，用轮询代替固定 sleep
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(tempPath) && DateTime.UtcNow < deadline)
                Thread.Sleep(50);

            Assert.True(File.Exists(tempPath), "防抖定时器应在无显式 Save 时自动落盘");

            var reloaded = LauncherSettingsV3.Load(tempPath);
            Assert.Equal("account-c", reloaded.CurrentAccountID);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }

    [Fact]
    public void CompactJson_StillRoundTrips()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"xivlauncher-test-{Guid.NewGuid():N}.json");
        try
        {
            var settings = LauncherSettingsV3.Load(tempPath);
            settings.CurrentAccountID = "roundtrip-check";
            settings.Save();

            // 紧凑 JSON（WriteIndented=false）应仍可正确反序列化
            var json = File.ReadAllText(tempPath);
            Assert.DoesNotContain("\n  ", json); // 无缩进

            var reloaded = LauncherSettingsV3.Load(tempPath);
            Assert.Equal("roundtrip-check", reloaded.CurrentAccountID);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }
}
