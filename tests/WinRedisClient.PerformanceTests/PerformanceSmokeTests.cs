using System.Diagnostics;
using WinRedisClient.Core.Services;

namespace WinRedisClient.PerformanceTests;

/// <summary>关键纯计算路径的性能冒烟测试（阈值宽松，只用于发现数量级退化）。</summary>
public class PerformanceSmokeTests
{
    [Fact]
    public void CommandLineParser_ParsesManyCommandsQuickly()
    {
        const int iterations = 20_000;
        var input = "SET \"user:10086:profile\" '{\"name\":\"Alice\",\"city\":\"Shanghai\"}' EX 3600";

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var tokens = CommandLineParser.Parse(input);
            if (tokens.Count != 5) throw new InvalidOperationException("分词结果不正确");
        }
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"解析 {iterations} 条命令耗时 {sw.ElapsedMilliseconds} ms，超出预期");
    }

    [Fact]
    public void KeyDiffEngine_LargeTextDiffCompletesQuickly()
    {
        var left = string.Join('\n', Enumerable.Range(0, 3000).Select(i => $"line-{i}"));
        var right = left.Replace("line-1000", "line-1000-changed", StringComparison.Ordinal);

        var sw = Stopwatch.StartNew();
        var result = KeyDiffEngine.CompareText(left, right);
        sw.Stop();

        Assert.True(result.HasChanges);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"3000 行文本对比耗时 {sw.ElapsedMilliseconds} ms，超出预期");
    }

    [Fact]
    public void KeyDiffEngine_CollectionDiffScalesLinearly()
    {
        var left = BuildHash(20_000, mutateEvery: 0);
        var right = BuildHash(20_000, mutateEvery: 10);

        var sw = Stopwatch.StartNew();
        var result = KeyDiffEngine.Compare(left, right);
        sw.Stop();

        Assert.Equal(2_000, result.Modified);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"20000 字段 Hash 对比耗时 {sw.ElapsedMilliseconds} ms，超出预期");
    }

    private static WinRedisClient.Core.Abstractions.KeyContent BuildHash(int count, int mutateEvery)
    {
        var items = new List<WinRedisClient.Core.Abstractions.ElementData>(count);
        for (var i = 0; i < count; i++)
        {
            var value = mutateEvery > 0 && i % mutateEvery == 0 ? $"value-{i}-changed" : $"value-{i}";
            items.Add(new WinRedisClient.Core.Abstractions.ElementData($"field-{i}", value, string.Empty));
        }

        return new WinRedisClient.Core.Abstractions.KeyContent(
            "bench", "hash", null, null, items, false);
    }
}
