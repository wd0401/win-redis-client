using WinRedisClient.Core.Services;

namespace WinRedisClient.Core.Tests;

[Collection("AppServicesSequential")]
public class TtlFormatterTests
{
    [Fact]
    public void Format_Null_MeansNoExpiry()
    {
        Assert.Equal("永不过期", TtlFormatter.Format(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Format_NonPositive_MeansExpired(int seconds)
    {
        Assert.Equal("已过期", TtlFormatter.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Format_Days_WithAndWithoutHours()
    {
        Assert.Equal("3天", TtlFormatter.Format(TimeSpan.FromDays(3)));
        Assert.Equal("2天 3小时", TtlFormatter.Format(TimeSpan.FromDays(2) + TimeSpan.FromHours(3)));
    }

    [Fact]
    public void Format_Hours_WithAndWithoutMinutes()
    {
        Assert.Equal("5小时", TtlFormatter.Format(TimeSpan.FromHours(5)));
        Assert.Equal("2小时 5分钟", TtlFormatter.Format(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Format_Minutes_WithAndWithoutSeconds()
    {
        Assert.Equal("4分钟", TtlFormatter.Format(TimeSpan.FromMinutes(4)));
        Assert.Equal("1分钟 30秒", TtlFormatter.Format(TimeSpan.FromSeconds(90)));
    }

    [Theory]
    [InlineData(30, "30秒")]
    [InlineData(0.4, "1秒")]
    [InlineData(59.2, "60秒")]
    public void Format_Seconds_UsesCeiling(double seconds, string expected)
    {
        Assert.Equal(expected, TtlFormatter.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void FormatSeconds_HandlesNoExpiry()
    {
        Assert.Equal("永不过期", TtlFormatter.FormatSeconds(-1));
        Assert.Equal("2分钟", TtlFormatter.FormatSeconds(120));
        Assert.Equal("已过期", TtlFormatter.FormatSeconds(0));
    }

    [Theory]
    [InlineData(null, "请输入 TTL 秒数，或选择永不过期")]
    [InlineData("", "请输入 TTL 秒数，或选择永不过期")]
    [InlineData("abc", "TTL 必须是整数秒")]
    [InlineData("1.5", "TTL 必须是整数秒")]
    [InlineData("0", "TTL 必须大于 0；清除过期时间请选择“永不过期”")]
    [InlineData("-5", "TTL 必须大于 0；清除过期时间请选择“永不过期”")]
    [InlineData("315360001", "TTL 超出合理范围（最大 10 年）")]
    public void TryParseSeconds_RejectsInvalidInput(string? text, string expectedError)
    {
        Assert.False(TtlFormatter.TryParseSeconds(text, out var ttl, out var error));
        Assert.Null(ttl);
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void TryParseSeconds_MinusOne_MeansNoExpiry()
    {
        Assert.True(TtlFormatter.TryParseSeconds("-1", out var ttl, out var error));
        Assert.Null(ttl);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("3600", 3600)]
    [InlineData(" 120 ", 120)]
    [InlineData("315360000", 315360000)]
    public void TryParseSeconds_AcceptsPositiveValues(string text, long expectedSeconds)
    {
        Assert.True(TtlFormatter.TryParseSeconds(text, out var ttl, out var error));
        Assert.Null(error);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ttl);
    }

    [Fact]
    public void FormatBytes_UsesReadableUnits()
    {
        Assert.Equal("—", TtlFormatter.FormatBytes(null));
        Assert.Equal("512 B", TtlFormatter.FormatBytes(512));
        Assert.Equal("1.5 KB", TtlFormatter.FormatBytes(1536));
        Assert.Equal("2 MB", TtlFormatter.FormatBytes(2L * 1024 * 1024));
        Assert.Equal("1.5 GB", TtlFormatter.FormatBytes((long)(1.5 * 1024 * 1024 * 1024)));
    }
}
