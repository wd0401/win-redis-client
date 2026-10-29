using System.Windows;

namespace WinRedisClient.App.Themes;

/// <summary>
/// 应用级 UI 字体缩放。XAML 通过 DynamicResource 引用 Fs* 字号键，
/// 这里按用户选择的倍率更新资源，已加载和后续加载的文本都会即时生效。
/// </summary>
public static class FontScaleManager
{
    public const double MinScale = 0.8;
    public const double MaxScale = 1.5;

    private static readonly (string Key, double BaseSize)[] Sizes =
    [
        ("Fs75", 7.5),
        ("Fs9", 9),
        ("Fs95", 9.5),
        ("Fs10", 10),
        ("Fs105", 10.5),
        ("Fs11", 11),
        ("Fs115", 11.5),
        ("Fs12", 12),
        ("Fs125", 12.5),
        ("Fs13", 13),
        ("Fs14", 14),
        ("Fs145", 14.5),
        ("Fs15", 15),
        ("Fs16", 16),
        ("Fs17", 17),
        ("Fs18", 18),
        ("Fs20", 20),
        ("Fs23", 23),
        ("Fs26", 26),
        ("Fs30", 30),
        ("Fs36", 36),
    ];

    public static double CurrentScale { get; private set; } = 1.0;

    public static void Apply(double scale)
    {
        scale = Math.Clamp(scale, MinScale, MaxScale);
        if (Math.Abs(scale - CurrentScale) < 0.001 && Application.Current is not null &&
            Application.Current.Resources.Contains("Fs13"))
        {
            return;
        }

        CurrentScale = scale;

        var resources = Application.Current?.Resources;
        if (resources is null) return;

        foreach (var (key, baseSize) in Sizes)
        {
            resources[key] = Math.Round(baseSize * scale, 1);
        }
    }
}
