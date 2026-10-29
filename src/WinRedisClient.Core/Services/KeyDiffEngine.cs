using System.Globalization;
using WinRedisClient.Core.Abstractions;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Core.Services;

public enum DiffChangeKind
{
    Context,
    Added,
    Removed,
    Modified,
}

public sealed record DiffLine(DiffChangeKind Kind, string Text);

public sealed record KeyDiffResult(
    string Summary,
    bool TypeMismatch,
    IReadOnlyList<DiffLine> Lines,
    int Added,
    int Removed,
    int Modified)
{
    public bool HasChanges => Added + Removed + Modified > 0;

    public string ToPlainText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Summary);
        foreach (var line in Lines)
        {
            var prefix = line.Kind switch
            {
                DiffChangeKind.Added => "+ ",
                DiffChangeKind.Removed => "- ",
                DiffChangeKind.Modified => "~ ",
                _ => "  ",
            };
            sb.AppendLine(prefix + line.Text);
        }
        return sb.ToString();
    }
}

/// <summary>类型感知的 Key 内容对比。</summary>
public static class KeyDiffEngine
{
    private const int MaxDiffLines = 1500;

    public static KeyDiffResult Compare(KeyContent left, KeyContent right)
    {
        if (!string.IsNullOrEmpty(left.Error) || !string.IsNullOrEmpty(right.Error))
        {
            var message = string.Join("；",
                new[] { left.Error, right.Error }.Where(e => !string.IsNullOrEmpty(e)));
            return new KeyDiffResult(Loc.F("读取失败：{0}", message), false,
                [new DiffLine(DiffChangeKind.Context, Loc.T("请刷新后重试"))], 0, 0, 0);
        }

        if (!string.Equals(left.Type, right.Type, StringComparison.OrdinalIgnoreCase))
        {
            var lines = new List<DiffLine>
            {
                new(DiffChangeKind.Removed, Loc.F("左侧类型：{0}", left.Type)),
                new(DiffChangeKind.Added, Loc.F("右侧类型：{0}", right.Type)),
                new(DiffChangeKind.Context, Loc.T("类型不同，未进行内容比较")),
            };
            return new KeyDiffResult(
                Loc.F("类型不同（{0} / {1}），未进行内容比较", left.Type, right.Type),
                true, lines, 0, 0, 0);
        }

        return left.Type switch
        {
            "string" => CompareText(left.Text ?? string.Empty, right.Text ?? string.Empty,
                left.IsTruncated || right.IsTruncated),
            "hash" or "set" or "zset" or "list" or "stream" =>
                CompareElements(left, right),
            _ => new KeyDiffResult(Loc.F("不支持比较的类型：{0}", left.Type), false,
                [new DiffLine(DiffChangeKind.Context, Loc.T("当前类型暂不支持内容比较"))], 0, 0, 0),
        };
    }

