using WinRedisClient.Core.Localization;

namespace WinRedisClient.Core.Services;

/// <summary>TTL 显示格式化。null 表示永不过期；小于等于 0 表示已过期/不存在。</summary>
public static class TtlFormatter
{
    public static string Format(TimeSpan? ttl)
    {
        if (ttl is null) return Loc.T("永不过期");
        var value = ttl.Value;
        if (value <= TimeSpan.Zero) return Loc.T("已过期");

        if (value.TotalDays >= 1)
        {
            var days = (int)value.TotalDays;
            var hours = value.Hours;
            return hours > 0 ? Loc.F("{0}天 {1}小时", days, hours) : Loc.F("{0}天", days);
        }

        if (value.TotalHours >= 1)
        {
            var hours = (int)value.TotalHours;
            var minutes = value.Minutes;
            return minutes > 0 ? Loc.F("{0}小时 {1}分钟", hours, minutes) : Loc.F("{0}小时", hours);
        }

        if (value.TotalMinutes >= 1)
        {
            var minutes = (int)value.TotalMinutes;
            var seconds = value.Seconds;
            return seconds > 0 ? Loc.F("{0}分钟 {1}秒", minutes, seconds) : Loc.F("{0}分钟", minutes);
        }

        return Loc.F("{0}秒", Math.Max(0, (int)Math.Ceiling(value.TotalSeconds)));
    }

    public static string FormatSeconds(long seconds)
    {
        if (seconds < 0) return Loc.T("永不过期");
        return Format(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>将秒数解析为 TimeSpan；-1 表示永不过期。</summary>
    public static bool TryParseSeconds(string? text, out TimeSpan? ttl, out string? error)
    {
        ttl = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = Loc.T("请输入 TTL 秒数，或选择永不过期");
            return false;
        }

        if (!long.TryParse(text.Trim(), out var seconds))
        {
            error = Loc.T("TTL 必须是整数秒");
            return false;
        }

        if (seconds == -1)
        {
            ttl = null;
            return true;
        }

        if (seconds <= 0)
        {
            error = Loc.T("TTL 必须大于 0；清除过期时间请选择“永不过期”");
            return false;
        }

        if (seconds > 10L * 365 * 24 * 3600)
        {
            error = Loc.T("TTL 超出合理范围（最大 10 年）");
            return false;
        }

        ttl = TimeSpan.FromSeconds(seconds);
        return true;
    }

    public static string FormatBytes(long? bytes)
    {
        if (bytes is null) return "—";
        var value = bytes.Value;
        if (value < 1024) return $"{value} B";
        if (value < 1024 * 1024) return $"{value / 1024.0:0.#} KB";
        if (value < 1024L * 1024 * 1024) return $"{value / (1024.0 * 1024):0.#} MB";
        return $"{value / (1024.0 * 1024 * 1024):0.##} GB";
    }
}
