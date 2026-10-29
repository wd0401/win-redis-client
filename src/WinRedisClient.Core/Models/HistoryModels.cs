namespace WinRedisClient.Core.Models;

/// <summary>最近访问的 Key（不保存 Value）。</summary>
public sealed record RecentKeyEntry(
    Guid ConnectionId,
    string ConnectionName,
    int Database,
    string Key,
    DateTimeOffset LastAccessed);

/// <summary>本地命令历史（不保存认证参数）。</summary>
public sealed record CommandHistoryEntry(
    string Command,
    DateTimeOffset ExecutedAt,
    Guid ConnectionId,
    int Database);
