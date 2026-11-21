using System.Diagnostics;
using XIVLauncher.Common;
using XIVLauncher.Common.Game;
using Xunit;

namespace XIVLauncher.Test.Common;

/// <summary>
///     游戏启动参数构建（国服定制参数）
/// </summary>
public sealed class LauncherTests
{
    [Fact]
    public void LaunchGame_BuildsCorrectArguments_ForSdo()
    {
        var launcher = new Launcher();
        var runner = new FakeRunner();
        using var temp = new RepositoryTests.TempDir();
        var gamePath = temp.Dir;
        var gameDir = Directory.CreateDirectory(Path.Combine(gamePath.FullName, "game"));
        File.WriteAllText(Path.Combine(gameDir.FullName, "ffxiv_dx11.exe"), "");

        var process = launcher.LaunchGame(
            runner,
            "session-123",
            "snda-456",
            0,
            "1",
            "lobby.example.com",
            "gm.example.com",
            "db.example.com",
            "areas-info",
            "",
            gamePath,
            false,
            DPIAwareness.Aware);

        // FakeRunner 返回 null（未真正启动进程），Launcher 会返回 null；
        // 断言重点是 runner 接收到的启动参数
        Assert.NotNull(runner.LastExePath);
        Assert.Equal(Path.Combine(gamePath.FullName, "game", "ffxiv_dx11.exe"), runner.LastExePath);
        Assert.Contains("-AppID", runner.LastArguments);
        Assert.Contains("Dev.LobbyHost01", runner.LastArguments);
        Assert.Contains("DEV.TestSID", runner.LastArguments);
        Assert.Contains("session-123", runner.LastArguments);
        Assert.Contains("XL.SndaId", runner.LastArguments);
        Assert.Contains("snda-456", runner.LastArguments);
    }

    [Fact]
    public void LaunchGame_Throws_WhenExeMissing()
    {
        var launcher = new Launcher();
        var runner = new FakeRunner();
        using var temp = new RepositoryTests.TempDir();
        var gamePath = temp.Dir; // 无 game 子目录

        Assert.ThrowsAny<Exception>(() => launcher.LaunchGame(
            runner, "s", "n", 0, "1", "l", "g", "d", "a", "", gamePath, false, DPIAwareness.Aware));
    }

    [Fact]
    public void LaunchGame_AppendsAdditionalArguments()
    {
        var launcher = new Launcher();
        var runner = new FakeRunner();
        using var temp = new RepositoryTests.TempDir();
        var gamePath = temp.Dir;
        var gameDir = Directory.CreateDirectory(Path.Combine(gamePath.FullName, "game"));
        File.WriteAllText(Path.Combine(gameDir.FullName, "ffxiv_dx11.exe"), "");

        launcher.LaunchGame(
            runner, "s", "n", 0, "1", "l", "g", "d", "a", "CustomKey=customValue", gamePath, false, DPIAwareness.Aware);

        Assert.Contains("CustomKey", runner.LastArguments);
        Assert.Contains("customValue", runner.LastArguments);
    }

    /// <summary>记录启动参数的假 runner</summary>
    private sealed class FakeRunner : IGameRunner
    {
        public string? LastExePath { get; private set; }
        public string? LastArguments { get; private set; }

        public Process? Start(string path, string workingDirectory, string arguments, IDictionary<string, string> environment, DPIAwareness dpiAwareness)
        {
            LastExePath = path;
            LastArguments = arguments;
            return null;
        }
    }
}
