using WinRedisClient.Core.Models;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

/// <summary>设置页主题选择项：名称、持久化值、说明与预览色。</summary>
public sealed record ThemeOptionViewModel(
    string Name,
    ThemePreference Value,
    string Description,
    string SidebarColor,
    string AppColor,
    string CardColor,
    string AccentColor,
    bool IsSystem = false);

/// <summary>内置主题目录。预览色与 Themes/*.xaml 的表面色保持一致。</summary>
public static class ThemeCatalog
{
    public static IReadOnlyList<ThemeOptionViewModel> All { get; } =
    [
        new(Loc.T("浅色"), ThemePreference.Light, Loc.T("Light · 清爽冷白 + 品牌紫"),
            "#FFFFFF", "#F5F7FB", "#FFFFFF", "#625BFF"),
        new(Loc.T("深色"), ThemePreference.Dark, Loc.T("Dark · 低亮度深色界面"),
            "#121725", "#0E1220", "#171C2B", "#8B85FF"),
        new(Loc.T("跟随系统"), ThemePreference.System, Loc.T("System · 跟随 Windows 明暗设置"),
            "#E9EDF3", "#F4F7FB", "#FFFFFF", "#7C86A0", IsSystem: true),
        new(Loc.T("海盐"), ThemePreference.Salt, Loc.T("Salt · 清冷灰青 + 珊瑚强调"),
            "#E9F1F1", "#F4F9F9", "#FBFDFD", "#C0523F"),
        new(Loc.T("奶糖"), ThemePreference.Sugar, Loc.T("Sugar · 温暖米色 + 琥珀强调"),
            "#F3EBDD", "#FBF7EF", "#FFFDF8", "#9C6528"),
        new(Loc.T("森林"), ThemePreference.Forest, Loc.T("Forest · 抹茶豆绿 + 森林绿强调"),
            "#E6EEE3", "#F5F9F2", "#FCFEFA", "#3F7B52"),
        new(Loc.T("繁花"), ThemePreference.Flower, Loc.T("Flower · 丁香粉紫 + 紫罗兰强调"),
            "#F0E8F0", "#FAF6FA", "#FEFBFE", "#7C5AA6"),
        new(Loc.T("微风"), ThemePreference.Wind, Loc.T("Wind · 莫兰迪灰 + 岩蓝灰强调"),
            "#DDE1E3", "#F1F3F4", "#FAFBFB", "#5F7078"),
    ];

    public static ThemeOptionViewModel For(ThemePreference preference)
        => All.FirstOrDefault(option => option.Value == preference) ?? All[0];
}
