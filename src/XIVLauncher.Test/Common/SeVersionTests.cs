using XIVLauncher.Common;
using Xunit;

namespace XIVLauncher.Test.Common;

/// <summary>
///     版本号解析与比较（国服版本号格式，如 2024.08.20.0000.0000）
/// </summary>
public sealed class SeVersionTests
{
    [Theory]
    [InlineData("2024.08.20.0000.0000", "2024.08.20.0000.0000", 0)]
    [InlineData("2024.08.21.0000.0000", "2024.08.20.0000.0000", 1)]
    [InlineData("2024.08.20.0000.0000", "2024.08.21.0000.0000", -1)]
    [InlineData("2025.01.01.1234.5678", "2024.12.31.9999.9999", 1)]
    public void Compare_OrdersNumerically(string left, string right, int expectedSign)
    {
        var l = SeVersion.Parse(left);
        var r = SeVersion.Parse(right);

        Assert.Equal(expectedSign, Math.Sign(l.CompareTo(r)));
    }

    [Theory]
    [InlineData("2024.01.02.0003.0004", "2024.01.02.0003.0004")]
    [InlineData("2023.11.30.0001.0002", "2023.11.30.0001.0002")]
    public void Parse_And_ToString_RoundTrips(string input, string expected)
    {
        var version = SeVersion.Parse(input);
        Assert.Equal(expected, version.ToString());
    }

    [Fact]
    public void ToString_PadsFields_ToCanonicalLength()
    {
        // 注意: 原版 ToString 使用固定格式 {0:0000}.{1:00}.{2:00}.{3:0000}.{4:0000}，
        // 全零输入会输出 "0000.00.00.0000.0000" 而非 "0.0.0.0.0" —— 这是原版语义
        var version = SeVersion.Parse("0.0.0.0.0");
        Assert.Equal("0000.00.00.0000.0000", version.ToString());
    }

    [Fact]
    public void Parse_InvalidInput_Throws()
    {
        Assert.ThrowsAny<Exception>(() => SeVersion.Parse("not-a-version"));
        Assert.ThrowsAny<Exception>(() => SeVersion.Parse(""));
        Assert.ThrowsAny<Exception>(() => SeVersion.Parse("1.2"));
        // 注意: 少于 5 段的输入也会抛 IndexOutOfRangeException —— 见完善度评估
        Assert.ThrowsAny<Exception>(() => SeVersion.Parse("2024.08.20"));
    }

    [Fact]
    public void Parse_ShortVersion_ThrowsByCurrentBehavior()
    {
        // 当前实现严格要求 5 段（year.month.day.revision.part），
        // 短版本（如 boot 目录的 ffxivboot.ver）会导致 IndexOutOfRangeException。
        // 这是待改进项（建议不足 5 段时补零），此处锁定当前行为。
        Assert.ThrowsAny<Exception>(() => SeVersion.Parse("2024.08.20"));
    }
}
