using System.Globalization;
using System.Text.Json;

namespace WinRedisClient.Core.Localization;

/// <summary>
/// 界面文案本地化：以中文原文为键，英文模式按下表翻译，缺失时回退中文原文。
/// 语言在启动时由设置决定，切换语言需要重启应用以保证界面一致。
/// </summary>
public sealed class Localizer
{
    private const string ResourceName = "WinRedisClient.Core.Localization.en.json";

    private static readonly IReadOnlyDictionary<string, string> English = LoadEnglish();

    private Localizer()
    {
    }

    public static Localizer Instance { get; } = new();

    /// <summary>当前语言。</summary>
    public AppLanguage Language { get; private set; } = AppLanguage.Chinese;

    /// <summary>检索键（中文原文），返回当前语言文案。</summary>
    public string this[string source] => Translate(source);

    public void SetLanguage(AppLanguage language) => Language = language;

    /// <summary>把中文原文翻译为当前语言；英文缺失时回退原文。</summary>
    public static string Translate(string source)
    {
        if (string.IsNullOrEmpty(source) || Instance.Language != AppLanguage.English)
            return source;

        return English.TryGetValue(source, out var translated) ? translated : source;
    }

    /// <summary>翻译后再按当前区域性格式化。</summary>
    public static string Format(string source, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Translate(source), args);

    private static Dictionary<string, string> LoadEnglish()
    {
        try
        {
            using var stream = typeof(Localizer).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return [];

            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
                   ?? [];
        }
        catch
        {
            // 翻译资源缺失或损坏时完全回退中文，不影响启动。
            return [];
        }
    }
}

/// <summary>短别名入口：Loc.T("中文") / Loc.F("已加载 {0} 个", count)。</summary>
public static class Loc
{
    public static string T(string source) => Localizer.Translate(source);

    public static string F(string source, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Localizer.Translate(source), args);
}
