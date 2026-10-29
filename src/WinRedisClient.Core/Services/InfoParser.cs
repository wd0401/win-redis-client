using System.Globalization;
using WinRedisClient.Core.Models;

namespace WinRedisClient.Core.Services;

/// <summary>解析 INFO 文本为 Section 和扁平字段（保留原始文本用于复制）。</summary>
public static class InfoParser
{
    public static InfoSnapshot Parse(string rawText, DateTimeOffset? retrievedAt = null)
    {
        var sections = new List<InfoSection>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentName = "General";
        var currentEntries = new List<KeyValuePair<string, string>>();

        void Flush()
        {
            if (currentEntries.Count > 0 || sections.Count > 0)
                sections.Add(new InfoSection(currentName, currentEntries));
        }

        foreach (var rawLine in rawText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('#'))
            {
                Flush();
                currentName = line.TrimStart('#', ' ').Trim();
                currentEntries = [];
                continue;
            }

            var idx = line.IndexOf(':');
            if (idx <= 0) continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            currentEntries.Add(new KeyValuePair<string, string>(key, value));
            fields[key] = value;
        }

        Flush();

        return new InfoSnapshot(
            rawText,
            fields,
            sections,
            retrievedAt ?? DateTimeOffset.Now);
    }

    /// <summary>从 INFO keyspace 文本解析各 DB 的 Key 数量；缺失或非数字条目跳过。</summary>
    public static IReadOnlyDictionary<int, long> ParseDatabaseKeyCounts(string rawText)
    {
        var counts = new Dictionary<int, long>();
        var snapshot = Parse(rawText);

        foreach (var (field, value) in snapshot.Fields)
        {
            if (!field.StartsWith("db", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(field.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var database))
            {
                continue;
            }

            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!part.StartsWith("keys=", StringComparison.OrdinalIgnoreCase)) continue;
                if (long.TryParse(part.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                    counts[database] = count;
                break;
            }
        }

        return counts;
    }
}
