using XIVLauncher.Common;
using Xunit;

namespace XIVLauncher.Test.Common;

/// <summary>
///     游戏版本文件（.ver）读取逻辑
/// </summary>
public sealed class RepositoryTests
{
    [Fact]
    public void GetVerFile_ReturnsCorrectPathForEachRepo()
    {
        using var tempDir = new TempDir();

        var boot = Repository.Boot.GetVerFile(tempDir.Dir);
        Assert.Equal(Path.Combine(tempDir.Dir.FullName, "boot", "ffxivboot.ver"), boot.FullName);

        var ffxiv = Repository.Ffxiv.GetVerFile(tempDir.Dir);
        Assert.Equal(Path.Combine(tempDir.Dir.FullName, "game", "ffxivgame.ver"), ffxiv.FullName);

        var ex1 = Repository.Ex1.GetVerFile(tempDir.Dir);
        Assert.Equal(Path.Combine(tempDir.Dir.FullName, "game", "sqpack", "ex1", "ex1.ver"), ex1.FullName);
    }

    [Fact]
    public void GetVer_ReadsContentFromFile()
    {
        using var tempDir = new TempDir();
        var gameDir = tempDir.Dir.CreateSubdirectory("game");
        var verFile = Path.Combine(gameDir.FullName, "ffxivgame.ver");
        File.WriteAllText(verFile, "2024.08.20.0000.0000");

        var version = Repository.Ffxiv.GetVer(tempDir.Dir);
        Assert.Equal("2024.08.20.0000.0000", version);
    }

    [Fact]
    public void GetVer_FileNotFound_ReturnsBaseVersion()
    {
        using var tempDir = new TempDir();

        // 原版语义：ver 文件缺失时返回基准版本（2012.01.01.0000.0000），而非抛异常
        var version = Repository.Ffxiv.GetVer(tempDir.Dir);
        Assert.Equal("2012.01.01.0000.0000", version);
    }

    [Fact]
    public void GetVer_BckFile_IsUsedWhenRequested()
    {
        using var tempDir = new TempDir();
        var gameDir = tempDir.Dir.CreateSubdirectory("game");
        File.WriteAllText(Path.Combine(gameDir.FullName, "ffxivgame.ver"), "2024.08.20.0000.0000");
        File.WriteAllText(Path.Combine(gameDir.FullName, "ffxivgame.bck"), "2024.01.01.0000.0000");

        Assert.Equal("2024.08.20.0000.0000", Repository.Ffxiv.GetVer(tempDir.Dir, isBck: false));
        Assert.Equal("2024.01.01.0000.0000", Repository.Ffxiv.GetVer(tempDir.Dir, isBck: true));
    }

    [Fact]
    public void IsBaseVer_DetectsBaseVersion()
    {
        using var tempDir = new TempDir();
        var gameDir = tempDir.Dir.CreateSubdirectory("game");
        File.WriteAllText(Path.Combine(gameDir.FullName, "ffxivgame.ver"), "2012.01.01.0000.0000");

        Assert.True(Repository.Ffxiv.IsBaseVer(tempDir.Dir));

        File.WriteAllText(Path.Combine(gameDir.FullName, "ffxivgame.ver"), "2024.08.20.0000.0000");
        Assert.False(Repository.Ffxiv.IsBaseVer(tempDir.Dir));
    }

    /// <summary>测试用临时目录（自动清理）</summary>
    internal sealed class TempDir : IDisposable
    {
        public DirectoryInfo Dir { get; } = new(Path.Combine(Path.GetTempPath(), "xltest_" + Guid.NewGuid().ToString("N")));

        public TempDir() => Dir.Create();

        public void Dispose()
        {
            try
            {
                if (Dir.Exists)
                    Dir.Delete(true);
            }
            catch
            {
                // ignored
            }
        }
    }
}
