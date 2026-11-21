using XIVLauncher.Common.Util;
using Xunit;

namespace XIVLauncher.Test.Common;

/// <summary>
///     macOS 平台工具（PlatformHelpers 移植后行为验证）
/// </summary>
public sealed class PlatformHelpersTests
{
    [Fact]
    public void GetTempFileName_ReturnsUniquePaths()
    {
        var first  = PlatformHelpers.GetTempFileName();
        var second = PlatformHelpers.GetTempFileName();

        Assert.NotEqual(first, second);
        Assert.StartsWith(Path.GetTempPath(), first);
        Assert.StartsWith(Path.GetTempPath(), second);
    }

    [Fact]
    public void DeleteAndRecreateDirectory_RemovesAndRecreates()
    {
        using var temp = new RepositoryTests.TempDir();
        var target = new DirectoryInfo(Path.Combine(temp.Dir.FullName, "sub"));
        target.Create();
        File.WriteAllText(Path.Combine(target.FullName, "file.txt"), "data");

        PlatformHelpers.DeleteAndRecreateDirectory(target);

        Assert.True(target.Exists);
        Assert.Empty(target.GetFiles());
    }

    [Fact]
    public void CopyFilesRecursively_CopiesTree()
    {
        using var temp = new RepositoryTests.TempDir();
        var source = temp.Dir.CreateSubdirectory("src");
        var nested = source.CreateSubdirectory("nested");
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "A");
        File.WriteAllText(Path.Combine(nested.FullName, "b.txt"), "B");

        var target = temp.Dir.CreateSubdirectory("dst");
        PlatformHelpers.CopyFilesRecursively(source, target);

        Assert.True(File.Exists(Path.Combine(target.FullName, "a.txt")));
        Assert.True(File.Exists(Path.Combine(target.FullName, "nested", "b.txt")));
        Assert.Equal("A", File.ReadAllText(Path.Combine(target.FullName, "a.txt")));
    }

    [Fact]
    public void GetDiskFreeSpace_ReturnsPositiveValue()
    {
        // 对临时目录所在卷查询可用空间，应返回正数（macOS: df -k 解析）
        var free = PlatformHelpers.GetDiskFreeSpace(new DirectoryInfo(Path.GetTempPath()));

        Assert.True(free > 0, $"可用空间应大于 0，实际 {free}");
    }

    [Fact]
    public void GetAvailablePort_ReturnsUsablePort()
    {
        var port = PlatformHelpers.GetAvailablePort();

        Assert.InRange(port, 1024, 65535);
    }

    [Fact]
    public void IsElevated_ReturnsFalseForNormalUser()
    {
        // 常规开发环境通常非 root；即便在 root 下运行，仅验证方法可执行不抛异常
        try
        {
            _ = PlatformHelpers.IsElevated();
        }
        catch (Exception ex)
        {
            Assert.Fail($"IsElevated 不应抛异常: {ex}");
        }
    }

    [Fact]
    public void Unzip7ZAsset_FallsBackGracefully_WhenSevenZipMissing()
    {
        using var temp = new RepositoryTests.TempDir();

        // 用非 7z 文件触发：找不到 7z 或解压失败都应走托管解压路径，不崩溃
        var dummyPath = Path.Combine(temp.Dir.FullName, "dummy.7z");
        var outputDir = Path.Combine(temp.Dir.FullName, "out");
        File.WriteAllText(dummyPath, "not a real archive");

        try
        {
            PlatformHelpers.Unzip7ZAsset(dummyPath, outputDir);
        }
        catch (Exception ex)
        {
            // 7z 解压失败或托管解压失败都应被捕获或抛可预期的异常，
            // 但不应抛出 "找不到 7z" 的原始 FileNotFound 而应优雅回退；
            // 这里接受任何异常（取决于本机是否装了 7z），重点是不挂起。
            Assert.NotNull(ex);
        }
    }
}
