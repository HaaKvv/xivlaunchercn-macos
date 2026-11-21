using XIVLauncher.GamePatchV3;
using XIVLauncher.GamePatchV3.Models;
using Xunit;

namespace XIVLauncher.Test.GamePatchV3;

/// <summary>
///     第三轮修复回归测试：完整性清单行解析（防含空格路径错乱）。
/// </summary>
public sealed class MetadataParsingFixTests
{
    [Fact]
    public void ParseIntegrityCheck_ParsesLines_WithPipeDelimiters()
    {
        // 行解析必须按 | 分隔字段而非任意空白：
        // 构造路径段含空格的响应验证（旧实现 Split() 会把它错误拆开）
        var response = "https://cdn.example.com/apppc/100|100001900|0.0.0.25\n" +
                       "game\\my dir\\file.dat|12345|ABCDEF1234567890ABCDEF1234567890\n" +
                       "game\\ffxiv_dx11.exe|51774720|04F0E75C4E67ACA6086E0DFA637F3BCC\n";

        var remoteVersion = new RemoteVersion
        {
            BaseUrl = "https://cdn.example.com/diff",
            Areas = [new GameVersionArea { Id = "0", Max = "0.0.0.25" }]
        };

        var result = GamePatchMetadataClient.ParseIntegrityCheckResponse(remoteVersion, response);

        Assert.Equal("https://cdn.example.com/apppc/100", result.BaseUrl);
        Assert.Equal("0.0.0.25", result.DataVersion);
        Assert.Equal(2, result.Hashes.Count);
        // 含空格的路径必须作为整体键保留
        Assert.Contains(result.Hashes, h => h.Key.Contains("my dir", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseIntegrityCheck_EmptyResponse_Throws()
    {
        var remoteVersion = new RemoteVersion
        {
            Areas = [new GameVersionArea { Id = "0", Max = "0.0.0.25" }]
        };

        Assert.Throws<InvalidDataException>(() =>
            GamePatchMetadataClient.ParseIntegrityCheckResponse(remoteVersion, ""));
    }

    [Fact]
    public void ParseIntegrityCheck_InvalidHeader_Throws()
    {
        var remoteVersion = new RemoteVersion
        {
            Areas = [new GameVersionArea { Id = "0", Max = "0.0.0.25" }]
        };

        // 头部只有 1 个字段（不足 3），应抛格式无效
        Assert.Throws<InvalidDataException>(() =>
            GamePatchMetadataClient.ParseIntegrityCheckResponse(remoteVersion, "only-header\n"));
    }
}
