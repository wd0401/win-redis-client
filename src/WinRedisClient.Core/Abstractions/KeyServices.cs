namespace WinRedisClient.Core.Abstractions;

/// <summary>增量 Key 扫描、游标管理、取消与进度。</summary>
public interface IKeyScanService
{
    IAsyncEnumerable<KeyScanResult> ScanAsync(
        IRedisSession session,
        string? pattern,
        int batchSize,
        CancellationToken cancellationToken = default);
}

/// <summary>扫描到的单个 Key。</summary>
public sealed record KeyScanResult(byte[] KeyBytes, string DisplayKey);

/// <summary>批量读取类型、TTL 等元数据。</summary>
public interface IKeyMetadataService
{
    Task<IReadOnlyList<KeyMetadata>> GetMetadataAsync(
        IRedisSession session,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default);
}

/// <summary>Key 元数据。</summary>
public sealed record KeyMetadata(
    string Key,
    string Type,
    TimeSpan? TimeToLive,
    DateTimeOffset RetrievedAt,
    string? Error = null,
    long? SizeBytes = null,
    long? ElementCount = null);

/// <summary>一页集合元素。Cursor 由服务层解释，UI 不解析。</summary>
public sealed record CollectionPage(
    IReadOnlyList<ElementData> Items,
    string? NextCursor,
    bool IsComplete,
    long TotalCount,
    string? Error = null);

/// <summary>Key 完整内容（用于对比，受大小限制）。</summary>
public sealed record KeyContent(
    string Key,
    string Type,
    TimeSpan? TimeToLive,
    string? Text,
    IReadOnlyList<ElementData> Elements,
    bool IsTruncated,
    string? Error = null);

/// <summary>BigKey 大小信息。</summary>
public sealed record BigKeyInfo(
    string Key,
    string Type,
    long? MemoryBytes,
    long? ElementCount);

/// <summary>按需预览与分块读取。</summary>
public interface IValuePreviewService
{
    Task<ValuePreview> PreviewAsync(
        IRedisSession session,
        string key,
        PreviewOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>预览选项。</summary>
public sealed record PreviewOptions
{
    public long MaxBytes { get; init; } = 128 * 1024;
    public long ChunkSize { get; init; } = 64 * 1024;
    public long Offset { get; init; }
}

/// <summary>预览结果。</summary>
public sealed record ValuePreview(
    long TotalLength,
    byte[] Data,
    long Offset,
    bool IsTruncated,
    bool IsPartialRead);
