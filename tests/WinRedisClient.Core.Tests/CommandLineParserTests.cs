using WinRedisClient.Core.Services;

namespace WinRedisClient.Core.Tests;

[Collection("AppServicesSequential")]
public class CommandLineParserTests
{
    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(CommandLineParser.Parse(string.Empty));
        Assert.Empty(CommandLineParser.Parse("   \t  "));
    }

    [Fact]
    public void Parse_WhitespaceSeparatedTokens()
    {
        var tokens = CommandLineParser.Parse("SET  user:1  \"Alice\"");
        Assert.Equal(["SET", "user:1", "Alice"], tokens);
    }

    [Fact]
    public void Parse_SingleQuotes_PreserveSpacesAndBackslashes()
    {
        var tokens = CommandLineParser.Parse("SET k 'a b\\n'");
        Assert.Equal(["SET", "k", @"a b\n"], tokens);
    }

    [Fact]
    public void Parse_DoubleQuotes_ProcessEscapes()
    {
        var tokens = CommandLineParser.Parse("SET k \"line1\\nline2\\t\\\"x\\\"\"");
        Assert.Equal(["SET", "k", "line1\nline2\t\"x\""], tokens);
    }

    [Fact]
    public void Parse_UnquotedBackslashEscapes()
    {
        var tokens = CommandLineParser.Parse(@"ECHO a\tb\qc");
        Assert.Equal(["ECHO", "a\tbqc"], tokens);
    }

    [Fact]
    public void Parse_QuotedSegmentConcatenatesWithAdjacentText()
    {
        var tokens = CommandLineParser.Parse("key'ab cd'ef");
        Assert.Equal(["keyab cdef"], tokens);
    }

    [Fact]
    public void Parse_EmptyQuotedToken_IsRetained()
    {
        var tokens = CommandLineParser.Parse("SET k '' \"\"");
        Assert.Equal(["SET", "k", "", ""], tokens);
    }

    [Theory]
    [InlineData("GET \"unclosed")]
    [InlineData("GET 'unclosed")]
    public void Parse_UnclosedQuote_Throws(string input)
    {
        var ex = Assert.Throws<FormatException>(() => CommandLineParser.Parse(input));
        Assert.Contains("引号未闭合", ex.Message);
    }

    [Fact]
    public void Parse_DoesNotInterpretShellOperators()
    {
        var tokens = CommandLineParser.Parse("GET k | DEL other");
        Assert.Equal(["GET", "k", "|", "DEL", "other"], tokens);
    }
}
