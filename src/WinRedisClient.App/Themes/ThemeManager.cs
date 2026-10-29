using System.Windows;
using WinRedisClient.Core.Models;

namespace WinRedisClient.App.Themes;

public enum ThemeMode
{
    Light,
    Dark,
    System,
    Salt,
    Sugar,
    Forest,
    Flower,
    Wind,
}

/// <summary>管理应用主题切换：亮色 / 暗色 / 跟随系统。</summary>
public static class ThemeManager
{
    private static ThemeMode _currentMode = ThemeMode.System;

    public static ThemeMode CurrentMode => _currentMode;

    /// <summary>主题切换事件。</summary>
    public static event EventHandler<ThemeMode>? ThemeChanged;

    /// <summary>把持久化的主题偏好映射为运行时主题模式。</summary>
    public static ThemeMode FromPreference(ThemePreference preference) => preference switch
    {
        ThemePreference.Dark => ThemeMode.Dark,
        ThemePreference.System => ThemeMode.System,
        ThemePreference.Salt => ThemeMode.Salt,
        ThemePreference.Sugar => ThemeMode.Sugar,
        ThemePreference.Forest => ThemeMode.Forest,
        ThemePreference.Flower => ThemeMode.Flower,
        ThemePreference.Wind => ThemeMode.Wind,
        _ => ThemeMode.Light,
    };

    /// <summary>初始化主题（跟随系统）。</summary>
    public static void Initialize()
    {
        Apply(ThemeMode.System);
    }

    /// <summary>切换主题。</summary>
    public static void Apply(ThemeMode mode)
    {
        _currentMode = mode;

        var actualTheme = mode == ThemeMode.System ? GetSystemTheme() : mode;
        SwapColorDictionary(actualTheme);
        ThemeChanged?.Invoke(null, mode);
    }

    /// <summary>获取当前系统主题。</summary>
    public static ThemeMode GetSystemTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0 ? ThemeMode.Dark : ThemeMode.Light;
        }
        catch
        {
            return ThemeMode.Light;
        }
    }

    private static void SwapColorDictionary(ThemeMode actualTheme)
    {
        var app = Application.Current;
        if (app?.Resources.MergedDictionaries is not { Count: > 0 } dicts) return;

        // 找到颜色字典（索引 0），替换为对应主题
        var fileName = actualTheme switch
        {
            ThemeMode.Dark => "Dark.xaml",
            ThemeMode.Salt => "Salt.xaml",
            ThemeMode.Sugar => "Sugar.xaml",
            ThemeMode.Forest => "Forest.xaml",
            ThemeMode.Flower => "Flower.xaml",
            ThemeMode.Wind => "Wind.xaml",
            _ => "Light.xaml",
        };

        var newColors = new ResourceDictionary
        {
            Source = new Uri($"Themes/{fileName}", UriKind.Relative),
        };

        dicts[0] = newColors;
    }
}
