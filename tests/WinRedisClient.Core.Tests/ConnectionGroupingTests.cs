using WinRedisClient.Core.Models;
using WinRedisClient.Presentation.ViewModels;

namespace WinRedisClient.Core.Tests;

public sealed class ConnectionGroupingTests
{
    [Theory]
    [InlineData("", "未分组")]
    [InlineData("   ", "未分组")]
    [InlineData(" 生产集群 ", "生产集群")]
    public void GroupName_FallsBackAndTrims(string group, string expected)
    {
        var item = new ConnectionItemViewModel { Group = group };

        Assert.Equal(expected, item.GroupName);
    }

    [Fact]
    public void WarningColor_ExposesDisplayAndBrushHex()
    {
        var item = new ConnectionItemViewModel { WarningColor = ConnectionWarningColor.Red };

        Assert.True(item.HasWarningColor);
        Assert.Equal("#DC2626", item.WarningColorHex);
        Assert.Contains("红色", item.WarningColorText);
    }

    [Fact]
    public void WarningColor_NoneHasNoMarker()
    {
        var item = new ConnectionItemViewModel { WarningColor = ConnectionWarningColor.None };

        Assert.False(item.HasWarningColor);
    }

    [Fact]
    public void WarningColorCatalog_CoversEveryValue()
    {
        foreach (var value in Enum.GetValues<ConnectionWarningColor>())
        {
            var option = WarningColorCatalog.For(value);
            Assert.Equal(value, option.Value);
            Assert.StartsWith("#", option.Hex);
        }
    }
}
