using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Services;

namespace WinRedisClient.Core.Tests;

[Collection("AppServicesSequential")]
public class KeyDiffEngineTests
{
    private static KeyContent Text(string text, string type = "string", bool truncated = false, string? error = null)
        => new("k", type, null, text, [], truncated, error);

    private static KeyContent Collection(string type, params (string Field, string Value, string Score)[] items)
        => new("k", type, null, null,
            items.Select(i => new ElementData(i.Field, i.Value, i.Score)).ToList(), false);

    [Fact]
    public void CompareText_IdenticalContent_ReportsNoChanges()
    {
        var result = KeyDiffEngine.CompareText("a\nb\nc", "a\nb\nc");

        Assert.False(result.HasChanges);
        Assert.Equal(0, result.Added + result.Removed + result.Modified);
        Assert.Contains("内容一致", result.Summary);
    }

    [Fact]
    public void CompareText_NormalizesLineEndings()
    {
        var result = KeyDiffEngine.CompareText("a\r\nb", "a\nb");
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void CompareText_DetectsAddedLine()
    {
        var result = KeyDiffEngine.CompareText("a\nc", "a\nb\nc");

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.Contains(result.Lines, l => l.Kind == DiffChangeKind.Added && l.Text == "b");
        Assert.Contains("新增 1 行", result.Summary);
    }

    [Fact]
    public void CompareText_DetectsRemovedAndAddedForReplacement()
    {
        var result = KeyDiffEngine.CompareText("a\nb", "a\nc");

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Removed);
        Assert.Contains(result.Lines, l => l.Kind == DiffChangeKind.Removed && l.Text == "b");
        Assert.Contains(result.Lines, l => l.Kind == DiffChangeKind.Added && l.Text == "c");
    }

    [Fact]
    public void CompareText_TruncatedInput_MarksSummary()
    {
        var result = KeyDiffEngine.CompareText("a", "a", truncated: true);
        Assert.False(result.HasChanges);
        Assert.Contains("截断", result.Summary);
    }

    [Fact]
    public void Compare_TypeMismatch_DoesNotCompareContent()
    {
        var result = KeyDiffEngine.Compare(Text("x"), Collection("hash", ("f", "v", "")));

        Assert.True(result.TypeMismatch);
        Assert.False(result.HasChanges);
        Assert.Contains("类型不同", result.Summary);
    }

    [Fact]
    public void Compare_ReadError_ReportsFailure()
    {
        var result = KeyDiffEngine.Compare(Text("", error: "连接已断开"), Text("x"));

        Assert.False(result.HasChanges);
        Assert.Contains("读取失败", result.Summary);
        Assert.Contains("连接已断开", result.Summary);
    }

    [Fact]
    public void Compare_Hash_DetectsAddedRemovedAndModified()
    {
        var left = Collection("hash", ("a", "1", ""), ("b", "2", ""));
        var right = Collection("hash", ("a", "1", ""), ("b", "3", ""), ("c", "4", ""));

        var result = KeyDiffEngine.Compare(left, right);

        Assert.False(result.TypeMismatch);
        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.Equal(1, result.Modified);
        Assert.Contains("新增 1", result.Summary);
        Assert.Contains("修改 1", result.Summary);
    }

    [Fact]
    public void Compare_List_OrdersIndicesNumerically()
    {
        var left = Collection("list",
            ("0", "a", ""), ("1", "b", ""), ("2", "c", ""), ("10", "d", ""));
        var right = Collection("list",
            ("0", "a", ""), ("2", "c", ""), ("10", "d", ""));

        var result = KeyDiffEngine.Compare(left, right);

        Assert.Equal(1, result.Removed);
        var removedLine = Assert.Single(result.Lines, l => l.Kind == DiffChangeKind.Removed);
        Assert.Contains("[1]", removedLine.Text);
        Assert.Equal(["[0] a", "[1] b", "[2] c", "[10] d"], result.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Compare_Set_TreatsMemberAsIdentity()
    {
        var left = Collection("set", ("x", "", ""), ("y", "", ""));
        var right = Collection("set", ("x", "", ""));

        var result = KeyDiffEngine.Compare(left, right);

        Assert.Equal(1, result.Removed);
        Assert.Contains(result.Lines, l => l.Kind == DiffChangeKind.Removed && l.Text == "y");
    }

    [Fact]
    public void Compare_ZSet_ComparesScores()
    {
        var left = Collection("zset", ("a", "", "1"), ("b", "", "2"));
        var right = Collection("zset", ("a", "", "1"), ("b", "", "5"));

        var result = KeyDiffEngine.Compare(left, right);

        Assert.Equal(1, result.Modified);
        Assert.Contains(result.Lines, l => l.Kind == DiffChangeKind.Modified && l.Text.Contains("左：2") && l.Text.Contains("右：5"));
    }

    [Fact]
    public void ToPlainText_IncludesPrefixes()
    {
        var result = KeyDiffEngine.CompareText("a\nb", "a\nc");
        var text = result.ToPlainText();

        Assert.Contains("+ c", text);
        Assert.Contains("- b", text);
    }
}