    public static KeyDiffResult CompareText(string left, string right, bool truncated = false)
    {
        var leftLines = SplitLines(left);
        var rightLines = SplitLines(right);

        if (leftLines.SequenceEqual(rightLines))
        {
            var note = truncated ? Loc.T("内容一致（对比内容已按大小限制截断）") : Loc.T("内容一致，无差异");
            return new KeyDiffResult(note, false,
                [new DiffLine(DiffChangeKind.Context, note)], 0, 0, 0);
        }

        var cap = Math.Min(MaxDiffLines, Math.Max(leftLines.Count, rightLines.Count));
        var a = leftLines.Take(cap).ToArray();
        var b = rightLines.Take(cap).ToArray();

        var table = BuildLcsTable(a, b);
        var lines = new List<DiffLine>();
        var added = 0;
        var removed = 0;
        int i = 0, j = 0;

        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j])
            {
                lines.Add(new DiffLine(DiffChangeKind.Context, a[i]));
                i++;
                j++;
            }
            else if (table[i + 1, j] >= table[i, j + 1])
            {
                lines.Add(new DiffLine(DiffChangeKind.Removed, a[i]));
                removed++;
                i++;
            }
            else
            {
                lines.Add(new DiffLine(DiffChangeKind.Added, b[j]));
                added++;
                j++;
            }
        }

        while (i < a.Length)
        {
            lines.Add(new DiffLine(DiffChangeKind.Removed, a[i++]));
            removed++;
        }

        while (j < b.Length)
        {
            lines.Add(new DiffLine(DiffChangeKind.Added, b[j++]));
            added++;
        }

        var suffix = truncated || leftLines.Count > cap || rightLines.Count > cap
            ? Loc.T("（内容已截断，仅比较前 1500 行）")
            : string.Empty;
        var summary = Loc.F("新增 {0} 行，删除 {1} 行{2}", added, removed, suffix);
        return new KeyDiffResult(summary, false, lines, added, removed, 0);
    }

    private static KeyDiffResult CompareElements(KeyContent left, KeyContent right)
    {
        var leftMap = ToMap(left);
        var rightMap = ToMap(right);

        var showContext = leftMap.Count + rightMap.Count <= 400;
        var keys = leftMap.Keys.Union(rightMap.Keys).ToList();
        if (left.Type == "list")
            keys.Sort(CompareListIndices);
        else
            keys.Sort(StringComparer.Ordinal);

        var lines = new List<DiffLine>();
        var added = 0;
        var removed = 0;
        var modified = 0;
        var same = 0;

        foreach (var key in keys)
        {
            var hasLeft = leftMap.TryGetValue(key, out var lv);
            var hasRight = rightMap.TryGetValue(key, out var rv);

            if (hasLeft && hasRight)
            {
                if (string.Equals(lv, rv, StringComparison.Ordinal))
                {
                    same++;
                    if (showContext) lines.Add(new DiffLine(DiffChangeKind.Context, Format(left.Type, key, lv!)));
                }
                else
                {
                    modified++;
                    lines.Add(new DiffLine(DiffChangeKind.Modified,
                        Loc.F("{0} · 左：{1} · 右：{2}", FormatKey(left.Type, key), lv, rv)));
                }
            }
            else if (hasLeft)
            {
                removed++;
                lines.Add(new DiffLine(DiffChangeKind.Removed, Format(left.Type, key, lv!)));
            }
            else
            {
                added++;
                lines.Add(new DiffLine(DiffChangeKind.Added, Format(right.Type, key, rv!)));
            }
        }

        var truncation = left.IsTruncated || right.IsTruncated
            ? Loc.T("（内容受读取上限影响，结果可能不完整）")
            : string.Empty;
        var summary = added + removed + modified == 0
            ? Loc.F("内容一致 · 共 {0} 个元素{1}", same, truncation)
            : Loc.F("新增 {0} · 删除 {1} · 修改 {2}{3}", added, removed, modified, truncation);

        if (lines.Count == 0)
            lines.Add(new DiffLine(DiffChangeKind.Context, Loc.T("两侧均为空")));

        return new KeyDiffResult(summary, false, lines, added, removed, modified);
    }

    private static Dictionary<string, string> ToMap(KeyContent content)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in content.Elements)
        {
            map[item.Field] = content.Type switch
            {
                "zset" => item.Score,
                "set" => item.Field,
                _ => item.Value,
            };
        }
        return map;
    }

    private static string Format(string type, string key, string value) => type switch
    {
        "zset" => $"{key} · score {value}",
        "set" => key,
        "list" => $"[{key}] {value}",
        "stream" => $"{key}  {value}",
        _ => $"{key} = {value}",
    };

    private static string FormatKey(string type, string key) => type switch
    {
        "list" => $"[{key}]",
        "set" => key,
        _ => key,
    };

    private static int CompareListIndices(string x, string y)
    {
        var lx = long.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a);
        var ly = long.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b);
        if (lx && ly) return a.CompareTo(b);
        return string.CompareOrdinal(x, y);
    }

    private static List<string> SplitLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

    private static int[,] BuildLcsTable(string[] a, string[] b)
    {
        var table = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                table[i, j] = a[i] == b[j]
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }
        return table;
    }
}
