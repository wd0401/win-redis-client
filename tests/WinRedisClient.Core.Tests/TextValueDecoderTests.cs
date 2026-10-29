using WinRedisClient.Core.Services;

namespace WinRedisClient.Core.Tests;

public class TextValueDecoderTests
{
    [Fact]
    public void Decodes_Utf8_TextAsUtf8()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("机械键盘 87键");

        Assert.True(TextValueDecoder.TryDecode(bytes, out var text, out var encoding));
        Assert.Equal("UTF-8", encoding);
        Assert.Equal("机械键盘 87键", text);
    }

    [Fact]
    public void Decodes_GbkBytes_AsTextInsteadOfReplacementCharacters()
    {
        // shop:product:2002 的实际存储字节（GBK）
        var bytes = new byte[]
        {
            0xBB, 0xFA, 0xD0, 0xB5, 0xBC, 0xFE, 0xC5, 0xCC, 0x20, 0x38, 0x37, 0xBC, 0xFE,
        };

        Assert.True(TextValueDecoder.TryDecode(bytes, out var text, out var encoding));
        Assert.Equal("GBK", encoding);
        Assert.Equal("机械件盘 87件", text);
        Assert.DoesNotContain('\uFFFD', text);
    }

    [Fact]
    public void Binary_Values_AreEscaped()
    {
        var bytes = new byte[] { 0x00, 0x01, 0xFF, 0x10 };

        var display = TextValueDecoder.DecodeForDisplay(bytes);

        Assert.StartsWith(@"\x00\x01\xff\x10", display);
    }
}
