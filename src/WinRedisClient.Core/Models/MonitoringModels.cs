namespace WinRedisClient.Core.Models;

/// <summary>INFO 的单个 Section。</summary>
public sealed record InfoSection(
    string Name,
    IReadOnlyList<KeyValuePair<string, string>> Entries);

/// <summary>一次 INFO 快照。</summary>
public sealed record InfoSnapshot(
    string RawText,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<InfoSection> Sections,
    DateTimeOffset RetrievedAt)
{
    public string? Get(string key) => Fields.TryGetValue(key, out var value) ? value : null;

    public long? GetLong(string key)
        => long.TryParse(Get(key), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public double? GetDouble(string key)
        => double.TryParse(Get(key), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}

/// <summary>SlowLog 条目。</summary>
public sealed record SlowLogEntry(
    long Id,
    DateTimeOffset Timestamp,
    TimeSpan Duration,
    IReadOnlyList<string> Command,
    string? ClientAddress,
    string? ClientName)
{
    public string CommandText => Command.Count == 0 ? "(empty)" : string.Join(' ', Command);
}

/// <summary>监控采样点。</summary>
public sealed record MetricSample(
    DateTimeOffset Timestamp,
    double UsedMemoryMb,
    double OpsPerSec,
    double ConnectedClients,
    double HitRate,
    long KeyCount);
