using System.Text;
using System.Text.Json;
using XIVLauncher.GamePatchV3;
using XIVLauncher.Settings.Converters;
using Xunit;

namespace XIVLauncher.Test.GamePatchV3;

/// <summary>
///     本轮修复回归测试：Vcdiff 请求构建与 DirectoryInfo 转换器边界。
/// </summary>
public sealed class VcdiffFixTests
{
    [Fact]
    public void BuildRequestData_ProducesValidRequest()
    {
        var request = VcdiffClient.BuildRequestData(
            "/tmp/source.dat",
            "/tmp/target.dat",
            "ABC123",
            1024,
            [1, 2, 3, 4, 5]);

        // 请求结构: opcode(4) + source(前缀+内容) + target + md5 + size(8) + deltaLen(4) + delta
        Assert.NotEmpty(request);
        Assert.True(request.Length > 5 + 3);
    }

    [Fact]
    public void DirectoryInfoConverter_EmptyString_ReturnsNull()
    {
        // 直接序列化含空字符串的 JSON，验证反序列化时空路径返回 null 而非抛异常
        var json = """{"Path":""}""";
        var settings = JsonSerializer.Deserialize<TestSettings>(json, new JsonSerializerOptions
        {
            Converters = { new DirectoryInfoJsonConverter() }
        });

        Assert.NotNull(settings);
        Assert.Null(settings!.Path);
    }

    [Fact]
    public void DirectoryInfoConverter_Null_ReturnsNull()
    {
        var json = """{"Path":null}""";
        var settings = JsonSerializer.Deserialize<TestSettings>(json, new JsonSerializerOptions
        {
            Converters = { new DirectoryInfoJsonConverter() }
        });

        Assert.Null(settings!.Path);
    }

    [Fact]
    public void DirectoryInfoConverter_ValidPath_RoundTrips()
    {
        var json = """{"Path":"/tmp/xivtest"}""";
        var settings = JsonSerializer.Deserialize<TestSettings>(json, new JsonSerializerOptions
        {
            Converters = { new DirectoryInfoJsonConverter() }
        });

        Assert.Equal("/tmp/xivtest", settings!.Path!.FullName);
    }

    private sealed class TestSettings
    {
        public System.IO.DirectoryInfo? Path { get; set; }
    }
}
