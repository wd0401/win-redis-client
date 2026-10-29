using System.Text;
using WinRedisClient.Core.Services;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.Support;

/// <summary>展示层通用的格式化与安全文本辅助。不包含任何 WPF 依赖。</summary>
public static class UiText
{
    /// <summary>按 UTF-8 → GBK/GB18030 顺序解码；含二进制控制字符时返回 false。</summary>
    public static bool TryDecodeText(ReadOnlySpan<byte> data, out string text)
        => TryDecodeText(data, out text, out _);

    /// <summary>按 UTF-8 → GBK/GB18030 顺序解码，并返回实际使用的编码。</summary>
    public static bool TryDecodeText(ReadOnlySpan<byte> data, out string text, out string encodingName)
    {
        text = string.Empty;
        encodingName = string.Empty;
        if (data.IsEmpty)
        {
            encodingName = "UTF-8";
            return true;
        }

        if (!TextValueDecoder.TryDecode(data, out var decoded, out encodingName)) return false;
        if (!LooksLikeText(decoded)) return false;

        text = decoded;
        return true;
    }

    /// <summary>判断字符串是否像可展示文本（控制字符占比低）。</summary>
    public static bool LooksLikeText(string value)
    {
        if (value.Length == 0) return true;
        var control = 0;
        foreach (var c in value)
        {
            if (c is '\r' or '\n' or '\t') continue;
            if (char.IsControl(c)) control++;
        }
        return control * 100 <= value.Length;
    }

    /// <summary>二进制预览（十六进制 + ASCII 侧栏），限制输出长度。</summary>
    public static string HexPreview(ReadOnlySpan<byte> data, int maxBytes = 512)
    {
        var length = Math.Min(data.Length, Math.Max(0, maxBytes));
        var sb = new StringBuilder(length * 4);
        for (var offset = 0; offset < length; offset += 16)
        {
            var lineLength = Math.Min(16, length - offset);
            sb.Append(offset.ToString("X8")).Append("  ");

            for (var i = 0; i < 16; i++)
            {
                if (i < lineLength)
                    sb.Append(data[offset + i].ToString("X2")).Append(' ');
                else
                    sb.Append("   ");
                if (i == 7) sb.Append(' ');
            }

            sb.Append(" |");
            for (var i = 0; i < lineLength; i++)
            {
                var b = data[offset + i];
                sb.Append(b is >= 32 and < 127 ? (char)b : '.');
            }
            sb.AppendLine("|");
        }

        if (data.Length > length)
            sb.AppendLine(Loc.F("… 共 {0:N0} bytes，仅显示前 {1:N0} bytes", data.Length, length));
        return sb.ToString();
    }

    /// <summary>截断长文本（保留末尾省略信息）。</summary>
    public static string Ellipsis(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length <= maxLength) return value;
        return value[..Math.Max(0, maxLength)] + "…";
    }

    /// <summary>单行摘要：把换行折叠为空格。</summary>
    public static string SingleLine(string? value, int maxLength = 160)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var flat = value.Replace("\r", " ").Replace("\n", " ").Trim();
        while (flat.Contains("  ", StringComparison.Ordinal))
            flat = flat.Replace("  ", " ", StringComparison.Ordinal);
        return Ellipsis(flat, maxLength);
    }

    public static string FormatNumber(long value) => value.ToString("N0");

    public static string FormatUptime(long seconds)
    {
        if (seconds < 0) return "—";
        return seconds switch
        {
            < 60 => Loc.F("{0} 秒", seconds),
            < 3600 => Loc.F("{0} 分钟", seconds / 60),
            < 86400 => Loc.F("{0} 小时 {1} 分钟", seconds / 3600, seconds % 3600 / 60),
            _ => Loc.F("{0} 天 {1} 小时", seconds / 86400, seconds % 86400 / 3600),
        };
    }

    /// <summary>从错误信息中移除可能出现的密码等敏感串。</summary>
    public static string Redact(string? message, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        var result = message;
        foreach (var secret in secrets)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 3) continue;
            result = result.Replace(secret, "••••••", StringComparison.Ordinal);
        }
        return result;
    }
}
