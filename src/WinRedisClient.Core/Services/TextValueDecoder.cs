using System.Text;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Core.Services;

/// <summary>
/// Redis 字节值文本解码：优先严格 UTF-8，失败后回退 GB18030/GBK。
/// 无法作为文本解码时返回转义的十六进制，避免显示替换字符造成“乱码”。
/// </summary>
public static class TextValueDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding StrictGb18030 = CreateGb18030();

    static TextValueDecoder()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
            // 运行环境缺少代码页提供程序时仅使用 UTF-8
        }
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out string text, out string encodingName)
    {
        text = string.Empty;
        encodingName = string.Empty;
        if (data.IsEmpty)
        {
            encodingName = "UTF-8";
            return true;
        }

        try
        {
            var utf8 = StrictUtf8.GetString(data);
            if (LooksLikeText(utf8))
            {
                text = utf8;
                encodingName = "UTF-8";
                return true;
            }
        }
        catch (DecoderFallbackException)
        {
            // 尝试 GBK/GB18030
        }

        try
        {
            var gb = StrictGb18030.GetString(data);
            if (LooksLikeText(gb))
            {
                text = gb;
                encodingName = "GBK";
                return true;
            }
        }
        catch (DecoderFallbackException)
        {
            // 非文本字节
        }

        return false;
    }

    /// <summary>集合元素/Key 名等展示值：文本优先，否则输出 \xNN 转义。</summary>
    public static string DecodeForDisplay(ReadOnlySpan<byte> data, int maxBinaryBytes = 256)
    {
        if (TryDecode(data, out var text, out _)) return text;
        return EscapeBinary(data, maxBinaryBytes);
    }

    public static string EscapeBinary(ReadOnlySpan<byte> data, int maxBytes = 256)
    {
        var length = Math.Min(data.Length, Math.Max(0, maxBytes));
        var sb = new StringBuilder(length * 4 + 24);
        for (var i = 0; i < length; i++)
            sb.Append("\\x").Append(data[i].ToString("x2"));
        if (data.Length > length)
            sb.Append(Loc.F(" …（共 {0:N0} 字节）", data.Length));
        return sb.ToString();
    }

    private static bool LooksLikeText(string value)
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

    private static Encoding CreateGb18030()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }
}
