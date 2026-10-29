using WinRedisClient.Core.Models;
using WinRedisClient.Presentation.ViewModels;

namespace WinRedisClient.Core.Tests;

public sealed class ThemeCatalogTests
{
    [Fact]
    public void Catalog_CoversEveryThemePreferenceExactlyOnce()
    {
        var values = ThemeCatalog.All.Select(option => option.Value).ToArray();

        Assert.Equal(Enum.GetValues<ThemePreference>().Length, values.Length);
        Assert.Equal(values.Length, values.Distinct().Count());
        foreach (var preference in Enum.GetValues<ThemePreference>())
        {
            Assert.Contains(preference, values);
        }
    }

    [Fact]
    public void Catalog_ProvidesPreviewColorsForEveryTheme()
    {
        Assert.All(ThemeCatalog.All, option =>
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Name));
            Assert.StartsWith("#", option.SidebarColor);
            Assert.StartsWith("#", option.AppColor);
            Assert.StartsWith("#", option.CardColor);
            Assert.StartsWith("#", option.AccentColor);
        });
    }

    [Fact]
    public void For_UnknownPreference_FallsBackToFirstTheme()
    {
        Assert.Same(ThemeCatalog.All[0], ThemeCatalog.For((ThemePreference)999));
    }
}
