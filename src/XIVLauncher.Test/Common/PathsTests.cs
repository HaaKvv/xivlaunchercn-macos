using XIVLauncher.Common.Constant;
using Xunit;

namespace XIVLauncher.Test.Common;

/// <summary>
///     路径逻辑（macOS 移植后行为验证）
/// </summary>
public sealed class PathsTests
{
    [Fact]
    public void RoamingPath_IsUnderApplicationData()
    {
        var expected = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncherCN");

        Assert.Equal(expected, Paths.RoamingPath);
    }

    [Fact]
    public void OverrideRoamingPath_ExpandsEnvironmentVariables()
    {
        var original = Paths.RoamingPath;

        try
        {
            // macOS 上 $HOME 环境变量展开
            var expanded = "$HOME/xltest-roaming";
            Paths.OverrideRoamingPath(expanded);

            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "xltest-roaming"), Paths.RoamingPath);
        }
        finally
        {
            Paths.OverrideRoamingPath(original);
        }
    }

    [Fact]
    public void GetConfigPath_IsInsideRoamingPath()
    {
        var original = Paths.RoamingPath;

        try
        {
            Paths.OverrideRoamingPath("/tmp/xl-test");
            Assert.Equal(Path.Combine("/tmp/xl-test", "launcherConfigV3.json"), Paths.GetConfigPath());
        }
        finally
        {
            Paths.OverrideRoamingPath(original);
        }
    }

    [Fact]
    public void ResolvePatchPath_DefaultsToRoamingPatches()
    {
        var result = Paths.ResolvePatchPath(null, "/tmp/xl-test");

        Assert.Equal(Path.Combine("/tmp/xl-test", "patches"), result.FullName);
    }

    [Fact]
    public void ResolvePatchPath_UsesConfiguredPath_WhenExists()
    {
        using var temp = new RepositoryTests.TempDir();

        var configured = new DirectoryInfo(Path.Combine(temp.Dir.FullName, "my-patches"));
        configured.Create();

        var result = Paths.ResolvePatchPath(configured, "/tmp/xl-test");
        Assert.Equal(configured.FullName, result.FullName);
    }

    [Fact]
    public void ResolvePatchPath_FallsBack_WhenConfiguredMissing()
    {
        using var temp = new RepositoryTests.TempDir();

        var configured = new DirectoryInfo(Path.Combine(temp.Dir.FullName, "does-not-exist"));
        var result = Paths.ResolvePatchPath(configured, "/tmp/xl-test");

        Assert.Equal(Path.Combine("/tmp/xl-test", "patches"), result.FullName);
    }

    [Fact]
    public void GetGamePath_ReturnsString_EvenWhenGameNotFound()
    {
        // macOS 无注册表，找不到游戏时应返回默认路径而非抛异常
        var path = Paths.GetGamePath();

        Assert.False(string.IsNullOrWhiteSpace(path));
    }
}
