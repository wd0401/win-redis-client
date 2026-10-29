using WinRedisClient.Presentation.ViewModels;

namespace WinRedisClient.Core.Tests;

public sealed class DatabaseEntryTests
{
    [Fact]
    public void SetCount_AppendsFormattedKeyCount()
    {
        var entry = new DatabaseEntryViewModel(9);

        entry.SetCount(1729);

        Assert.Equal("DB 9 · 1,729", entry.Text);
    }

    [Fact]
    public void SetCount_ZeroIsShownExplicitly()
    {
        var entry = new DatabaseEntryViewModel(3);

        entry.SetCount(0);

        Assert.Equal("DB 3 · 0", entry.Text);
    }

    [Fact]
    public void SetCount_UnknownKeepsOnlyDatabaseNumber()
    {
        var entry = new DatabaseEntryViewModel(2);
        entry.SetCount(10);

        entry.SetCount(null);

        Assert.Equal("DB 2", entry.Text);
    }

    [Fact]
    public void SetLoading_ShowsProgressMarker()
    {
        var entry = new DatabaseEntryViewModel(9);

        entry.SetLoading();

        Assert.Equal("DB 9 · …", entry.Text);
    }
}
