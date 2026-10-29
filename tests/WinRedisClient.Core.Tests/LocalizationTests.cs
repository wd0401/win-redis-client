using System.Text.Json;
using WinRedisClient.Core.Localization;

namespace WinRedisClient.Core.Tests;

/// <summary>本地化资源与回退行为验证。</summary>
[Collection("AppServicesSequential")]
public sealed class LocalizationTests
{
    [Fact]
    public void ChineseMode_ReturnsSourceText()
    {
        Localizer.Instance.SetLanguage(AppLanguage.Chinese);
        Assert.Equal("取消", Loc.T("取消"));
        Assert.Equal("已加载 3 个 Key", Loc.F("已加载 {0} 个 Key", 3));
    }

    [Fact]
    public void EnglishMode_TranslatesKnownKeys()
    {
        try
        {
            Localizer.Instance.SetLanguage(AppLanguage.English);
            Assert.Equal("Cancel", Loc.T("取消"));
            Assert.Equal("Loaded 7 keys", Loc.F("已加载 {0:N0} 个 Key", 7));
        }
        finally
        {
            Localizer.Instance.SetLanguage(AppLanguage.Chinese);
        }
    }

    [Fact]
    public void UnknownKey_FallsBackToSource()
    {
        try
        {
            Localizer.Instance.SetLanguage(AppLanguage.English);
            Assert.Equal("不存在的中文键", Loc.T("不存在的中文键"));
        }
        finally
        {
            Localizer.Instance.SetLanguage(AppLanguage.Chinese);
        }
    }

    [Fact]
    public void EnglishResource_IsCompleteAndFormatSafe()
    {
        using var stream = typeof(Localizer).Assembly
            .GetManifestResourceStream("WinRedisClient.Core.Localization.en.json");
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        Assert.NotNull(map);
        Assert.True(map.Count > 800, $"en.json 仅包含 {map.Count} 条翻译");

        static string Placeholders(string text) =>
            string.Concat(System.Text.RegularExpressions.Regex.Matches(text, @"\{(\d+)")
                .Select(m => m.Groups[1].Value + ",")
                .OrderBy(v => v, StringComparer.Ordinal));

        foreach (var (source, translation) in map)
        {
            Assert.False(string.IsNullOrWhiteSpace(translation), $"翻译为空：{source}");
            Assert.Equal(Placeholders(source), Placeholders(translation));
        }
    }
}
