using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WinRedisClient.Core.Models;

namespace WinRedisClient.App;

/// <summary>通用布尔 → 可见性；ConverterParameter=invert 时取反。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (IsInvert(parameter)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility visibility && visibility == Visibility.Visible;

    internal static bool IsInvert(object? parameter)
        => parameter is string text && text.Equals("invert", StringComparison.OrdinalIgnoreCase);
}

/// <summary>非 null → 可见；ConverterParameter=invert 时取反。</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is not null;
        if (BoolToVisibilityConverter.IsInvert(parameter)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>空字符串 → 可见（用于占位提示）；ConverterParameter=invert 表示非空可见。</summary>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = string.IsNullOrEmpty(value as string);
        if (BoolToVisibilityConverter.IsInvert(parameter)) empty = !empty;
        return empty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>数量 &gt; 0 → 可见；ConverterParameter=invert 表示 0 时可见。</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        long count = value switch
        {
            int i => i,
            long l => l,
            double d => (long)d,
            _ => 0,
        };
        var flag = count > 0;
        if (BoolToVisibilityConverter.IsInvert(parameter)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not bool b || !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not bool b || !b;
}

/// <summary>环境标签 → 颜色画刷。</summary>
public sealed class EnvironmentToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            EnvironmentTag.Production => "EnvProdBrush",
            EnvironmentTag.Staging => "EnvStagingBrush",
            EnvironmentTag.Testing => "EnvTestBrush",
            EnvironmentTag.Development => "EnvDevBrush",
            _ => "PrimaryBrush",
        };
        return Lookup(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    internal static Brush Lookup(string key)
        => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>环境标签 → 浅色背景画刷。</summary>
public sealed class EnvironmentToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is EnvironmentTag.Production
            ? EnvironmentToBrushConverter.Lookup("EnvProdBgBrush")
            : EnvironmentToBrushConverter.Lookup("PrimaryLightBrush");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Key 类型 → 类型色。ConverterParameter=fg 取前景，否则背景。</summary>
public sealed class KeyTypeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var suffix = parameter is string p && p.Equals("fg", StringComparison.OrdinalIgnoreCase) ? "Fg" : "Bg";
        var name = (value as string)?.ToLowerInvariant() switch
        {
            "string" => "TypeString",
            "hash" => "TypeHash",
            "list" => "TypeList",
            "set" => "TypeSet",
            "zset" => "TypeZset",
            "stream" => "TypeStream",
            _ => "TypeDefault",
        };
        return EnvironmentToBrushConverter.Lookup(name + suffix);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Diff 行类型 → 颜色。</summary>
public sealed class DiffKindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "Added" => EnvironmentToBrushConverter.Lookup("SuccessBrush"),
            "Removed" => EnvironmentToBrushConverter.Lookup("DangerBrush"),
            "Modified" => EnvironmentToBrushConverter.Lookup("WarningBrush"),
            _ => EnvironmentToBrushConverter.Lookup("TextSecondaryBrush"),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>颜色字符串（#RRGGBB）→ 画刷。</summary>
public sealed class StringToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text)) return Brushes.Gray;
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Gray;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>WindowState → 可见性。ConverterParameter=maximized / normal。</summary>
public sealed class WindowStateToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var maximized = value is WindowState.Maximized;
        var wantMaximized = parameter is string text && text.Equals("maximized", StringComparison.OrdinalIgnoreCase);
        return maximized == wantMaximized ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>数值 &gt; 阈值 → 可见；参数 "invert" 反转。</summary>
public sealed class GreaterThanZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value switch
        {
            double d => d > 0,
            int i => i > 0,
            long l => l > 0,
            _ => false,
        };
        if (BoolToVisibilityConverter.IsInvert(parameter)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>布尔值 → 资源画刷。ConverterParameter="TrueKey|FalseKey"。</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        var keys = (parameter as string ?? string.Empty).Split('|');
        var key = flag ? keys.ElementAtOrDefault(0) : keys.ElementAtOrDefault(1);
        return string.IsNullOrEmpty(key)
            ? EnvironmentToBrushConverter.Lookup("TextSecondaryBrush")
            : EnvironmentToBrushConverter.Lookup(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>double → GridLength（侧栏宽度绑定）。</summary>
public sealed class DoubleToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new GridLength(value is double d ? d : 0, GridUnitType.Pixel);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is GridLength length ? length.Value : 0d;
}
