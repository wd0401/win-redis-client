using WinRedisClient.Core.Models;

namespace WinRedisClient.Core.Abstractions;

/// <summary>
/// Redis 客户端高层门面 — 封装会话生命周期、SCAN 浏览、元数据、值读取与受控写入。
/// UI 只通过本接口访问 Redis，避免在 ViewModel 中散落底层命令。
/// </summary>
public interface IRedisService : IAsyncDisposable
{
    bool IsConnected { get; }
    ConnectionProfile? CurrentProfile { get; }
    int Database { get; }

    Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task DisconnectAsync();

    /// <summary>切换当前 DB。返回是否切换成功（各 Redis 默认 0-15，受 databases 配置限制）。</summary>
    bool SelectDatabase(int db);

    Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default);
    Task<string?> GetServerVersionAsync(CancellationToken cancellationToken = default);
    Task<long> GetDatabaseSizeAsync(CancellationToken cancellationToken = default);

    /// <summary>读取各数据库的 Key 数量（INFO keyspace，一次调用返回全部非空 DB）。</summary>
    Task<IReadOnlyDictionary<int, long>> GetDatabaseKeyCountsAsync(CancellationToken cancellationToken = default);

    // ── 浏览 ────────────────────────────────────────────────
    Task<ScanPage> ScanPageAsync(
        string? pattern,
        int batchSize,
        long cursor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KeyMetadata>> GetMetadataBatchAsync(
        IReadOnlyList<string> keys,
        bool includeSize = true,
        CancellationToken cancellationToken = default);

    Task<KeyMetadata?> GetMetadataAsync(
        string key,
        bool includeSize = true,
        CancellationToken cancellationToken = default);

    Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default);

    Task<string?> GetKeyTypeAsync(string key, CancellationToken cancellationToken = default);

    // ── 值读取 ──────────────────────────────────────────────
    Task<ValuePreview?> PreviewAsync(
        string key,
        PreviewOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>读取 String 原始字节（超过 maxBytes 时截断，调用方可通过 PreviewAsync 获取完整长度）。</summary>
    Task<ValuePreview?> GetStringBytesAsync(
        string key,
        long maxBytes,
        CancellationToken cancellationToken = default);

    /// <summary>分页读取集合元素。type 为 hash/list/set/zset/stream。</summary>
    Task<CollectionPage> GetCollectionPageAsync(
        string key,
        string type,
        string? cursor,
        int count,
        bool descending = false,
        CancellationToken cancellationToken = default);

    /// <summary>读取用于对比/导出的完整 Key 内容（有上限，结果带 IsTruncated）。</summary>
    Task<KeyContent> GetKeyContentAsync(
        string key,
        int maxElements,
        long maxBytes,
        CancellationToken cancellationToken = default);

    Task<BigKeyInfo?> GetBigKeyInfoAsync(string key, CancellationToken cancellationToken = default);

    // ── 命令与写入 ──────────────────────────────────────────
    Task<CommandResult> ExecuteAsync(
        string command,
        CommandRiskLevel risk = CommandRiskLevel.ReadOnly,
        bool confirmed = false,
        CancellationToken cancellationToken = default);

    Task<CommandResult> ExecuteArgsAsync(
        string command,
        string[] args,
        CommandRiskLevel risk = CommandRiskLevel.ReadOnly,
        bool confirmed = false,
        CancellationToken cancellationToken = default);

    /// <summary>保存 String；ttl 为 null 时保持原 TTL（若 preserveTtl）。</summary>
    Task<CommandResult> SetStringAsync(
        string key,
        byte[] value,
        TimeSpan? ttl = null,
        bool preserveTtl = true,
        bool confirmed = false,
        CancellationToken cancellationToken = default);

    /// <summary>新建 Key；已存在时不覆盖并返回 target_exists。</summary>
    Task<CommandResult> CreateKeyAsync(
        string key,
        string type,
        IReadOnlyList<string> initialValues,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    Task<CommandResult> DeleteKeyAsync(string key, bool confirmed = false, CancellationToken cancellationToken = default);

    /// <summary>安全重命名（RENAMENX）；目标存在时返回 target_exists。</summary>
    Task<CommandResult> RenameKeyAsync(string oldKey, string newKey, bool confirmed = false, CancellationToken cancellationToken = default);

    /// <summary>设置 TTL；ttl 为 null 表示永不过期（PERSIST）。</summary>
    Task<CommandResult> SetTtlAsync(string key, TimeSpan? ttl, CancellationToken cancellationToken = default);

    /// <summary>按索引删除 List 元素（服务端脚本，避免误删同值元素）。</summary>
    Task<CommandResult> ListRemoveAtAsync(string key, long index, bool confirmed = false, CancellationToken cancellationToken = default);

    Task<CommandResult> StreamAddAsync(
        string key,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken = default);

    // ── 监控 ────────────────────────────────────────────────
    Task<InfoSnapshot> GetInfoAsync(string? section = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SlowLogEntry>> GetSlowLogAsync(int count = 20, CancellationToken cancellationToken = default);
}

/// <summary>一页 SCAN 结果。</summary>
public sealed record ScanPage(
    IReadOnlyList<KeyScanResult> Keys,
    long NextCursor,
    bool IsComplete);

/// <summary>聚合类型的单个元素。Field 对 List 为索引、对 Stream 为消息 ID。</summary>
public sealed record ElementData(
    string Field,
    string Value,
    string Score);
